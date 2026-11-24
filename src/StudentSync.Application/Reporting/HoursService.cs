using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.People;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

using StudentSync.Domain.People;
namespace StudentSync.Application.Reporting;

/// <summary>
/// Attendance and hours for one student or one teacher ("This month" in the centre's time zone, and "All time"), the
/// teacher behind each of a student's subjects, and the students a teacher teaches. Definitions are on
/// <see cref="HoursStatsDto"/>. Access: a student sees themselves, a teacher their own students and themselves, an
/// admin everyone; anyone else gets "not found".
/// </summary>
public sealed class HoursService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, PeopleService people, PhotoUrls photos)
{
    private sealed record Row(Guid SessionId, DateTime StartUtc, DateTime EndUtc, SessionStatus Status, CancellationKind? CancelledBecause,
        AttendanceStatus? Mark, Guid? SubjectId, Guid TeacherId);

    public async Task<StudentHoursDto> StudentAsync(Guid studentId, CancellationToken ct)
    {
        await access.EnsureCanViewStudentAsync(studentId, ct);
        // Every class the student was booked into (current or removed later) with their attendance mark, if any.
        var rows = await (from p in db.SessionParticipants
                          where p.StudentId == studentId
                          join s in db.Sessions on p.SessionId equals s.Id
                          let mark = db.Attendance.Where(a => a.SessionId == s.Id && a.StudentId == studentId).Select(a => (AttendanceStatus?)a.Status).FirstOrDefault()
                          where p.Status == ParticipantStatus.Active || mark != null
                          select new Row(s.Id, s.StartUtc, s.EndUtc, s.Status, s.CancelledBecause, mark, s.SubjectId, s.TeacherId))
            .Take(5000).ToListAsync(ct);

        var monthStart = await MonthStartUtcAsync(ct);
        var now = clock.UtcNow;
        var periods = new List<PeriodHoursDto>
        {
            new("This month", StudentStats(rows.Where(r => r.StartUtc >= monthStart && r.StartUtc < now), now, includeUpcoming: false)),
            new("All time", StudentStats(rows, now, includeUpcoming: true)),
        };

        // Subjects: the ones chosen for the student plus any they have had classes in; teachers who taught them, else
        // the subject's assigned teachers.
        var chosen = await db.StudentSubjects.Where(x => x.StudentId == studentId).Select(x => x.SubjectId).ToListAsync(ct);
        var subjectIds = chosen.Concat(rows.Where(r => r.SubjectId != null && r.Status != SessionStatus.Cancelled).Select(r => r.SubjectId!.Value)).Distinct().ToList();
        var subjects = await db.Subjects.Where(s => subjectIds.Contains(s.Id)).Select(s => new { s.Id, s.Name, Course = s.Course!.Name }).ToListAsync(ct);
        var assigned = await db.TeacherSubjects.Where(t => subjectIds.Contains(t.SubjectId) && t.Teacher!.Status != PersonStatus.Archived)
            .Select(t => new { t.SubjectId, t.TeacherId, t.Teacher!.User!.FullName }).ToListAsync(ct);
        var teacherIds = rows.Select(r => r.TeacherId).Distinct().ToList();
        // A teacher who was deleted since is shown as "Former teacher" on the classes they taught this student.
        var teacherNames = await db.Teachers.Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, Name = t.Status == PersonStatus.Archived ? DeletedPeople.TeacherName : t.User!.FullName }).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var bySubject = subjects.Select(s =>
        {
            var done = rows.Where(r => r.SubjectId == s.Id && Attended(r)).ToList();
            var taughtBy = rows.Where(r => r.SubjectId == s.Id && r.Status != SessionStatus.Cancelled).Select(r => r.TeacherId).Distinct()
                .Select(id => new LookupItem(id, teacherNames.GetValueOrDefault(id, "Teacher"), "teaches this student")).ToList();
            var teachers = taughtBy.Count > 0 ? taughtBy
                : assigned.Where(a => a.SubjectId == s.Id).Select(a => new LookupItem(a.TeacherId, a.FullName, "teaches this subject")).ToList();
            return new SubjectTeachersDto(s.Id, s.Name, s.Course, teachers, done.Count, done.Sum(r => TeacherPayRules.Hours(r.StartUtc, r.EndUtc)));
        }).OrderByDescending(x => x.HoursDone).ThenBy(x => x.SubjectName).ToList();

        return new StudentHoursDto(studentId, periods, bySubject);
    }

    public async Task<TeacherOverviewDto> TeacherAsync(Guid teacherId, CancellationToken ct)
    {
        // Admins, or the teacher themselves: the overview lists the teacher's students, which other teachers must not see.
        if (!(access.IsTeacher && teacherId == user.UserId)) access.Require(Permissions.UsersManage);
        var teacher = await people.TeacherAsync(teacherId, ct);
        var rows = await db.Sessions.Where(s => s.TeacherId == teacherId)
            .Select(s => new Row(s.Id, s.StartUtc, s.EndUtc, s.Status, s.CancelledBecause, null, s.SubjectId, s.TeacherId))
            .Take(10000).ToListAsync(ct);
        var marks = await (from a in db.Attendance join s in db.Sessions on a.SessionId equals s.Id where s.TeacherId == teacherId select new { a.SessionId, a.Status })
            .ToListAsync(ct);

        var monthStart = await MonthStartUtcAsync(ct);
        var now = clock.UtcNow;
        var periods = new List<PeriodHoursDto>
        {
            new("This month", TeacherStats(rows.Where(r => r.StartUtc >= monthStart && r.StartUtc < now).ToList(), marks.Select(m => (m.SessionId, m.Status)), now, includeUpcoming: false)),
            new("All time", TeacherStats(rows, marks.Select(m => (m.SessionId, m.Status)), now, includeUpcoming: true)),
        };

        // The students they teach: classes and hours done together, subjects, and the next class.
        var students = await (from p in db.SessionParticipants
                              where p.Status == ParticipantStatus.Active
                              join s in db.Sessions on p.SessionId equals s.Id
                              where s.TeacherId == teacherId && s.Status != SessionStatus.Cancelled
                              select new { p.StudentId, s.Id, s.StartUtc, s.EndUtc, s.Status, Subject = s.SubjectId != null ? db.Subjects.Where(x => x.Id == s.SubjectId).Select(x => x.Name).FirstOrDefault() : null })
            .Take(10000).ToListAsync(ct);
        var ids = students.Select(x => x.StudentId).Distinct().ToList();
        var info = await db.Students.Where(s => ids.Contains(s.Id) && s.Status != PersonStatus.Archived) // a deleted student is no longer "their student"
            .Select(s => new { s.Id, s.User!.FullName, s.User.PhotoFileId, Grade = db.GradeLevels.Where(g => g.Id == s.GradeLevelId).Select(g => g.Name).FirstOrDefault() })
            .ToListAsync(ct);
        var list = info.Select(s =>
        {
            var mine = students.Where(x => x.StudentId == s.Id).ToList();
            var done = mine.Where(x => x.Status == SessionStatus.Completed).ToList();
            var next = mine.Where(x => x.Status is SessionStatus.Scheduled or SessionStatus.Live && x.EndUtc > now).OrderBy(x => x.StartUtc).FirstOrDefault();
            return new TeacherStudentDto(s.Id, s.FullName, photos.For(s.PhotoFileId), s.Grade,
                mine.Select(x => x.Subject).OfType<string>().Distinct().OrderBy(x => x).ToList(),
                done.Count, done.Sum(x => TeacherPayRules.Hours(x.StartUtc, x.EndUtc)), next?.StartUtc, next?.Id);
        }).OrderBy(x => x.NextClassUtc ?? DateTime.MaxValue).ThenBy(x => x.Name).ToList();

        return new TeacherOverviewDto(teacher, periods, list);
    }

    // ---------------- rules ----------------

    private static bool Attended(Row r) => r.Status != SessionStatus.Cancelled && r.Mark is AttendanceStatus.Present or AttendanceStatus.Late;

    private static HoursStatsDto StudentStats(IEnumerable<Row> rows, DateTime now, bool includeUpcoming)
    {
        var list = rows.ToList();
        var live = list.Where(r => r.Status != SessionStatus.Cancelled).ToList();
        var done = live.Where(Attended).ToList();
        var absent = live.Where(r => r.Mark == AttendanceStatus.Absent).ToList();
        var cancelledForStudent = list.Where(r => r.Status == SessionStatus.Cancelled && r.CancelledBecause == CancellationKind.StudentAbsent).ToList();
        var missed = absent.Concat(cancelledForStudent).ToList();
        return new HoursStatsDto(done.Count, Sum(done), missed.Count, Sum(missed),
            live.Count(r => r.Mark == AttendanceStatus.Present), live.Count(r => r.Mark == AttendanceStatus.Late), absent.Count, live.Count(r => r.Mark == AttendanceStatus.Excused),
            live.Count(r => r.Mark == null && r.EndUtc <= now),
            includeUpcoming ? live.Count(r => r.Status is SessionStatus.Scheduled or SessionStatus.Live && r.EndUtc > now) : 0);
    }

    private static HoursStatsDto TeacherStats(List<Row> rows, IEnumerable<(Guid SessionId, AttendanceStatus Status)> marks, DateTime now, bool includeUpcoming)
    {
        var ids = rows.Select(r => r.SessionId).ToHashSet();
        var mine = marks.Where(m => ids.Contains(m.SessionId)).ToList();
        var done = rows.Where(r => r.Status == SessionStatus.Completed).ToList();
        var missed = rows.Where(r => r.Status == SessionStatus.Cancelled && r.CancelledBecause == CancellationKind.TeacherAbsent).ToList();
        return new HoursStatsDto(done.Count, Sum(done), missed.Count, Sum(missed),
            mine.Count(m => m.Status == AttendanceStatus.Present), mine.Count(m => m.Status == AttendanceStatus.Late),
            mine.Count(m => m.Status == AttendanceStatus.Absent), mine.Count(m => m.Status == AttendanceStatus.Excused),
            rows.Count(r => r.Status is SessionStatus.Scheduled or SessionStatus.Live && r.EndUtc <= now),
            includeUpcoming ? rows.Count(r => r.Status is SessionStatus.Scheduled or SessionStatus.Live && r.EndUtc > now) : 0);
    }

    private static decimal Sum(IEnumerable<Row> rows) => rows.Sum(r => TeacherPayRules.Hours(r.StartUtc, r.EndUtc));

    /// <summary>Midnight on the 1st of this month in the centre's time zone, as UTC.</summary>
    private async Task<DateTime> MonthStartUtcAsync(CancellationToken ct)
    {
        var tz = await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.DefaultTimeZoneId).FirstOrDefaultAsync(ct) ?? "UTC";
        var local = ZonedTime.ToLocal(clock.UtcNow, tz);
        return ZonedTime.ToUtc(new DateOnly(local.Year, local.Month, 1), TimeOnly.MinValue, tz).Utc;
    }
}
