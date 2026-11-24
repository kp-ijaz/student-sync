using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentSync.Application.Common;
using StudentSync.Application.Fees;
using StudentSync.Application.People;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Automation;

/// <summary>
/// The institute's scheduled automations (owner request 2026-11-17), run for one institute inside its tenant scope.
/// <see cref="RunDueAsync"/> is called every 15 minutes and does each daily/monthly task once, when the centre's local time
/// passes the configured time (an <see cref="AutomationRun"/> row per task and period; notifications and SMS also carry
/// dedupe keys). Every task only reminds, alerts or prepares — none changes money, attendance or classes.
/// </summary>
public sealed class DailyAutomations(IAppDbContext db, ITenantContext tenant, IClock clock, AutomationSettingsService settingsService,
    NotificationService notify, SmsOutbox sms, FeeService fees, SalaryService salaries, PaymentLinkService links, ProgressReportService progress,
    IServiceScopeFactory scopes, ILogger<DailyAutomations> logger)
{
    private sealed record Centre(string Name, string Tz, string Currency);

    /// <summary>
    /// Runs every task that is due now. Each task runs in its own scope (its own unit of work), so one failing task
    /// neither blocks the others nor leaves half-made rows behind; failures are reported together at the end (the job
    /// runner then logs them and tells the admins).
    /// </summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var s = await settingsService.CurrentAsync(ct);
        var c = await CentreAsync(ct);
        var local = ZonedTime.ToLocal(clock.UtcNow, c.Tz);
        var today = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        var day = Fmt.Key(today);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var tasks = new List<(string Task, string Key)>();
        if (time >= s.MorningSummaryAt)
        {
            tasks.Add(("lead-follow-ups", day)); // marketing: always on, sends nothing when no follow-up is due
            if (s.LowHoursAlert) tasks.Add(("low-hours", day));
            if (s.FeeReminders) tasks.Add(("fees", day));
            if (s.ScheduleRunningOut) tasks.Add(("schedule", day));
            if (s.ClassDaySms) tasks.Add(("class-day", day));
            if (s.MorningSummary || s.AttendanceNudge) tasks.Add(("summary", day));
            // Month tasks run on the 1st; days 2–3 catch up if the server was down on the 1st.
            if (today.Day <= 3)
            {
                if (s.MonthEndReport) tasks.Add(("month-report", Fmt.MonthKey(month)));
                if (s.MonthEndSalaries) tasks.Add(("salaries", Fmt.MonthKey(month)));
                if (s.ProgressReports) tasks.Add(("progress", Fmt.MonthKey(month)));
            }
        }
        if (s.EveningTimetable && time >= s.EveningTimetableAt) tasks.Add(("timetable", day));

        var done = await db.AutomationRuns.Where(r => tasks.Select(t => t.Task + "|" + t.Key).Contains(r.Task + "|" + r.RunKey)).Select(r => r.Task).ToListAsync(ct);
        var n = 0;
        var failed = new List<(string Task, Exception Error)>();
        foreach (var (task, key) in tasks.Where(t => !done.Contains(t.Task)))
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenant.TenantId!.Value);
                n += await scope.ServiceProvider.GetRequiredService<DailyAutomations>().RunTaskAsync(task, key, s, c, today, month, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Automation {Task} failed; the other tasks carry on", task);
                failed.Add((task, ex));
            }
        }
        if (failed.Count > 0)
            throw new AggregateException($"Automations failed: {string.Join(", ", failed.Select(f => f.Task))}", failed.Select(f => f.Error));
        return n;
    }

    private Task<int> RunTaskAsync(string task, string key, AutomationSettings s, Centre c, DateOnly today, DateOnly month, CancellationToken ct) =>
        OnceAsync(task, key, () => task switch
        {
            "lead-follow-ups" => LeadFollowUpsAsync(today, ct),
            "low-hours" => LowHoursAsync(s, c, today, ct),
            "fees" => FeeRemindersAsync(s, c, today, ct),
            "schedule" => ScheduleRunningOutAsync(c, today, ct),
            "class-day" => ClassDaySmsAsync(c, today, ct),
            "summary" => MorningSummaryAsync(s, c, today, ct),
            "month-report" => MonthReportAsync(c, month, today, ct),
            "salaries" => SalariesAsync(c, month, ct),
            "progress" => progress.PrepareAsync(month, ct),
            "timetable" => TimetableAsync(c, today.AddDays(1), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(task), task, null),
        }, ct);

    /// <summary>
    /// Each morning: tells every marketing team member (or admin) how many of their leads are due a follow-up today and how
    /// many are overdue, and tells the admins about due leads nobody is assigned to. One notification per person per day;
    /// it carries counts only, never a family's details.
    /// </summary>
    public async Task<int> LeadFollowUpsAsync(DateOnly today, CancellationToken ct)
    {
        var due = await db.Leads.Where(l => l.NextFollowUpOn <= today && l.Status != Domain.Marketing.LeadStatus.Admitted && l.Status != Domain.Marketing.LeadStatus.NotInterested)
            .Select(l => new { l.AssignedToUserId, Overdue = l.NextFollowUpOn < today }).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var active = await db.Users.Where(u => u.TenantId == tenant.TenantId && u.IsActive && (u.Role == UserRole.Marketing || u.Role == UserRole.InstitutionAdmin)).Select(u => u.Id).ToListAsync(ct);
        static string Text(int n, int overdue) => $"{n} lead{(n == 1 ? "" : "s")} to follow up today" + (overdue > 0 ? $" ({overdue} overdue)" : "");
        foreach (var g in due.Where(l => l.AssignedToUserId is { } id && active.Contains(id)).GroupBy(l => l.AssignedToUserId!.Value))
            await notify.NotifyAsync([g.Key], NotificationType.LeadFollowUp, Text(g.Count(), g.Count(x => x.Overdue)), "Open Follow-ups, call them and set the next day.",
                "/marketing/follow-ups", $"lead-fu:{Fmt.Key(today)}", ct);
        var unowned = due.Where(l => l.AssignedToUserId is not { } id || !active.Contains(id)).ToList();
        if (unowned.Count > 0)
            await notify.NotifyAsync(await AdminsAsync(ct), NotificationType.LeadFollowUp, $"{unowned.Count} lead{(unowned.Count == 1 ? "" : "s")} due a follow-up with nobody assigned",
                "Give them to someone on the marketing team.", "/marketing/follow-ups", $"lead-fu-unowned:{Fmt.Key(today)}", ct);
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    /// <summary>Every 10 minutes: classes that ended a while ago and still have no attendance → remind the teacher once.</summary>
    public async Task<int> AttendanceNudgeAsync(CancellationToken ct)
    {
        var s = await settingsService.CurrentAsync(ct);
        if (!s.AttendanceNudge) return 0;
        var now = clock.UtcNow;
        var endedBefore = now.AddMinutes(-s.AttendanceNudgeAfterMinutes);
        var unmarked = await Unmarked(now.AddDays(-2), endedBefore).Select(x => new { x.Id, x.Title, x.TeacherId, x.StartUtc, x.TimeZoneId }).Take(200).ToListAsync(ct);
        foreach (var x in unmarked)
            await notify.NotifyAsync([x.TeacherId], NotificationType.AttendanceMissing, $"Mark attendance: {x.Title}",
                $"{DescribeLocal(x.StartUtc, x.TimeZoneId)}. The student's hours and your pay count once attendance is marked.",
                "/teacher/tasks#attendance", $"att-nudge:{x.Id}", ct);
        await db.SaveChangesAsync(ct);
        return unmarked.Count;
    }

    // ---------------- money ----------------

    private async Task<int> LowHoursAsync(AutomationSettings s, Centre c, DateOnly today, CancellationToken ct)
    {
        var (balances, _) = await fees.BalancesAsync(today, ct);
        var low = balances.Where(b => b.Left <= s.LowHoursAt).ToList();
        if (low.Count == 0) return 0;
        var names = await NamesAsync(low.Select(b => b.StudentId), ct);
        var admins = await AdminsAsync(ct);
        foreach (var b in low)
        {
            var name = names.GetValueOrDefault(b.StudentId, "A student");
            var left = b.Left <= 0 ? "No hours" : Fmt.Hours(b.Left);
            var key = $"{b.StudentId}:{b.LatestPackageId}";
            await notify.NotifyAsync(admins, NotificationType.LowHours, $"Running low: {name}",
                $"{left} left of {Fmt.Hours(b.Bought)} added. Add more hours from their Fees tab.", $"/students/{b.StudentId}#fees", $"low:{key}", ct);
            await notify.NotifyAsync([b.StudentId], NotificationType.LowHours, "Your learning hours are running low",
                $"{left} left. Please talk to {c.Name} to add more hours so your classes continue.", "/student", $"low:{key}", ct);
            if (s.LowHoursSms)
                await sms.QueueFamilyAsync(b.StudentId, "low-hours",
                    $"{Fmt.FirstName(name)} has {(b.Left <= 0 ? "no" : b.Left.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))} tuition hours left. Please renew to continue classes.",
                    $"sms-low:{key}", ct);
        }
        await db.SaveChangesAsync(ct);
        return low.Count;
    }

    private async Task<int> FeeRemindersAsync(AutomationSettings s, Centre c, DateOnly today, CancellationToken ct)
    {
        var (_, open) = await fees.BalancesAsync(today, ct);
        var due = open.Select(p => (Package: p, Stage: Stage(s, p.DueOn.DayNumber - today.DayNumber))).Where(x => x.Stage is not null).ToList();
        if (due.Count == 0) return 0;
        var names = await NamesAsync(due.Select(x => x.Package.StudentId), ct);
        var admins = await AdminsAsync(ct);
        foreach (var (p, stage) in due)
        {
            var name = names.GetValueOrDefault(p.StudentId, "A student");
            var link = s.PaymentLinks ? await links.EnsureLinkAsync(p, ct) : null;
            var amount = Fmt.Money(p.Balance, p.CurrencyCode);
            var (title, body) = stage switch
            {
                "before" => ($"Fee due on {Fmt.Day(p.DueOn)}", $"{amount} for “{p.Name}” is due on {Fmt.Day(p.DueOn)}."),
                "today" => ("Fee due today", $"{amount} for “{p.Name}” is due today."),
                _ => ("Fee overdue", $"{amount} for “{p.Name}” was due on {Fmt.Day(p.DueOn)} and is still unpaid."),
            };
            if (link is not null) body += $" Pay online: {link}";
            await notify.NotifyAsync([p.StudentId], NotificationType.FeeReminder, title, body, link ?? "/student", $"fee:{p.PackageId}:{stage}", ct);
            if (s.FeeReminderSms)
            {
                var text = stage switch
                {
                    "before" => $"Fee reminder: {Fmt.Money(p.Balance, p.CurrencyCode, sms: true)} for {Fmt.FirstName(name)} is due on {Fmt.Day(p.DueOn)}.",
                    "today" => $"Fee reminder: {Fmt.Money(p.Balance, p.CurrencyCode, sms: true)} for {Fmt.FirstName(name)} is due today.",
                    _ => $"Fee overdue: {Fmt.Money(p.Balance, p.CurrencyCode, sms: true)} for {Fmt.FirstName(name)} was due on {Fmt.Day(p.DueOn)}.",
                } + (link is null ? "" : $" Pay online: {link}");
                await sms.QueueFamilyAsync(p.StudentId, "fee-reminder", text, $"sms-fee:{p.PackageId}:{stage}", ct);
            }
            if (stage == "after")
                await notify.NotifyAsync(admins, NotificationType.FeeReminder, $"Overdue: {name}",
                    $"{amount} for “{p.Name}” is overdue since {Fmt.Day(p.DueOn)}. Remind them, or put their classes on hold.",
                    $"/students/{p.StudentId}#fees", $"fee-admin:{p.PackageId}", ct);
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    /// <summary>"before" (within N days), "today", "after" (N days late, up to 60 days, so old debts never flood on day one).</summary>
    public static string? Stage(AutomationSettings s, int daysToDue) => daysToDue switch
    {
        > 0 when daysToDue <= s.FeeReminderDaysBefore => "before",
        0 => "today",
        < 0 when s.FeeReminderDaysAfter > 0 && daysToDue <= -s.FeeReminderDaysAfter && daysToDue >= -60 => "after",
        _ => null,
    };

    private async Task<int> SalariesAsync(Centre c, DateOnly month, CancellationToken ct)
    {
        var rows = (await salaries.MonthAsync(month, ct)).Where(r => r.Hours > 0 || r.DueNow > 0).ToList();
        if (rows.Count == 0) return 0;
        var lines = rows.Select(r => $"{r.TeacherName}: {Fmt.Hours(r.Hours)}, {Fmt.Money(r.Earned, c.Currency)} earned, {Fmt.Money(r.DueNow, c.Currency)} to pay").ToList();
        await notify.NotifyAsync(await AdminsAsync(ct), NotificationType.SalariesDue, $"Salaries for {Fmt.Month(month)}",
            string.Join("\n", lines.Prepend($"Total to pay: {Fmt.Money(rows.Sum(r => r.DueNow), c.Currency)}")),
            $"/admin/salaries?month={Fmt.MonthKey(month)}", $"salaries:{Fmt.MonthKey(month)}", ct);
        foreach (var r in rows.Where(r => r.Hours > 0))
            await notify.NotifyAsync([r.TeacherId], NotificationType.SalariesDue, $"Your hours for {Fmt.Month(month)}",
                $"{Fmt.Hours(r.Hours)} taught, {Fmt.Money(r.Earned, c.Currency)} earned.", "/teacher/profile", $"salary-teacher:{Fmt.MonthKey(month)}", ct);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    // ---------------- classes ----------------

    private async Task<int> ScheduleRunningOutAsync(Centre c, DateOnly today, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var active = db.SessionParticipants.Where(p => p.Status == ParticipantStatus.Active && p.Student!.Status == PersonStatus.Active);
        var recent = await active.Where(p => p.Session!.Status != SessionStatus.Cancelled && p.Session.StartUtc >= now.AddDays(-30) && p.Session.StartUtc <= now)
            .Select(p => new { p.StudentId, p.SessionId, p.Session!.StartUtc }).ToListAsync(ct);
        var future = await active.Where(p => p.Session!.Status == SessionStatus.Scheduled && p.Session.StartUtc > now)
            .Select(p => new { p.StudentId, p.SessionId, p.Session!.StartUtc, p.Session.EndUtc }).ToListAsync(ct);
        var (balances, _) = await fees.BalancesAsync(today, ct);
        var byStudent = balances.ToDictionary(b => b.StudentId);
        var ids = recent.Select(r => r.StudentId).Concat(future.Select(f => f.StudentId)).Distinct().ToList();
        var names = await NamesAsync(ids, ct);
        var admins = await AdminsAsync(ct);
        var n = 0;
        foreach (var id in ids)
        {
            var mine = future.Where(f => f.StudentId == id).OrderBy(f => f.StartUtc).ToList();
            var name = names.GetValueOrDefault(id, "A student");
            // Only students who are actually studying (a class in the last 30 days) and whose timetable is about to end.
            if (mine.Count <= 2 && recent.Any(r => r.StudentId == id))
            {
                var last = mine.LastOrDefault()?.SessionId ?? recent.Where(r => r.StudentId == id).MaxBy(r => r.StartUtc)!.SessionId;
                await notify.NotifyAsync(admins, NotificationType.ScheduleEnding,
                    mine.Count == 0 ? $"{name} has no classes booked" : $"{name}: {mine.Count} class{(mine.Count == 1 ? "" : "es")} left in the timetable",
                    "Add their next classes so they keep going.", $"/students/{id}", $"sched-end:{id}:{last}", ct);
                n++;
            }
            var booked = mine.Sum(f => FeeService.HoursOf(f.StartUtc, f.EndUtc));
            if (byStudent.TryGetValue(id, out var b) && booked > b.Left && booked > 0)
            {
                await notify.NotifyAsync(admins, NotificationType.ScheduleEnding, $"{name}: booked classes need more hours",
                    $"{Fmt.Hours(booked)} booked, {Fmt.Hours(Math.Max(0, b.Left))} left. Add hours or shorten the timetable.",
                    $"/students/{id}#fees", $"sched-hours:{id}:{b.LatestPackageId}", ct);
                n++;
            }
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    private async Task<int> ClassDaySmsAsync(Centre c, DateOnly today, CancellationToken ct)
    {
        var (from, to) = DayUtc(today, c.Tz);
        var rows = await db.SessionParticipants
            .Where(p => p.Status == ParticipantStatus.Active && p.Session!.Status == SessionStatus.Scheduled && p.Session.StartUtc >= from && p.Session.StartUtc < to && p.Session.StartUtc > clock.UtcNow)
            .Select(p => new { p.StudentId, p.Session!.Title, p.Session.StartUtc, p.Session.EndUtc, Teacher = db.Users.Where(u => u.Id == p.Session.TeacherId).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct);
        foreach (var g in rows.GroupBy(r => r.StudentId))
        {
            var text = "Class today: " + string.Join("; ", g.OrderBy(x => x.StartUtc).Select(x =>
                $"{Fmt.Time(ZonedTime.ToLocal(x.StartUtc, c.Tz))}-{Fmt.Time(ZonedTime.ToLocal(x.EndUtc, c.Tz))} {x.Title}{(x.Teacher is null ? "" : $" with {Fmt.FirstName(x.Teacher)}")}")) + ".";
            await sms.QueueFamilyAsync(g.Key, "class-day", text, $"classday:{g.Key}:{Fmt.Key(today)}", ct, preferStudent: true);
        }
        await db.SaveChangesAsync(ct);
        return rows.Select(r => r.StudentId).Distinct().Count();
    }

    private async Task<int> TimetableAsync(Centre c, DateOnly tomorrow, CancellationToken ct)
    {
        var (from, to) = DayUtc(tomorrow, c.Tz);
        var rows = await db.Sessions.Where(x => x.Status == SessionStatus.Scheduled && x.StartUtc >= from && x.StartUtc < to)
            .Select(x => new { x.TeacherId, x.Title, x.StartUtc, x.EndUtc, Students = x.Participants.Count(p => p.Status == ParticipantStatus.Active) })
            .ToListAsync(ct);
        foreach (var g in rows.GroupBy(r => r.TeacherId))
        {
            var lines = g.OrderBy(x => x.StartUtc).Select(x =>
                $"{Fmt.Time(ZonedTime.ToLocal(x.StartUtc, c.Tz))}–{Fmt.Time(ZonedTime.ToLocal(x.EndUtc, c.Tz))} {x.Title}{(x.Students > 1 ? $" ({x.Students} students)" : "")}");
            await notify.NotifyAsync([g.Key], NotificationType.Timetable, $"Tomorrow: {g.Count()} class{(g.Count() == 1 ? "" : "es")}",
                string.Join("\n", lines), "/sessions", $"timetable:{g.Key}:{Fmt.Key(tomorrow)}", ct);
        }
        await db.SaveChangesAsync(ct);
        return rows.Select(r => r.TeacherId).Distinct().Count();
    }

    // ---------------- reports ----------------

    private async Task<int> MorningSummaryAsync(AutomationSettings s, Centre c, DateOnly today, CancellationToken ct)
    {
        var admins = await AdminsAsync(ct);
        var (todayFrom, todayTo) = DayUtc(today, c.Tz);
        var (yesterdayFrom, _) = DayUtc(today.AddDays(-1), c.Tz);
        var unmarked = s.AttendanceNudge ? await Unmarked(yesterdayFrom, todayFrom).CountAsync(ct) : 0;
        if (!s.MorningSummary)
        {
            if (unmarked > 0)
                await notify.NotifyAsync(admins, NotificationType.AttendanceMissing, $"{unmarked} class{(unmarked == 1 ? "" : "es")} from yesterday without attendance",
                    "Hours and teacher pay count only once attendance is marked.", "/sessions", $"att-escalate:{Fmt.Key(today)}", ct);
            await db.SaveChangesAsync(ct);
            return unmarked;
        }

        var classes = await db.Sessions.Where(x => x.Status != SessionStatus.Cancelled && x.StartUtc >= todayFrom && x.StartUtc < todayTo)
            .OrderBy(x => x.StartUtc).Select(x => new { x.Title, x.StartUtc, Teacher = db.Users.Where(u => u.Id == x.TeacherId).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct);
        var (balances, _) = await fees.BalancesAsync(today, ct);
        var low = balances.Count(b => b.Left <= s.LowHoursAt);
        var toGrade = await db.Submissions.CountAsync(x => x.Status == SubmissionStatus.Submitted, ct);
        var doubts = await db.Doubts.CountAsync(d => Doubt.Unresolved.Contains(d.Status), ct);

        var lines = new List<string> { classes.Count == 0 ? "No classes today." : $"{classes.Count} class{(classes.Count == 1 ? "" : "es")} today:" };
        lines.AddRange(classes.Take(6).Select(x => $"• {Fmt.Time(ZonedTime.ToLocal(x.StartUtc, c.Tz))} {x.Title}{(x.Teacher is null ? "" : $" — {Fmt.FirstName(x.Teacher)}")}"));
        if (classes.Count > 6) lines.Add($"• and {classes.Count - 6} more");
        if (unmarked > 0) lines.Add($"Attendance missing for {unmarked} class{(unmarked == 1 ? "" : "es")} from yesterday.");
        var owing = balances.Where(b => b.Due > 0).ToList();
        if (owing.Count > 0) lines.Add($"Fees due: {Fmt.Money(owing.Sum(b => b.Due), c.Currency)} from {owing.Count} student{(owing.Count == 1 ? "" : "s")} ({owing.Count(b => b.OverdueDays > 0)} overdue).");
        if (low > 0) lines.Add($"{low} student{(low == 1 ? " is" : "s are")} running low on hours.");
        if (toGrade > 0) lines.Add($"{toGrade} submission{(toGrade == 1 ? "" : "s")} waiting to be marked.");
        if (doubts > 0) lines.Add($"{doubts} open doubt{(doubts == 1 ? "" : "s")}.");

        await notify.NotifyAsync(admins, NotificationType.DailySummary, $"Today at {c.Name}: {classes.Count} class{(classes.Count == 1 ? "" : "es")}",
            string.Join("\n", lines), "/admin", $"summary:{Fmt.Key(today)}", ct);
        await db.SaveChangesAsync(ct);
        return 1;
    }

    private async Task<int> MonthReportAsync(Centre c, DateOnly month, DateOnly today, CancellationToken ct)
    {
        var next = month.AddMonths(1);
        var (from, _) = DayUtc(month, c.Tz);
        var (to, _) = DayUtc(next, c.Tz);
        var collected = await db.FeePayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= month && p.PaidOn < next).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var salariesPaid = await db.TeacherPayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= month && p.PaidOn < next).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var sessions = await db.Sessions.Where(x => x.StartUtc >= from && x.StartUtc < to).Select(x => new { x.Status, x.StartUtc, x.EndUtc }).ToListAsync(ct);
        var done = sessions.Where(x => x.Status == SessionStatus.Completed).ToList();
        var newStudents = await db.Students.CountAsync(x => x.JoinedOn >= month && x.JoinedOn < next, ct);
        var (balances, _) = await fees.BalancesAsync(today, ct);
        var owed = (await salaries.MonthAsync(month, ct)).Sum(r => r.DueNow);

        var lines = new List<string>
        {
            $"Fees collected: {Fmt.Money(collected, c.Currency)}",
            $"Salaries paid: {Fmt.Money(salariesPaid, c.Currency)}",
            $"Hours taught: {Fmt.Hours(done.Sum(x => FeeService.HoursOf(x.StartUtc, x.EndUtc)))} in {done.Count} class{(done.Count == 1 ? "" : "es")} ({sessions.Count(x => x.Status == SessionStatus.Cancelled)} cancelled)",
            $"New students: {newStudents}",
            $"Fees still to collect today: {Fmt.Money(balances.Sum(b => b.Due), c.Currency)}",
            $"Salaries still to pay today: {Fmt.Money(owed, c.Currency)}",
        };
        await notify.NotifyAsync(await AdminsAsync(ct), NotificationType.MonthReport, $"{Fmt.Month(month)} at a glance", string.Join("\n", lines),
            "/admin/reports", $"month-report:{Fmt.MonthKey(month)}", ct);
        await db.SaveChangesAsync(ct);
        return 1;
    }

    // ---------------- helpers ----------------

    /// <summary>Finished, not cancelled, with students, and no attendance at all.</summary>
    private IQueryable<LearningSession> Unmarked(DateTime endedAfter, DateTime endedBefore) =>
        db.Sessions.Where(x => (x.Status == SessionStatus.Scheduled || x.Status == SessionStatus.Live)
                               && x.EndUtc >= endedAfter && x.EndUtc <= endedBefore
                               && x.Participants.Any(p => p.Status == ParticipantStatus.Active)
                               && !db.Attendance.Any(a => a.SessionId == x.Id));

    /// <summary>Does <paramref name="work"/> once per task and period. The run row is saved with the work's own changes, so
    /// a failed run leaves nothing behind and is retried; a second runner hits the unique index and saves nothing.</summary>
    private async Task<int> OnceAsync(string task, string key, Func<Task<int>> work, CancellationToken ct)
    {
        if (await db.AutomationRuns.AnyAsync(r => r.Task == task && r.RunKey == key, ct)) return 0;
        var run = new AutomationRun { TenantId = tenant.TenantId!.Value, Task = task, RunKey = key, CreatedAtUtc = clock.UtcNow };
        db.AutomationRuns.Add(run);
        var items = await work();
        (run.Items, run.FinishedAtUtc) = (items, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        if (items > 0) logger.LogInformation("Automation {Task} {Key}: {Items} item(s)", task, key, items);
        return items;
    }

    private async Task<Centre> CentreAsync(CancellationToken ct)
    {
        var t = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => new { x.Name, x.DefaultTimeZoneId, x.CurrencyCode }).FirstAsync(ct);
        return new Centre(t.Name, t.DefaultTimeZoneId, t.CurrencyCode);
    }

    private Task<List<Guid>> AdminsAsync(CancellationToken ct) =>
        db.Users.Where(u => u.TenantId == tenant.TenantId && u.Role == UserRole.InstitutionAdmin && u.IsActive).Select(u => u.Id).ToListAsync(ct);

    private Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return db.Users.Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static (DateTime From, DateTime To) DayUtc(DateOnly day, string tz) =>
        (ZonedTime.ToUtc(day, TimeOnly.MinValue, tz).Utc, ZonedTime.ToUtc(day.AddDays(1), TimeOnly.MinValue, tz).Utc);

    private static string DescribeLocal(DateTime utc, string tz) => Scheduling.SchedulingService.DescribeLocal(utc, tz);
}
