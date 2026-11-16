namespace StudentSync.Contracts;

/// <summary>Settings ▸ Automations. Times are the centre's local time. "Ready" flags say whether email / SMS / online
/// payments are connected (Settings ▸ Integrations); an automation that needs a missing connection simply skips that part.</summary>
public sealed record AutomationSettingsDto(
    bool AttendanceNudge, int AttendanceNudgeAfterMinutes, bool AbsenceSms, int AbsenceFlagAt,
    bool EveningTimetable, TimeOnly EveningTimetableAt, bool ClassDaySms, bool ScheduleRunningOut,
    bool LowHoursAlert, decimal LowHoursAt, bool LowHoursSms,
    bool FeeReminders, int FeeReminderDaysBefore, int FeeReminderDaysAfter, bool FeeReminderSms, bool PaymentLinks, bool MonthEndSalaries,
    bool MorningSummary, TimeOnly MorningSummaryAt, bool MonthEndReport, bool ProgressReports, bool ProgressReportSms,
    bool AutoAiNotes, bool JobFailureAlerts,
    bool EmailReady, bool SmsReady, bool PaymentsReady, int SmsSentThisMonth);

public sealed record SaveAutomationSettingsRequest(
    bool AttendanceNudge, int AttendanceNudgeAfterMinutes, bool AbsenceSms, int AbsenceFlagAt,
    bool EveningTimetable, TimeOnly EveningTimetableAt, bool ClassDaySms, bool ScheduleRunningOut,
    bool LowHoursAlert, decimal LowHoursAt, bool LowHoursSms,
    bool FeeReminders, int FeeReminderDaysBefore, int FeeReminderDaysAfter, bool FeeReminderSms, bool PaymentLinks, bool MonthEndSalaries,
    bool MorningSummary, TimeOnly MorningSummaryAt, bool MonthEndReport, bool ProgressReports, bool ProgressReportSms,
    bool AutoAiNotes, bool JobFailureAlerts);

/// <summary>One automated SMS in the log. The phone number is masked.</summary>
public sealed record OutboundMessageDto(Guid Id, string Kind, Guid StudentId, string StudentName, string Recipient, string PhoneMasked,
    string Text, string Status, string? Error, DateTime CreatedAtUtc, DateTime? SentAtUtc);

/// <summary>A student's month, prepared automatically; the teacher adds a remark and sends it.</summary>
public sealed record ProgressReportDto(Guid Id, Guid StudentId, string StudentName, string TeacherName, DateOnly Month, int ClassesAttended, int ClassesMissed,
    decimal HoursAttended, decimal? HoursLeft, int AssignmentsGraded, decimal? AverageMarkPercent, string? Remark, string Status, DateTime? SentAtUtc);
public sealed record SendProgressReportRequest(string? Remark);

/// <summary>Printable fee receipt (admins, and the student it belongs to).</summary>
public sealed record ReceiptDto(Guid Id, string Number, string InstituteName, string? InstituteEmail, string StudentName, string StudentCode,
    string PackageName, decimal PackageHours, decimal Amount, string CurrencyCode, DateOnly PaidOn, string Method, string? Reference, string? RecordedBy,
    decimal PackagePrice, decimal PaidOnPackage, decimal BalanceOnPackage, bool Voided, string? VoidReason);

/// <summary>Printable salary slip (admins, and the teacher it belongs to).</summary>
public sealed record SalarySlipDto(Guid Id, string Number, string InstituteName, string? InstituteEmail, string TeacherName, decimal Amount, string CurrencyCode,
    DateOnly PaidOn, DateOnly? PeriodFrom, DateOnly? PeriodTo, string Method, string? Reference, string? Note, decimal? HoursInPeriod, bool Voided, string? VoidReason);

/// <summary>Classes on a holiday, offered for cancelling or moving when the holiday is added.</summary>
public sealed record HolidayClassesDto(DateOnly Date, IReadOnlyList<HolidayClassDto> Classes);
public sealed record HolidayClassDto(Guid SessionId, string Title, DateTime StartUtc, DateTime EndUtc, string TeacherName, int Students);
public sealed record HolidayClassesRequest(DateOnly Date, string Action);
public sealed record HolidayClassesResult(int Done, IReadOnlyList<string> Skipped);
