using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Scheduling;

/// <summary>Attendance works identically for one student or many; there is one row per (session, student).</summary>
public sealed class AttendanceService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify)
{
    public async Task<AttendanceRosterDto> RosterAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(sessionId, ct);
        var canEdit = access.CanManageSession(s) && user.Has(Permissions.AttendanceMark) && s.Status != SessionStatus.Cancelled;
        var participants = db.SessionParticipants.Where(p => p.SessionId == s.Id && p.Status == ParticipantStatus.Active);
        if (!access.CanManageSession(s))
        {
            var own = access.IsParent ? await access.ChildIdsAsync(ct) : [user.UserId];
            participants = participants.Where(p => own.Contains(p.StudentId));
        }
        var rows = await participants.OrderBy(p => p.Student!.User!.FullName)
            .Select(p => new
            {
                p.StudentId,
                Name = p.Student!.User!.FullName,
                Record = db.Attendance.Where(a => a.SessionId == s.Id && a.StudentId == p.StudentId).Select(a => new { a.Status, a.Note, a.MarkedAtUtc }).FirstOrDefault(),
            }).ToListAsync(ct);
        return new AttendanceRosterDto(s.Id, s.Status, canEdit,
            rows.Select(r => new AttendanceRowDto(r.StudentId, r.Name, r.Record?.Status, canEdit ? r.Record?.Note : null, r.Record?.MarkedAtUtc)).ToList());
    }

    public async Task MarkAsync(Guid sessionId, MarkAttendanceRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AttendanceMark);
        var s = await access.GetManageableSessionAsync(sessionId, ct, includeParticipants: true);
        if (s.Status == SessionStatus.Cancelled) throw new DomainException("Attendance cannot be taken for a cancelled session.", "invalid_session_state");
        if (s.StartUtc > clock.UtcNow.AddMinutes(30)) throw new DomainException("Attendance opens 30 minutes before the session starts.", "attendance_not_open");
        if (r.Entries.Count == 0) throw RequestValidationException.For("entries", "Mark at least one student.");
        if (r.Entries.Select(e => e.StudentId).Distinct().Count() != r.Entries.Count) throw RequestValidationException.For("entries", "Each student may appear only once.");
        var active = s.Participants.Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToHashSet();
        if (r.Entries.Any(e => !active.Contains(e.StudentId))) throw RequestValidationException.For("entries", "Attendance can only be marked for session participants.");

        var existing = await db.Attendance.Where(a => a.SessionId == s.Id).ToDictionaryAsync(a => a.StudentId, ct);
        var absentees = new List<Guid>();
        foreach (var e in r.Entries)
        {
            if (!existing.TryGetValue(e.StudentId, out var rec))
                db.Attendance.Add(rec = new AttendanceRecord { SessionId = s.Id, StudentId = e.StudentId });
            if (e.Status == AttendanceStatus.Absent && rec.Status != AttendanceStatus.Absent) absentees.Add(e.StudentId);
            rec.Status = e.Status;
            rec.Note = Text.Optional(e.Note, "note", 500);
            rec.MarkedAtUtc = clock.UtcNow;
            rec.MarkedByUserId = user.UserId;
        }
        if (s.Status == SessionStatus.Scheduled && s.EndUtc <= clock.UtcNow) s.SetStatus(SessionStatus.Completed);
        audit.Record("attendance.marked", "LearningSession", s.Id, $"{r.Entries.Count} student(s)");

        // Guardians are told about absences only.
        foreach (var studentId in absentees)
        {
            var parents = await db.ParentLinks.Where(l => l.StudentId == studentId && l.IsVerified).Select(l => l.ParentId).ToListAsync(ct);
            await notify.NotifyAsync(parents, NotificationType.AttendanceMarked, "Absence recorded",
                $"{s.Title} on {SchedulingService.DescribeLocal(s.StartUtc, s.TimeZoneId)} was marked absent.", "/parent", $"absent:{s.Id}:{studentId}", ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
