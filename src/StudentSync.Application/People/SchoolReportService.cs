using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;

namespace StudentSync.Application.People;

/// <summary>
/// School results a student shares with the centre (overall %, marks per subject, the scanned report). Admins record and
/// remove them; anyone who may view the student (admins, the student's teachers, the student) can read them.
/// </summary>
public sealed class SchoolReportService(IAppDbContext db, ICurrentUser user, AccessPolicy access, FileService files, IClock clock, AuditService audit)
{
    public const int MaxMarksPerReport = 30;

    public async Task<IReadOnlyList<SchoolReportDto>> ListAsync(Guid studentId, CancellationToken ct)
    {
        await access.EnsureCanViewStudentAsync(studentId, ct);
        var rows = await db.StudentSchoolReports.Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.ExamDate).ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.ExamDate,
                r.OverallPercent,
                r.Notes,
                r.CreatedAtUtc,
                Marks = r.Marks.OrderBy(m => m.SortOrder).Select(m => new SchoolMarkDto(m.Subject, m.Marks, m.MaxMarks, m.Grade)).ToList(),
                File = db.Files.Where(f => f.Id == r.FileId && f.DeletedAtUtc == null).Select(f => new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes)).FirstOrDefault(),
            })
            .Take(50).ToListAsync(ct);
        return rows.Select(r => new SchoolReportDto(r.Id, r.Title, r.ExamDate, r.OverallPercent, r.Notes, r.Marks, r.File, r.CreatedAtUtc)).ToList();
    }

    public async Task<Guid> AddAsync(Guid studentId, SaveSchoolReportRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student");
        var report = await BuildAsync(studentId, r, ct);
        await db.SaveChangesAsync(ct);
        return report.Id;
    }

    /// <summary>Validates a report and adds it to the context; the caller saves (admission saves it with the new student).</summary>
    public async Task<StudentSchoolReport> BuildAsync(Guid studentId, SaveSchoolReportRequest r, CancellationToken ct)
    {
        var report = new StudentSchoolReport { StudentId = studentId, RecordedByUserId = user.UserId };
        Apply(report, r);
        report.FileId = await ClaimFileAsync(r.FileId, ct);
        db.StudentSchoolReports.Add(report);
        audit.Record("student.school_report_added", "Student", studentId, report.Title);
        return report;
    }

    /// <summary>
    /// Corrects a report: title, date, overall %, notes and marks are replaced. A new <c>FileId</c> replaces the scan (the old
    /// one is removed); leaving it empty keeps the current scan.
    /// </summary>
    public async Task UpdateAsync(Guid studentId, Guid reportId, SaveSchoolReportRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var report = await db.StudentSchoolReports.Include(x => x.Marks).FirstOrDefaultAsync(x => x.Id == reportId && x.StudentId == studentId, ct)
                     ?? throw new NotFoundException("School report");
        db.StudentSchoolMarks.RemoveRange(report.Marks);
        report.Marks.Clear();
        Apply(report, r);
        if (r.FileId is { } newFile && newFile != report.FileId)
        {
            var claimed = await ClaimFileAsync(newFile, ct);
            if (report.FileId is { } old && await db.Files.FirstOrDefaultAsync(x => x.Id == old, ct) is { } oldFile) oldFile.DeletedAtUtc = clock.UtcNow;
            report.FileId = claimed;
        }
        audit.Record("student.school_report_updated", "Student", studentId, report.Title);
        await db.SaveChangesAsync(ct);
    }

    private void Apply(StudentSchoolReport report, SaveSchoolReportRequest r)
    {
        if (r.OverallPercent is < 0 or > 100) throw RequestValidationException.For("overallPercent", "The overall percentage must be between 0 and 100.");
        if (r.ExamDate is { } d && d > DateOnly.FromDateTime(clock.UtcNow).AddDays(31)) throw RequestValidationException.For("examDate", "The exam date is in the future.");
        var marks = r.Marks ?? [];
        if (marks.Count > MaxMarksPerReport) throw RequestValidationException.For("marks", $"Add at most {MaxMarksPerReport} subjects.");
        report.Title = Text.Required(r.Title, "title", 150);
        report.ExamDate = r.ExamDate;
        report.OverallPercent = r.OverallPercent;
        report.Notes = Text.Optional(r.Notes, "notes", 1000);
        foreach (var (m, i) in marks.Select((m, i) => (m, i)))
        {
            var subject = Text.Required(m.Subject, "marks", 100);
            if (m.Marks is < 0 || m.MaxMarks is <= 0 || (m.Marks is { } got && m.MaxMarks is { } max && got > max))
                throw RequestValidationException.For("marks", $"Check the marks for {subject}.");
            report.Marks.Add(new StudentSchoolMark { Subject = subject, Marks = m.Marks, MaxMarks = m.MaxMarks, Grade = Text.Optional(m.Grade, "marks", 10), SortOrder = i });
        }
    }

    private async Task<Guid?> ClaimFileAsync(Guid? fileId, CancellationToken ct)
    {
        var fileIds = await files.ClaimAsync(fileId is { } f ? [f] : null, FilePurpose.SchoolReport, ct);
        if (fileIds.Count > 0 && await db.StudentSchoolReports.AnyAsync(x => x.FileId == fileIds[0], ct))
            throw RequestValidationException.For("fileId", "This file is already attached to another report.");
        return fileIds.Count > 0 ? fileIds[0] : null;
    }

    public async Task DeleteAsync(Guid studentId, Guid reportId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var report = await db.StudentSchoolReports.Include(x => x.Marks).FirstOrDefaultAsync(x => x.Id == reportId && x.StudentId == studentId, ct)
                     ?? throw new NotFoundException("School report");
        if (report.FileId is { } fileId && await db.Files.FirstOrDefaultAsync(x => x.Id == fileId, ct) is { } file) file.DeletedAtUtc = clock.UtcNow;
        db.StudentSchoolReports.Remove(report);
        audit.Record("student.school_report_removed", "Student", studentId, report.Title);
        await db.SaveChangesAsync(ct);
    }
}
