using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner decision 2026-11-24: a deleted student or teacher is gone from every working screen and figure (lists, pickers,
/// fees due, salaries, class lists, teacher's students, subjects, reports) and is kept in one place only, the Deleted
/// view of their list, from where an admin can restore them. Money already received or paid stays in Finance.
/// </summary>
public sealed class DeletedPeopleTests(ApiFixture api)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_deleted_student_and_teacher_disappear_everywhere_and_are_kept_in_one_place()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Gone {tag}"))).Id;
        Task<CreatedPersonResponse> Teacher(string n) => admin.Post<CreatedPersonResponse>("teachers",
            new CreateTeacherRequest($"{n} {tag} (Demo Teacher)", $"{n.ToLowerInvariant()}-{tag}@horizon.example.test", null, null, null, "Asia/Dubai", [subject]));
        Task<CreatedPersonResponse> Student(string n) => admin.Post<CreatedPersonResponse>("students",
            new CreateStudentRequest($"{n} {tag} (Demo Student)", $"{n.ToLowerInvariant()}-{tag}@horizon.example.test", $"{n[..2].ToUpperInvariant()}-{tag}", null, null, "Asia/Dubai"));
        var leaving = (await Teacher("Leaving")).Id;   // will be deleted
        var staying = (await Teacher("Staying")).Id;
        var gone = (await Student("Gone")).Id;         // will be deleted
        var kept = (await Student("Kept")).Id;
        await admin.Put($"teachers/{leaving}/pay/rate", new SetTeacherRateRequest(400, Today.AddDays(-300)));
        await admin.Post<IdResponse>($"students/{gone}/packages", new SavePackageRequest(10, 5000, Today.AddDays(-200), PaidNow: 1000));
        await admin.Post<IdResponse>($"students/{kept}/packages", new SavePackageRequest(10, 3000, Today.AddDays(-200), PaidNow: 3000));
        var feePaidOn = Today; // the first payment is dated today

        // Past classes: the leaving teacher with each student, and the staying teacher with the student who will be deleted.
        var day = Today.AddDays(-Random.Shared.Next(20, 150));
        async Task<Guid> Class(Guid teacher, Guid student, int hour, bool complete = true)
        {
            var id = (await admin.Post<CreateSessionResponse>("sessions", new CreateSessionRequest($"Gone class {tag}", SessionType.OneToOne, teacher, null, null, null, subject, null, [student],
                "Asia/Dubai", day, new TimeOnly(hour, 0), 60, DeliveryMode.InPerson, null, null, null, null))).SessionIds.Single();
            if (complete) await admin.Post($"sessions/{id}/status", new SetSessionStatusRequest(SessionStatus.Completed));
            return id;
        }
        var leavingWithGone = await Class(leaving, gone, 7);
        var leavingWithKept = await Class(leaving, kept, 9);
        var stayingWithGone = await Class(staying, gone, 11);
        var unmarked = await Class(leaving, kept, 13, complete: false); // past, never completed

        // A class with two students (older group classes): the kept student's class also has the student who will be deleted.
        // The app only creates one-to-one classes now, so the second student is added straight into the database.
        using (var scope = api.Factory.Services.CreateScope())
        {
            var tenantId = admin.Auth.User.TenantId!.Value;
            using var tenantScope = scope.ServiceProvider.GetRequiredService<StudentSync.Application.Common.ITenantContext>().BeginScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StudentSync.Infrastructure.Persistence.AppDbContext>();
            db.SessionParticipants.Add(new SessionParticipant { TenantId = tenantId, SessionId = leavingWithKept, StudentId = gone });
            await db.SaveChangesAsync();
        }
        Assert.Equal(2, (await admin.Get<SessionDetailDto>($"sessions/{leavingWithKept}")).Participants.Count);

        var month = new DateOnly(Today.Year, Today.Month, 1);
        string Range() => $"fromUtc={day.AddDays(-1):yyyy-MM-dd}T00:00:00Z&toUtc={day.AddDays(2):yyyy-MM-dd}T00:00:00Z&limit=500";
        async Task<List<Guid>> ClassIds(string extra = "") => (await admin.Get<List<SessionListItemDto>>($"sessions?{Range()}{extra}")).Select(s => s.Id).ToList();

        // Before: both are on the working screens.
        Assert.Contains((await admin.Get<FeesDueDto>("fees/due?take=200")).Items, i => i.StudentId == gone);
        Assert.Contains((await admin.Get<SalaryMonthDto>($"salaries?month={month:yyyy-MM-dd}")).Teachers, t => t.TeacherId == leaving);
        Assert.Contains(leavingWithGone, await ClassIds());

        Assert.False((await (await admin.DeleteRaw($"students/{gone}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json))!.Deleted);
        Assert.False((await (await admin.DeleteRaw($"teachers/{leaving}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json))!.Deleted);

        // People lists and pickers.
        Assert.DoesNotContain((await admin.Get<PagedResult<StudentDto>>($"students?pageSize=100&search={tag}")).Items, s => s.Id == gone);
        Assert.DoesNotContain((await admin.Get<PagedResult<TeacherDto>>($"teachers?pageSize=100&search={tag}")).Items, t => t.Id == leaving);
        Assert.DoesNotContain(await admin.Get<List<LookupItem>>($"students/lookup?search={tag}"), s => s.Id == gone);
        // The one place: the Deleted view (admin only), and the record still opens there.
        Assert.Contains((await admin.Get<PagedResult<StudentDto>>($"students?status=Archived&pageSize=100&search={tag}")).Items, s => s.Id == gone);
        Assert.Contains((await admin.Get<PagedResult<TeacherDto>>($"teachers?status=Archived&pageSize=100&search={tag}")).Items, t => t.Id == leaving);
        Assert.Equal(PersonStatus.Archived, (await admin.Get<StudentProfileDto>($"students/{gone}")).Student.Status);
        var amira = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw("students?status=Archived")).StatusCode);

        // Money: off Fees due and Salaries; what is still open is kept with the deleted records.
        Assert.DoesNotContain((await admin.Get<FeesDueDto>("fees/due?take=200")).Items, i => i.StudentId == gone);
        Assert.DoesNotContain((await admin.Get<SalaryMonthDto>($"salaries?month={month:yyyy-MM-dd}")).Teachers, t => t.TeacherId == leaving);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostRaw($"salaries/{leaving}/pay", new PaySalaryRequest(month, PaymentMethod.Cash, null))).StatusCode);
        var former = await admin.Get<FormerBalancesDto>("finance/former");
        Assert.Equal(4000m, Assert.Single(former.Students, s => s.StudentId == gone).Due);
        Assert.Equal(800m, Assert.Single(former.Teachers, t => t.TeacherId == leaving).Balance); // two completed hours × 400
        // The fee already received is real cash: it stays in Finance.
        Assert.Contains((await admin.Get<FinanceEntriesDto>($"finance/entries?from={feePaidOn:yyyy-MM-dd}&to={feePaidOn:yyyy-MM-dd}&kind=Income&search={tag}&pageSize=100")).Page.Items,
            e => e.PersonId == gone && e.Amount == 1000);

        // Classes: the deleted student's classes leave the lists; a current student's class stays even though its teacher
        // was deleted; the deleted student's own record still shows theirs.
        var listed = await ClassIds();
        Assert.DoesNotContain(leavingWithGone, listed);
        Assert.DoesNotContain(stayingWithGone, listed);
        Assert.Contains(leavingWithKept, listed);
        // The shared class: head-count, student list and attendance register are without the deleted student.
        Assert.Equal(1, (await admin.Get<List<SessionListItemDto>>($"sessions?{Range()}")).Single(x => x.Id == leavingWithKept).ParticipantCount);
        var shared = await admin.Get<SessionDetailDto>($"sessions/{leavingWithKept}");
        Assert.Equal((1, kept), (shared.ParticipantCount, Assert.Single(shared.Participants).StudentId));
        Assert.Equal(kept, Assert.Single((await admin.Get<AttendanceRosterDto>($"sessions/{leavingWithKept}/attendance")).Rows).StudentId);
        Assert.Equal("Former teacher", shared.TeacherName); // its teacher was deleted too
        var record = await ClassIds($"&studentId={gone}");
        Assert.Contains(leavingWithGone, record);
        Assert.Contains(stayingWithGone, record);
        // A class of a deleted teacher cannot be moved back onto the calendar.
        var detail = await admin.Get<SessionDetailDto>($"sessions/{unmarked}");
        var moved = await admin.PostRaw($"sessions/{unmarked}/reschedule", new RescheduleSessionRequest("Asia/Dubai", Today.AddDays(5), new TimeOnly(10, 0), 60, "Test", detail.RowVersion));
        Assert.Equal((HttpStatusCode)422, moved.StatusCode);
        Assert.Contains("person_deleted", await moved.Content.ReadAsStringAsync());

        // Teaching links: the staying teacher no longer has the deleted student; the subject no longer lists the deleted teacher.
        Assert.DoesNotContain((await admin.Get<TeacherOverviewDto>($"teachers/{staying}/overview")).Students, s => s.StudentId == gone);
        var subjects = await admin.Get<List<SubjectDto>>("academics/subjects");
        Assert.DoesNotContain(subjects.Single(s => s.Id == subject).Teachers, t => t.Id == leaving);
        Assert.Contains(subjects.Single(s => s.Id == subject).Teachers, t => t.Id == staying);
        // Reports: no row for the deleted teacher.
        var report = await admin.Get<InstitutionReportDto>($"reports/institution?from={day.AddDays(-1):yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}");
        Assert.DoesNotContain(report.Teachers, t => t.TeacherId == leaving);
        Assert.Contains(report.Teachers, t => t.TeacherId == staying);

        // Restore: back on the working screens.
        await admin.Post($"students/{gone}/restore", null);
        await admin.Post($"teachers/{leaving}/restore", null);
        Assert.Contains((await admin.Get<FeesDueDto>("fees/due?take=200")).Items, i => i.StudentId == gone);
        Assert.Contains((await admin.Get<SalaryMonthDto>($"salaries?month={month:yyyy-MM-dd}")).Teachers, t => t.TeacherId == leaving);
        Assert.Contains(leavingWithGone, await ClassIds());
        Assert.DoesNotContain((await admin.Get<FormerBalancesDto>("finance/former")).Students, s => s.StudentId == gone);
        var restored = await admin.Get<SessionDetailDto>($"sessions/{leavingWithKept}");
        Assert.Equal(2, restored.ParticipantCount);
        Assert.NotEqual("Former teacher", restored.TeacherName);
        // Only an admin can restore.
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.PostRaw($"students/{gone}/restore", null)).StatusCode);
    }

    /// <summary>
    /// The learning side: doubts, assignments, messages, notifications, "my teachers" and the name shown on past classes —
    /// and that Restore brings every one of them back, including the sign-in.
    /// </summary>
    [Fact]
    public async Task Learning_records_of_deleted_people_vanish_and_come_back_on_restore()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Learn {tag}"))).Id;

        // People who can sign in: each gets a first password, then chooses their own.
        async Task<(Guid Id, string Email, string Name)> Person(string kind, string n)
        {
            var email = $"{n.ToLowerInvariant()}-{tag}@horizon.example.test";
            var name = $"{n} {tag} (Demo {(kind == "teachers" ? "Teacher" : "Student")})";
            var created = kind == "teachers"
                ? await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest(name, email, null, null, null, "Asia/Dubai", [subject]))
                : await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest(name, email, $"{n[..2].ToUpperInvariant()}-{tag}", null, null, "Asia/Dubai"));
            var first = await api.LoginAsync(email, created.TemporaryPassword);
            await first.Post("me/password", new ChangePasswordRequest(created.TemporaryPassword!, Password(n)));
            return (created.Id, email, name);
        }
        string Password(string n) => $"Chosen-{n}-{tag}-9";
        var stay = await Person("teachers", "Stay");
        var leave = await Person("teachers", "Leave");   // will be deleted
        var gone = await Person("students", "Gone");     // will be deleted
        var kept = await Person("students", "Kept");
        foreach (var student in new[] { gone.Id, kept.Id })
            await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 3000, Today.AddDays(-200), PaidNow: 3000));

        var day = Today.AddDays(-Random.Shared.Next(20, 150));
        async Task<Guid> Class(Guid teacher, Guid student, int hour)
        {
            var id = (await admin.Post<CreateSessionResponse>("sessions", new CreateSessionRequest($"Learn class {tag}", SessionType.OneToOne, teacher, null, null, null, subject, null, [student],
                "Asia/Dubai", day, new TimeOnly(hour, 0), 60, DeliveryMode.InPerson, null, null, null, null))).SessionIds.Single();
            await admin.Post($"sessions/{id}/status", new SetSessionStatusRequest(SessionStatus.Completed));
            return id;
        }
        var stayWithGone = await Class(stay.Id, gone.Id, 7);
        await Class(stay.Id, kept.Id, 9);
        var leaveWithKept = await Class(leave.Id, kept.Id, 11);

        var stayTeacher = await api.LoginAsync(stay.Email, Password("Stay"));
        var goneStudent = await api.LoginAsync(gone.Email, Password("Gone"));
        var keptStudent = await api.LoginAsync(kept.Email, Password("Kept"));

        // The student who will be deleted: a doubt, a handed-in assignment and a conversation with their teacher.
        var doubt = (await goneStudent.Post<IdResponse>("doubts", new CreateDoubtRequest($"Doubt {tag}", "How does this work?", subject, null, null, null, null, true, null))).Id;
        var work = (await stayTeacher.Post<IdResponse>("assignments", new SaveAssignmentRequest($"Work {tag}", "Do it.", DateTime.UtcNow.AddDays(2), 10, false, false, stayWithGone, null, null, null, null))).Id;
        await stayTeacher.Post($"assignments/{work}/publish");
        await goneStudent.Put($"assignments/{work}/my-submission", new SaveSubmissionRequest("Done.", null, true, null));
        var chat = (await stayTeacher.Post<IdResponse>("conversations", new StartConversationRequest(gone.Id, $"Hello {tag}"))).Id;

        async Task<bool> DoubtListed(Client who) => (await who.Get<PagedResult<DoubtListItemDto>>($"doubts?pageSize=100&search={tag}")).Items.Any(d => d.Id == doubt);
        async Task<AssignmentListItemDto> WorkRow() => (await stayTeacher.Get<PagedResult<AssignmentListItemDto>>("assignments?pageSize=100")).Items.Single(a => a.Id == work);
        async Task<bool> ChatListed() => (await stayTeacher.Get<List<ConversationDto>>("conversations")).Any(c => c.Id == chat);
        async Task<List<MyTeacherDto>> KeptTeachers() => await keptStudent.Get<List<MyTeacherDto>>("my/teachers");
        async Task<string> NameOnKeptClass() => (await keptStudent.Get<List<SessionListItemDto>>("sessions?limit=200")).Single(s => s.Id == leaveWithKept).TeacherName;
        async Task<List<string>> NamesInKeptHours() =>
            (await admin.Get<StudentHoursDto>($"students/{kept.Id}/hours")).Subjects.Single(s => s.SubjectId == subject).Teachers.Select(t => t.Name).ToList();

        // Before: everything is there.
        Assert.True(await DoubtListed(admin));
        Assert.Equal((1, 1), ((await WorkRow()).RecipientCount, (await WorkRow()).SubmittedCount));
        Assert.Single(await stayTeacher.Get<List<SubmissionDto>>($"assignments/{work}/submissions"));
        Assert.True(await ChatListed());
        Assert.Contains(await KeptTeachers(), t => t.TeacherId == leave.Id);
        Assert.Equal(leave.Name, await NameOnKeptClass());
        Assert.Contains(leave.Name, await NamesInKeptHours());

        Assert.False((await (await admin.DeleteRaw($"students/{gone.Id}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json))!.Deleted);
        Assert.False((await (await admin.DeleteRaw($"teachers/{leave.Id}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json))!.Deleted);

        // Neither can sign in any more.
        foreach (var (email, n) in new[] { (gone.Email, "Gone"), (leave.Email, "Leave") })
            Assert.False((await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password(n), null, "tests"), ApiFixture.Json)).IsSuccessStatusCode);

        // The deleted student's doubt, work and conversation are off every list — for the admin and for their teacher.
        Assert.False(await DoubtListed(admin));
        Assert.False(await DoubtListed(stayTeacher));
        Assert.Equal((0, 0, 0), ((await WorkRow()).RecipientCount, (await WorkRow()).SubmittedCount, (await WorkRow()).GradedCount));
        Assert.Empty(await stayTeacher.Get<List<SubmissionDto>>($"assignments/{work}/submissions"));
        Assert.Empty((await stayTeacher.Get<AssignmentDetailDto>($"assignments/{work}")).Recipients);
        Assert.False(await ChatListed());
        Assert.DoesNotContain((await admin.Get<TeacherOverviewDto>($"teachers/{stay.Id}/overview")).Students, s => s.StudentId == gone.Id);
        // Their own class is still on their record, with its one student counted.
        Assert.Equal(1, (await admin.Get<List<SessionListItemDto>>($"sessions?studentId={gone.Id}&limit=200")).Single(s => s.Id == stayWithGone).ParticipantCount);

        // The deleted teacher: no longer one of the kept student's teachers; the class they taught stays in the student's
        // history under "Former teacher", without the name.
        Assert.DoesNotContain(await KeptTeachers(), t => t.TeacherId == leave.Id);
        Assert.Equal("Former teacher", await NameOnKeptClass());
        var names = await NamesInKeptHours();
        Assert.DoesNotContain(leave.Name, names);
        Assert.Contains("Former teacher", names);

        // New work cannot be set for the deleted student (they are no longer this teacher's student), and a notice to all
        // students does not wait for them either.
        Assert.Equal(HttpStatusCode.BadRequest, (await stayTeacher.PostRaw("assignments",
            new SaveAssignmentRequest($"Later {tag}", "Do it.", DateTime.UtcNow.AddDays(2), 10, false, false, stayWithGone, null, null, null, null))).StatusCode);
        await admin.Post<IdResponse>("announcements", new CreateAnnouncementRequest($"Later {tag}", "For everyone.", StudentSync.Domain.Communication.AnnouncementAudience.Students, null));

        // Restore: everything is back, and both can sign in again with the password they had.
        await admin.Post($"students/{gone.Id}/restore", null);
        await admin.Post($"teachers/{leave.Id}/restore", null);
        Assert.True(await DoubtListed(admin));
        Assert.Equal((1, 1), ((await WorkRow()).RecipientCount, (await WorkRow()).SubmittedCount));
        Assert.Single(await stayTeacher.Get<List<SubmissionDto>>($"assignments/{work}/submissions"));
        Assert.True(await ChatListed());
        Assert.Contains((await admin.Get<TeacherOverviewDto>($"teachers/{stay.Id}/overview")).Students, s => s.StudentId == gone.Id);
        Assert.Contains(await KeptTeachers(), t => t.TeacherId == leave.Id);
        Assert.Equal(leave.Name, await NameOnKeptClass());
        Assert.Contains(leave.Name, await NamesInKeptHours());
        Assert.Contains((await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Id == subject).Teachers, t => t.Id == leave.Id);
        var back = await api.LoginAsync(gone.Email, Password("Gone"));
        await api.LoginAsync(leave.Email, Password("Leave"));
        var notes = (await back.Get<PagedResult<NotificationDto>>("notifications?pageSize=100")).Items;
        Assert.DoesNotContain(notes, n => n.Title.Contains($"Later {tag}") || n.Body.Contains($"Later {tag}"));
        Assert.Contains((await keptStudent.Get<PagedResult<NotificationDto>>("notifications?pageSize=100")).Items, n => n.Title.Contains($"Later {tag}")); // current students did get it
        Assert.True((await back.Get<PagedResult<DoubtListItemDto>>("doubts?pageSize=100")).Items.Any(d => d.Id == doubt)); // their own doubt is theirs again
    }
}
