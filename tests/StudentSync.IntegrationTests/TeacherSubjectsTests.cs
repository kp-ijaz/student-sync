using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// One rule connects everything a teacher creates: they work only in the subjects on their profile (set by the admin).
/// Subject pickers ask for "mine"; classes, exams, assignments and questions in other subjects are refused; admins are not limited.
/// </summary>
public sealed class TeacherSubjectsTests(ApiFixture api)
{
    [Fact]
    public async Task Teachers_create_work_only_in_their_own_subjects()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var amira = await api.LoginAsync(Accounts.Amira);
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Geography {Guid.NewGuid():N}"[..20]))).Id;

        // "Mine" lists only the teacher's subjects; admins and the full list still see every subject.
        var mine = await amira.Get<List<SubjectDto>>("academics/subjects?mine=true");
        Assert.NotEmpty(mine);
        Assert.All(mine, s => Assert.Contains(s.Teachers, t => t.Id == amira.UserId));
        Assert.DoesNotContain(mine, s => s.Id == subject);
        Assert.Contains(await admin.Get<List<SubjectDto>>("academics/subjects?mine=true"), s => s.Id == subject);

        // Not on her profile: every kind of new work in that subject is refused with a clear reason.
        var question = new SaveQuestionRequest(subject, null, QuestionType.TrueFalse, Difficulty.Easy, "Rivers flow downhill (test)", null, 1, null, null, 0,
            [new("True", true, null), new("False", false, null)]);
        var refused = await amira.PostRaw("questions", question);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("subjects you teach", await refused.Content.ReadAsStringAsync());
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(700 + Random.Shared.Next(300)));
        var student = (await amira.Get<PagedResult<StudentDto>>("students?pageSize=1")).Items.Single().Id;
        var session = new CreateSessionRequest("Geography · test", SessionType.OneToOne, null, null, null, null, subject, null, [student], "Asia/Dubai",
            day, new TimeOnly(Random.Shared.Next(1, 22), 0), 45, DeliveryMode.Online, null, null, null, null);
        Assert.Equal(HttpStatusCode.BadRequest, (await amira.PostRaw("sessions", session)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await amira.PostRaw("assignments", new SaveAssignmentRequest("Map work (test)", "Label the rivers.",
            DateTime.UtcNow.AddDays(3), 10, true, false, null, null, subject, [student], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await amira.PostRaw("exams", new SaveExamRequest("Geography quiz (test)", null, subject, null, null,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7), 30, 1, false, [], [student]))).StatusCode);

        // Once the admin adds the subject to her profile, it appears in her pickers and the same work is accepted.
        await admin.Post($"academics/subjects/{subject}/teachers", new AssignTeacherRequest(amira.UserId));
        Assert.Contains(await amira.Get<List<SubjectDto>>("academics/subjects?mine=true"), s => s.Id == subject);
        Assert.NotEqual(Guid.Empty, (await amira.Post<IdResponse>("questions", question)).Id);
        Assert.Single((await amira.Post<CreateSessionResponse>("sessions", session)).SessionIds);
        await admin.Http.DeleteAsync($"/api/v1/academics/subjects/{subject}/teachers/{amira.UserId}");
    }
}
