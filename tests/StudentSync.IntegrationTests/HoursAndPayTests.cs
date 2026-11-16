using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Scheduling;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner request 2026-11-16: attendance and hours (done / missed) for students and teachers, a fee rate per student, a pay
/// rate per teacher with salary payments, money in reports, and the default catalogue (NCERT, Kerala State, ICSE, Classes 1–12).
/// </summary>
public sealed class HoursAndPayTests(ApiFixture api)
{
    private async Task<Guid> IdOf(string email)
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.IgnoreQueryFilters().Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    [Fact]
    public async Task Teacher_pay_uses_the_rate_of_each_day_and_payments_are_voided_not_edited()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var amira = await IdOf(Accounts.Amira);
        await admin.Put($"teachers/{amira}/pay/rate", new SetTeacherRateRequest(300, new DateOnly(2000, 1, 1)));
        var pay = await admin.Get<TeacherPayDto>($"teachers/{amira}/pay");
        Assert.Equal(300, pay.CurrentRate);
        Assert.True(pay.HoursAllTime > 0, "the demo teacher has completed classes");
        Assert.Equal(decimal.Round(pay.HoursAllTime * 300, 2), pay.EarnedAllTime);
        Assert.Equal(0, pay.UnratedClasses);

        // A later rate applies only from its day: nothing in the past changes.
        await admin.Put($"teachers/{amira}/pay/rate", new SetTeacherRateRequest(500, new DateOnly(2100, 1, 1)));
        Assert.Equal(pay.EarnedAllTime, (await admin.Get<TeacherPayDto>($"teachers/{amira}/pay")).EarnedAllTime);

        var paymentId = (await admin.Post<IdResponse>($"teachers/{amira}/pay/payments", new AddTeacherPaymentRequest(1000, DateOnly.FromDateTime(DateTime.UtcNow), PaymentMethod.BankTransfer, "SAL-1", "October salary (test)", null, null))).Id;
        var afterPay = await admin.Get<TeacherPayDto>($"teachers/{amira}/pay");
        Assert.Equal((pay.Paid + 1000, afterPay.EarnedAllTime - afterPay.Paid), (afterPay.Paid, afterPay.Due));
        await admin.Post($"teachers/{amira}/pay/payments/{paymentId}/void", new VoidTeacherPaymentRequest("Entered twice (test)"));
        var afterVoid = await admin.Get<TeacherPayDto>($"teachers/{amira}/pay");
        Assert.Equal(pay.Paid, afterVoid.Paid);
        Assert.Contains(afterVoid.Payments, p => p.Id == paymentId && p.Voided && p.VoidReason == "Entered twice (test)");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw($"teachers/{amira}/pay/payments/{paymentId}/void", new VoidTeacherPaymentRequest("again"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"teachers/{amira}/pay/payments", new AddTeacherPaymentRequest(0, DateOnly.FromDateTime(DateTime.UtcNow), PaymentMethod.Cash, null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Teachers_see_only_their_own_pay_and_profile_and_cannot_change_pay()
    {
        var amiraId = await IdOf(Accounts.Amira);
        var danielId = await IdOf(Accounts.Daniel);
        var amira = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.OK, (await amira.GetRaw($"teachers/{amiraId}/pay")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await amira.GetRaw($"teachers/{amiraId}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw($"teachers/{danielId}/pay")).StatusCode);
        // Another teacher's overview lists students Amira does not teach: refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw($"teachers/{danielId}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.PutRaw($"teachers/{amiraId}/pay/rate", new SetTeacherRateRequest(9999, null))).StatusCode);
        var student = await api.LoginAsync(Accounts.Aisha);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetRaw($"teachers/{amiraId}/pay")).StatusCode);
        // Another institute's admin cannot see this teacher at all.
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"teachers/{amiraId}/pay")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"teachers/{amiraId}/overview")).StatusCode);

        var overview = await (await api.LoginAsync(Accounts.Admin)).Get<TeacherOverviewDto>($"teachers/{amiraId}/overview");
        Assert.NotEmpty(overview.Students);
        Assert.Equal(["This month", "All time"], overview.Periods.Select(p => p.Label));
    }

    [Fact]
    public async Task Student_hours_count_attended_and_missed_classes_and_cancellation_reasons()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var fatima = await IdOf(Accounts.Fatima);
        var before = (await admin.Get<StudentHoursDto>($"students/{fatima}/hours")).Periods.Single(p => p.Label == "All time").Stats;

        // The expected "done" count straight from the database: present or late marks on classes that were not cancelled.
        using (var scope = api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var done = await db.Attendance.IgnoreQueryFilters().CountAsync(a => a.StudentId == fatima && (a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late)
                && db.Sessions.IgnoreQueryFilters().Any(s => s.Id == a.SessionId && s.Status != SessionStatus.Cancelled));
            Assert.Equal(done, before.ClassesDone);
        }

        // Cancel her last two upcoming classes: one because she was away, one because the teacher was.
        Guid studentAway, teacherAway, teacherId;
        decimal studentAwayHours;
        using (var scope = api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var future = await db.Sessions.IgnoreQueryFilters()
                .Where(s => s.Status == SessionStatus.Scheduled && s.StartUtc > DateTime.UtcNow.AddDays(2) && s.Participants.Any(p => p.StudentId == fatima && p.Status == ParticipantStatus.Active))
                .OrderByDescending(s => s.StartUtc).Take(2).Select(s => new { s.Id, s.TeacherId, s.StartUtc, s.EndUtc }).ToListAsync();
            Assert.Equal(2, future.Count);
            (studentAway, teacherAway, teacherId) = (future[0].Id, future[1].Id, future[1].TeacherId);
            studentAwayHours = Domain.Fees.TeacherPayRules.Hours(future[0].StartUtc, future[0].EndUtc);
        }
        var teacherBefore = (await admin.Get<TeacherOverviewDto>($"teachers/{teacherId}/overview")).Periods.Single(p => p.Label == "All time").Stats;
        await admin.Post($"sessions/{studentAway}/cancel", new CancelSessionRequest("Student travelling (test)", false, CancellationKind.StudentAbsent));
        await admin.Post($"sessions/{teacherAway}/cancel", new CancelSessionRequest("Teacher unwell (test)", false, CancellationKind.TeacherAbsent));

        var after = (await admin.Get<StudentHoursDto>($"students/{fatima}/hours")).Periods.Single(p => p.Label == "All time").Stats;
        Assert.Equal(before.ClassesMissed + 1, after.ClassesMissed);
        Assert.Equal(before.HoursMissed + studentAwayHours, after.HoursMissed);
        Assert.Equal(before.Upcoming - 2, after.Upcoming);
        var teacherAfter = (await admin.Get<TeacherOverviewDto>($"teachers/{teacherId}/overview")).Periods.Single(p => p.Label == "All time").Stats;
        Assert.Equal(teacherBefore.ClassesMissed + 1, teacherAfter.ClassesMissed);

        // The student herself may read her hours; another student may not.
        Assert.Equal(HttpStatusCode.OK, (await (await api.LoginAsync(Accounts.Fatima)).GetRaw($"students/{fatima}/hours")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Aisha)).GetRaw($"students/{fatima}/hours")).StatusCode);
    }

    [Fact]
    public async Task Student_fee_rate_is_admin_only_and_reports_show_money_to_admins()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var hiro = await IdOf(Accounts.Hiro);
        await admin.Put($"students/{hiro}/rate", new SetStudentRateRequest(450));
        Assert.Equal(450, (await admin.Get<StudentFeesDto>($"students/{hiro}/fees")).HourlyRate);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{hiro}/rate", new SetStudentRateRequest(-5))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).PutRaw($"students/{hiro}/rate", new SetStudentRateRequest(1))).StatusCode);

        var report = await admin.Get<InstitutionReportDto>("reports/institution");
        Assert.NotNull(report.Money);
        Assert.True(report.Money.HoursTaught >= 0 && report.Money.ClassesHeld >= 0);
        Assert.Equal(report.Teachers.Sum(t => t.HoursTaught), report.Money.HoursTaught);
        Assert.NotNull(report.Subjects);
    }

    [Fact]
    public async Task Default_catalogue_adds_three_syllabi_classes_1_to_12_and_their_subjects_once()
    {
        var admin = await api.LoginAsync(Accounts.OtherAdmin);
        var first = await admin.Post<DefaultCatalogueResult>("academics/catalogue/defaults", null);
        var again = await admin.Post<DefaultCatalogueResult>("academics/catalogue/defaults", null);
        Assert.Equal(new DefaultCatalogueResult(0, 0, 0, 0, 0), again);
        Assert.True(first.Courses + first.Subjects > 0 || again == first);

        var curricula = await admin.Get<List<CurriculumDto>>("academics/curricula");
        foreach (var name in new[] { "NCERT", "Kerala", "ICSE" }) Assert.Contains(curricula, c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        var courses = await admin.Get<List<CourseDto>>("academics/courses");
        var c10 = Assert.Single(courses, c => c.Code == "NCERT-C10");
        Assert.Equal("Class 10", c10.GradeLevelName);
        Assert.NotNull(c10.CurriculumId);
        Assert.Contains(courses, c => c.Code == "KERALA-C12");
        Assert.Contains(courses, c => c.Code == "ICSE-C1");

        // Teachers and students cannot load it; teachers of another institute never see these courses.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.OtherTeacher)).PostRaw("academics/catalogue/defaults", null)).StatusCode);
        var horizon = await (await api.LoginAsync(Accounts.Admin)).Get<List<CourseDto>>("academics/courses");
        Assert.DoesNotContain(horizon, c => c.Id == c10.Id);
    }
}
