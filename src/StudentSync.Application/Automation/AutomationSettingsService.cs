using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Platform;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Platform;

namespace StudentSync.Application.Automation;

/// <summary>Settings ▸ Automations (institution admins). Jobs read <see cref="CurrentAsync"/>, which never needs a saved row.</summary>
public sealed class AutomationSettingsService(IAppDbContext db, AccessPolicy access, AuditService audit, IClock clock,
    IntegrationSettingsService integrations)
{
    /// <summary>The institute's settings, or the defaults when it never saved any (no permission check: used by jobs).</summary>
    public async Task<AutomationSettings> CurrentAsync(CancellationToken ct) =>
        await db.AutomationSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AutomationSettings();

    public async Task<AutomationSettingsDto> GetAsync(CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var s = await CurrentAsync(ct);
        var (email, sms, payments) = await integrations.ReadinessAsync(ct);
        var monthStart = new DateTime(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sent = await db.OutboundMessages.CountAsync(m => m.Status == OutboundStatus.Sent && m.SentAtUtc >= monthStart, ct);
        return new AutomationSettingsDto(s.AttendanceNudge, s.AttendanceNudgeAfterMinutes, s.AbsenceSms, s.AbsenceFlagAt,
            s.EveningTimetable, s.EveningTimetableAt, s.ClassDaySms, s.ScheduleRunningOut,
            s.LowHoursAlert, s.LowHoursAt, s.LowHoursSms,
            s.FeeReminders, s.FeeReminderDaysBefore, s.FeeReminderDaysAfter, s.FeeReminderSms, s.PaymentLinks, s.MonthEndSalaries,
            s.MorningSummary, s.MorningSummaryAt, s.MonthEndReport, s.ProgressReports, s.ProgressReportSms,
            s.AutoAiNotes, s.JobFailureAlerts, email, sms, payments, sent);
    }

    public async Task SaveAsync(SaveAutomationSettingsRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        if (r.AttendanceNudgeAfterMinutes is < 0 or > 720) throw RequestValidationException.For("attendanceNudgeAfterMinutes", "Use 0 to 720 minutes after the class ends.");
        if (r.AbsenceFlagAt is < 0 or > 31) throw RequestValidationException.For("absenceFlagAt", "Use 0 (off) to 31 absences.");
        if (r.LowHoursAt is < 0 or > 100) throw RequestValidationException.For("lowHoursAt", "Use 0 to 100 hours.");
        if (r.FeeReminderDaysBefore is < 0 or > 30) throw RequestValidationException.For("feeReminderDaysBefore", "Use 0 to 30 days.");
        if (r.FeeReminderDaysAfter is < 0 or > 60) throw RequestValidationException.For("feeReminderDaysAfter", "Use 0 to 60 days.");

        var s = await db.AutomationSettings.FirstOrDefaultAsync(ct);
        if (s is null) db.AutomationSettings.Add(s = new AutomationSettings());
        (s.AttendanceNudge, s.AttendanceNudgeAfterMinutes, s.AbsenceSms, s.AbsenceFlagAt) = (r.AttendanceNudge, r.AttendanceNudgeAfterMinutes, r.AbsenceSms, r.AbsenceFlagAt);
        (s.EveningTimetable, s.EveningTimetableAt, s.ClassDaySms, s.ScheduleRunningOut) = (r.EveningTimetable, r.EveningTimetableAt, r.ClassDaySms, r.ScheduleRunningOut);
        (s.LowHoursAlert, s.LowHoursAt, s.LowHoursSms) = (r.LowHoursAlert, decimal.Round(r.LowHoursAt, 2), r.LowHoursSms);
        (s.FeeReminders, s.FeeReminderDaysBefore, s.FeeReminderDaysAfter, s.FeeReminderSms) = (r.FeeReminders, r.FeeReminderDaysBefore, r.FeeReminderDaysAfter, r.FeeReminderSms);
        (s.PaymentLinks, s.MonthEndSalaries) = (r.PaymentLinks, r.MonthEndSalaries);
        (s.MorningSummary, s.MorningSummaryAt, s.MonthEndReport, s.ProgressReports, s.ProgressReportSms) = (r.MorningSummary, r.MorningSummaryAt, r.MonthEndReport, r.ProgressReports, r.ProgressReportSms);
        (s.AutoAiNotes, s.JobFailureAlerts) = (r.AutoAiNotes, r.JobFailureAlerts);
        var smsOn = new[] { (r.AbsenceSms, "absence"), (r.ClassDaySms, "class day"), (r.LowHoursSms, "low hours"), (r.FeeReminderSms, "fees"), (r.ProgressReportSms, "progress") }
            .Where(x => x.Item1).Select(x => x.Item2).ToList();
        audit.Record("automations.saved", nameof(AutomationSettings), s.Id, $"SMS on for: {(smsOn.Count == 0 ? "nothing" : string.Join(", ", smsOn))}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<OutboundMessageDto>> MessagesAsync(int take, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        take = Math.Clamp(take, 1, 200);
        return await db.OutboundMessages.OrderByDescending(m => m.CreatedAtUtc).Take(take)
            .Select(m => new OutboundMessageDto(m.Id, m.Kind, m.StudentId,
                db.Users.Where(u => u.Id == m.StudentId).Select(u => u.FullName).FirstOrDefault() ?? "Student",
                m.Recipient.ToString(), m.PhoneMasked, m.Text, m.Status.ToString(), m.Error, m.CreatedAtUtc, m.SentAtUtc))
            .ToListAsync(ct);
    }
}
