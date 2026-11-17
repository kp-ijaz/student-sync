using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Scheduling;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner request 2026-11-17: a student sees their teachers (subjects, next class) and can ask to postpone or reschedule one
/// of their own upcoming classes; the teacher of that class (or an admin) accepts — the class moves by the normal rules —
/// or keeps it. Nobody else can ask for, see or answer the request. Passwords now need only 6 characters (common ones refused).
/// </summary>
public sealed class ClassChangeTests(ApiFixture api)
{
    private async Task<(Client Admin, Client Amira, Client Aisha, Guid SessionId, DateOnly Day)> ClassAsync()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var amira = await api.LoginAsync(Accounts.Amira);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        // A fresh one-to-one class far ahead, at a time nothing else uses (demo students have demo hours).
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(100 + Random.Shared.Next(60)));
        var time = new TimeOnly(2, 5 * Random.Shared.Next(0, 12));
        var id = (await admin.Post<CreateSessionResponse>("sessions",
            TenantIsolationTests.NewSession(SessionType.OneToOne, amira.UserId, [aisha.UserId], day, time: time))).SessionIds.Single();
        return (admin, amira, aisha, id, day);
    }

    [Fact]
    public async Task Student_sees_their_teachers_with_subjects_and_next_classes()
    {
        var (_, amira, aisha, _, _) = await ClassAsync();
        var teachers = await aisha.Get<List<MyTeacherDto>>("my/teachers");
        var t = Assert.Single(teachers, x => x.TeacherId == amira.UserId);
        Assert.Contains("Mathematics", t.Subjects);
        // The next classes with this teacher, soonest first (at most 10; the class booked above is months away).
        Assert.InRange(t.Upcoming.Count, 1, 10);
        Assert.All(t.Upcoming, c => Assert.True(c.EndUtc > DateTime.UtcNow));
        Assert.Equal(t.Upcoming.OrderBy(c => c.StartUtc).Select(c => c.SessionId), t.Upcoming.Select(c => c.SessionId));
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw("my/teachers")).StatusCode);
    }

    [Fact]
    public async Task A_reschedule_request_goes_to_the_teacher_and_accepting_moves_the_class()
    {
        var (admin, amira, aisha, sessionId, day) = await ClassAsync();
        var proposed = (Date: day.AddDays(1), Time: new TimeOnly(4, 35)); // test classes run 02:00–03:55, so this never clashes
        var requestId = (await aisha.Post<IdResponse>("class-requests", new CreateClassChangeRequest(sessionId, "Reschedule", proposed.Date, proposed.Time, "School exam (test)"))).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await aisha.PostRaw("class-requests", new CreateClassChangeRequest(sessionId, "Postpone", null, null, "Again"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await aisha.PostRaw("class-requests", new CreateClassChangeRequest(sessionId, "Reschedule", null, null, "No time"))).StatusCode);

        // Another student cannot ask about this class; another teacher neither sees nor answers the request.
        var fatima = await api.LoginAsync(Accounts.Fatima);
        Assert.Equal(HttpStatusCode.NotFound, (await fatima.PostRaw("class-requests", new CreateClassChangeRequest(sessionId, "Postpone", null, null, "Not mine"))).StatusCode);
        var daniel = await api.LoginAsync(Accounts.Daniel);
        Assert.DoesNotContain(await daniel.Get<List<ClassChangeRequestDto>>("class-requests"), r => r.Id == requestId);
        Assert.Equal(HttpStatusCode.NotFound, (await daniel.PostRaw($"class-requests/{requestId}/answer", new AnswerClassChangeRequest(true, null, null, null))).StatusCode);
        Assert.Contains(await admin.Get<List<ClassChangeRequestDto>>("class-requests?status=Pending"), r => r.Id == requestId);

        // The student sees the waiting request.
        var mine = (await aisha.Get<List<ClassChangeRequestDto>>("class-requests")).Single(r => r.Id == requestId);
        Assert.Equal(("Pending", "Reschedule", sessionId), (mine.Status, mine.Kind, mine.SessionId));

        var pending = Assert.Single(await amira.Get<List<ClassChangeRequestDto>>("class-requests?status=Pending"), r => r.Id == requestId);
        Assert.Equal((proposed.Date, proposed.Time, "School exam (test)"), (pending.ProposedDate, pending.ProposedTime, pending.Reason));
        await amira.Post($"class-requests/{requestId}/answer", new AnswerClassChangeRequest(true, null, null, "See you then"));

        var session = await admin.Get<SessionDetailDto>($"sessions/{sessionId}");
        Assert.Equal(ZonedTime.ToUtc(proposed.Date, proposed.Time, "Asia/Dubai").Utc, session.StartUtc);
        var answered = (await aisha.Get<List<ClassChangeRequestDto>>("class-requests")).Single(r => r.Id == requestId);
        Assert.Equal(("Accepted", "See you then"), (answered.Status, answered.ResponseNote));
        Assert.Equal(HttpStatusCode.Conflict, (await amira.PostRaw($"class-requests/{requestId}/answer", new AnswerClassChangeRequest(false, null, null, null))).StatusCode);
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == aisha.UserId && n.Type == NotificationType.ClassChangeAnswered && n.DedupeKey == $"class-change-answer:{requestId}"));
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == amira.UserId && n.Type == NotificationType.ClassChangeRequested && n.DedupeKey == $"class-change:{requestId}"));
    }

    [Fact]
    public async Task A_postpone_request_can_be_withdrawn_or_declined_and_the_class_stays()
    {
        var (admin, amira, aisha, sessionId, _) = await ClassAsync();
        var before = (await admin.Get<SessionDetailDto>($"sessions/{sessionId}")).StartUtc;
        var first = (await aisha.Post<IdResponse>("class-requests", new CreateClassChangeRequest(sessionId, "Postpone", null, null, "Travelling (test)"))).Id;
        await aisha.Post($"class-requests/{first}/withdraw", null);
        var second = (await aisha.Post<IdResponse>("class-requests", new CreateClassChangeRequest(sessionId, "Postpone", null, null, "Travelling (test)"))).Id;
        // Accepting a postpone needs the new time from the teacher.
        Assert.Equal(HttpStatusCode.BadRequest, (await amira.PostRaw($"class-requests/{second}/answer", new AnswerClassChangeRequest(true, null, null, null))).StatusCode);
        await amira.Post($"class-requests/{second}/answer", new AnswerClassChangeRequest(false, null, null, "Exams are close; let's keep it."));
        Assert.Equal(before, (await admin.Get<SessionDetailDto>($"sessions/{sessionId}")).StartUtc);
        var mine = await aisha.Get<List<ClassChangeRequestDto>>("class-requests");
        Assert.Equal("Withdrawn", mine.Single(r => r.Id == first).Status);
        Assert.Equal("Declined", mine.Single(r => r.Id == second).Status);
    }

    [Fact]
    public async Task Passwords_need_six_characters_and_common_ones_are_refused()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var created = await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Password {tag} (Demo Student)",
            $"pw-{tag}@horizon.example.test", $"PW-{tag}", null, null, "Asia/Dubai"));
        var student = await api.LoginAsync($"pw-{tag}@horizon.example.test", created.TemporaryPassword);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostRaw("me/password", new ChangePasswordRequest(created.TemporaryPassword, "tulip"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostRaw("me/password", new ChangePasswordRequest(created.TemporaryPassword, "123456"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostRaw("me/password", new ChangePasswordRequest(created.TemporaryPassword, $"pw-{tag}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostRaw("me/password", new ChangePasswordRequest(created.TemporaryPassword, "tulip7"))).StatusCode);
        await api.LoginAsync($"pw-{tag}@horizon.example.test", "tulip7");
    }
}
