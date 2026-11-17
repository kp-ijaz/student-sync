using StudentSync.Domain.Common;

namespace StudentSync.Domain.Platform;

/// <summary>
/// The institute's automations (Settings ▸ Automations, owner request 2026-11-17). One row per institute, created with
/// defaults on first read. In-app and email automations are on by default; anything that sends SMS is off until the admin
/// switches it on, because every SMS costs money. Nothing here ever changes money or attendance by itself: automations
/// remind, alert and prepare; the admin or teacher still decides.
/// </summary>
public class AutomationSettings : TenantEntity
{
    // Classes
    /// <summary>Remind the teacher when a finished class has no attendance; tell the admins the next morning.</summary>
    public bool AttendanceNudge { get; set; } = true;
    public int AttendanceNudgeAfterMinutes { get; set; } = 30;
    /// <summary>SMS the guardian when a student is marked absent.</summary>
    public bool AbsenceSms { get; set; }
    /// <summary>Flag a student to the admins at this many absences in a calendar month (0 = off).</summary>
    public int AbsenceFlagAt { get; set; } = 3;
    /// <summary>Tomorrow's classes to every teacher (in-app/email) and an SMS reminder to students on the day (if SMS is on).</summary>
    public bool EveningTimetable { get; set; } = true;
    public TimeOnly EveningTimetableAt { get; set; } = new(20, 0);
    public bool ClassDaySms { get; set; }
    /// <summary>Tell the admins when a student's weekly schedule ends soon, or booked classes need more hours than are left.</summary>
    public bool ScheduleRunningOut { get; set; } = true;

    // Money
    public bool LowHoursAlert { get; set; } = true;
    public decimal LowHoursAt { get; set; } = 2m;
    public bool LowHoursSms { get; set; }
    public bool FeeReminders { get; set; } = true;
    public int FeeReminderDaysBefore { get; set; } = 3;
    public int FeeReminderDaysAfter { get; set; } = 3;
    public bool FeeReminderSms { get; set; }
    /// <summary>
    /// Attach an online payment link (Razorpay/Stripe) to fee reminders. Off, and not offered in the app this phase (owner
    /// decision 2026-11-17): online payment will come with the parents module; the gateway connection is kept for it.
    /// </summary>
    public bool PaymentLinks { get; set; }
    public bool MonthEndSalaries { get; set; } = true;

    // Reports
    public bool MorningSummary { get; set; } = true;
    public TimeOnly MorningSummaryAt { get; set; } = new(7, 30);
    public bool MonthEndReport { get; set; } = true;
    /// <summary>On the 1st, prepare a progress report per student for the teacher to approve; approved reports go to the guardian.</summary>
    public bool ProgressReports { get; set; } = true;
    public bool ProgressReportSms { get; set; }

    // Teaching and site health
    /// <summary>Start the AI class-notes draft as soon as a recording's transcript is ready (still a draft for the teacher).</summary>
    public bool AutoAiNotes { get; set; }
    public bool JobFailureAlerts { get; set; } = true;
}

public enum OutboundChannel { Sms }
public enum OutboundRecipient { Student, Guardian }
public enum OutboundStatus { Pending, Sent, Failed, Skipped }

/// <summary>
/// One automated SMS: queued in the same transaction as the event, sent by the outbox job, retried up to 3 times.
/// (TenantId, DedupeKey) is unique, so the same reminder is never sent twice. The phone is stored masked; the full number
/// is read from the student's record at send time.
/// </summary>
public class OutboundMessage : TenantEntity
{
    public OutboundChannel Channel { get; set; }
    /// <summary>What sent it, e.g. "absence", "low-hours", "fee-reminder".</summary>
    public string Kind { get; set; } = "";
    public Guid StudentId { get; set; }
    public OutboundRecipient Recipient { get; set; }
    public string PhoneMasked { get; set; } = "";
    public string Text { get; set; } = "";
    public string DedupeKey { get; set; } = "";
    public OutboundStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTime? SentAtUtc { get; set; }
    /// <summary>Do not send before this time (quiet hours: nothing between 21:00 and 08:00 in the centre's time).</summary>
    public DateTime NotBeforeUtc { get; set; }
}

/// <summary>
/// "This daily/monthly task already ran for this period" (e.g. Task "fees", RunKey "2026-11-17"). Unique per tenant, task
/// and key, so two job runners can never both do it. A failed run removes its row and is retried by the next tick.
/// </summary>
public class AutomationRun : TenantEntity
{
    public string Task { get; set; } = "";
    public string RunKey { get; set; } = "";
    public int Items { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}
