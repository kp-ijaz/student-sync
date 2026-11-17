using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner decision 2026-11-17: classes must fit the student's prepaid hours. Free = hours added − used − already booked; a
/// student without hours cannot be booked, a plan that needs more than is free is refused (the check says how many fit),
/// and the same rule applies to weekly plans, adding a student to a class and making a class longer.
/// </summary>
public sealed class HoursRuleTests(ApiFixture api)
{
    private static CreateSessionRequest Weekly(Guid teacher, Guid subject, Guid student, DateOnly start, int weeks, int minutes = 60) =>
        new("Maths · Hours rule (demo)", SessionType.OneToOne, teacher, null, null, null, subject, null, [student], "Asia/Dubai", start, new TimeOnly(7, 0), minutes,
            DeliveryMode.InPerson, null, null, null, new RecurrenceRequest(RecurrenceFrequency.Weekly, 1, WeekDays.Sunday, start.AddDays(weeks * 7 - 1), null));

    private async Task<(Client Admin, Guid Student, Guid Teacher, Guid Subject)> SetupAsync()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Hours Rule {tag} (Demo Student)",
            $"hours-{tag}@horizon.example.test", $"HR-{tag}", null, null, "Asia/Dubai"))).Id;
        var amira = (await admin.Get<PagedResult<TeacherDto>>("teachers?pageSize=100")).Items.Single(t => t.Email == Accounts.Amira);
        return (admin, student, amira.Id, amira.Subjects.First().Id);
    }

    /// <summary>A Sunday far enough ahead (and unique per test) that the demo timetable never clashes.</summary>
    private static DateOnly FarSunday(int weeksAhead)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7 * weeksAhead);
        while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task A_student_without_hours_cannot_be_booked()
    {
        var (admin, student, teacher, subject) = await SetupAsync();
        var plan = new CreateSessionBatchRequest([Weekly(teacher, subject, student, FarSunday(60), 2)]);
        var check = await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check", plan);
        var hours = Assert.Single(check.Hours!);
        Assert.Equal((false, false, 0), (hours.HasHours, hours.Fits, hours.FitCount));

        var refused = await admin.PostRaw("sessions/batch", plan);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("hours_exceeded", body);
        Assert.Contains("no hours yet", body);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw("sessions", plan.Items[0])).StatusCode);
    }

    [Fact]
    public async Task Classes_must_fit_the_free_hours_and_the_check_says_how_many_fit()
    {
        var (admin, student, teacher, subject) = await SetupAsync();
        await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(3, 1500));
        var start = FarSunday(70);

        // 4 weekly 1-hour classes need 4 h; 3 h are free: refused, and 3 would fit.
        var four = new CreateSessionBatchRequest([Weekly(teacher, subject, student, start, 4)]);
        var check = Assert.Single((await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check", four)).Hours!);
        Assert.Equal((true, false, 3m, 4m, 3, 4), (check.HasHours, check.Fits, check.Free, check.Needed, check.FitCount, check.Classes));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw("sessions/batch", four)).StatusCode);

        // Booking 3 works; then nothing is free, so one more class is refused, also when added another way.
        var created = await admin.Post<CreateSessionResponse>("sessions/batch", new CreateSessionBatchRequest([Weekly(teacher, subject, student, start, 3)]));
        Assert.Equal(3, created.SessionIds.Count);
        var after = Assert.Single((await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check",
            new CreateSessionBatchRequest([Weekly(teacher, subject, student, start.AddDays(35), 1)]))).Hours!);
        Assert.Equal((3m, 0m, 0), (after.Booked, after.Free, after.FitCount));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw("sessions", Weekly(teacher, subject, student, start.AddDays(35), 1) with { Recurrence = null })).StatusCode);

        // Making a booked class longer needs free hours too; the same length (moving it) is fine.
        var session = await admin.Get<SessionDetailDto>($"sessions/{created.SessionIds[0]}");
        var longer = new RescheduleSessionRequest("Asia/Dubai", start, new TimeOnly(8, 0), 90, "Longer (test)", session.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Http.PostAsJsonAsync($"/api/v1/sessions/{session.Id}/reschedule", longer, ApiFixture.Json)).StatusCode);
        var moved = longer with { DurationMinutes = 60, Reason = "Moved (test)" };
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.PostAsJsonAsync($"/api/v1/sessions/{session.Id}/reschedule", moved, ApiFixture.Json)).StatusCode);

        // Adding hours frees them again.
        await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(2, 1000));
        Assert.Equal(2m, Assert.Single((await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check",
            new CreateSessionBatchRequest([Weekly(teacher, subject, student, start.AddDays(35), 1)]))).Hours!).Free);
    }
}
