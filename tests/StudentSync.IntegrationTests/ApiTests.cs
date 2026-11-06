using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Application.Common;
using StudentSync.Application.Doubts;
using StudentSync.Contracts;
using StudentSync.Domain.AI;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Scheduling;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Seeding;

namespace StudentSync.IntegrationTests;

public sealed class AuthTests(ApiFixture api)
{
    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_generic_error()
    {
        var http = api.Anonymous();
        var a = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Accounts.Aisha, "wrong-password-1A", null, null));
        var b = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@example.test", "wrong-password-1A", null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, a.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, b.StatusCode);
        Assert.Equal(await Client.Code(a), await Client.Code(b));
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync("/api/v1/sessions")).StatusCode);

    [Fact]
    public async Task Refresh_token_rotation_detects_reuse_and_revokes_the_session_family()
    {
        var c = await api.LoginAsync(Accounts.Ben);
        var http = api.Anonymous();
        var first = await http.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(c.Auth.RefreshToken));
        Assert.True(first.IsSuccessStatusCode);
        var rotated = (await first.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        // Presenting the old token again (theft scenario) fails and revokes the rotated token too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(c.Auth.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task New_account_with_temporary_password_must_change_it_before_using_the_api()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var created = await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Temp Pass Student (Test)", $"temp{Guid.NewGuid():N}@horizon.example.test", $"T-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"));
        var profile = await admin.Get<StudentProfileDto>($"students/{created.Id}");
        var student = await api.LoginAsync(profile.Student.Email, created.TemporaryPassword);
        Assert.True(student.Auth.User.MustChangePassword);
        var blocked = await student.GetRaw("sessions");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("password_change_required", await Client.Code(blocked));
        await student.Post("me/password", new ChangePasswordRequest(created.TemporaryPassword, "NewSecurePass123"));
        var again = await api.LoginAsync(profile.Student.Email, "NewSecurePass123");
        Assert.False(again.Auth.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await again.GetRaw("sessions")).StatusCode);
    }

    [Fact]
    public async Task There_is_no_public_registration_and_only_admins_create_student_accounts()
    {
        var http = api.Anonymous();
        // The routes are gone: anonymous callers get the generic 401 of the API (or 404), never a 2xx.
        Assert.Contains((await http.GetAsync("/api/v1/auth/registration")).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
        Assert.Contains((await http.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Nobody", email = "nobody@example.test", password = "Register-Pass-123" })).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
        var teacher = await api.LoginAsync(Accounts.Amira);
        var request = new CreateStudentRequest("Teacher Made (Test)", $"tm{Guid.NewGuid():N}@horizon.example.test", $"TM-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostRaw("students", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Admin)).GetRaw("students/registrations")).StatusCode);
    }

    [Fact]
    public async Task Admin_password_reset_issues_a_temporary_password_that_must_be_changed_and_ends_other_sessions()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var email = $"reset{Guid.NewGuid():N}@horizon.example.test";
        var created = await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Reset Me (Test)", email, $"R-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"));
        var student = await api.LoginAsync(email, created.TemporaryPassword);
        await student.Post("me/password", new ChangePasswordRequest(created.TemporaryPassword, "FirstChosenPass123"));
        student = await api.LoginAsync(email, "FirstChosenPass123");

        // Teachers cannot reset passwords; another institution cannot see the student.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).PostRaw($"students/{created.Id}/password-reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.OtherAdmin)).PostRaw($"students/{created.Id}/password-reset", null)).StatusCode);

        var reset = await admin.Post<TemporaryPasswordResponse>($"students/{created.Id}/password-reset", null);
        Assert.NotEqual("FirstChosenPass123", reset.TemporaryPassword);
        // The old password no longer works and the existing session is signed out.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "FirstChosenPass123", null, null), ApiFixture.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(student.Auth.RefreshToken), ApiFixture.Json)).StatusCode);
        var again = await api.LoginAsync(email, reset.TemporaryPassword);
        Assert.True(again.Auth.User.MustChangePassword);
        Assert.Equal("password_change_required", await Client.Code(await again.GetRaw("sessions")));

        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.Action == "student.password_reset" && a.EntityId == created.Id.ToString()).ToListAsync();
        Assert.Single(audit);
        Assert.DoesNotContain(reset.TemporaryPassword, audit[0].Summary ?? "");
    }
}

public sealed class TenantIsolationTests(ApiFixture api)
{
    private async Task<Guid> HorizonSessionAsync() =>
        (await (await api.LoginAsync(Accounts.Amira)).Get<List<SessionListItemDto>>("sessions?limit=5")).First().Id;

    [Fact]
    public async Task Other_institution_cannot_read_or_change_sessions_by_id()
    {
        var sessionId = await HorizonSessionAsync();
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"sessions/{sessionId}/attendance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostRaw($"sessions/{sessionId}/cancel", new CancelSessionRequest("hijack", false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"sessions/{sessionId}/notes")).StatusCode);
        Assert.DoesNotContain((await other.Get<List<SessionListItemDto>>("sessions?limit=500")), s => s.Id == sessionId);
    }

    [Fact]
    public async Task Other_institution_cannot_see_students_or_doubts()
    {
        var horizonAdmin = await api.LoginAsync(Accounts.Admin);
        var aisha = (await horizonAdmin.Get<PagedResult<StudentDto>>("students?search=Aisha")).Items.Single();
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"students/{aisha.Id}")).StatusCode);
        Assert.Empty((await other.Get<PagedResult<StudentDto>>("students?search=Aisha")).Items);
        Assert.Empty((await other.Get<PagedResult<DoubtListItemDto>>("doubts")).Items);
    }

    [Fact]
    public async Task Cross_tenant_ids_are_rejected_when_scheduling()
    {
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        var horizonTeacher = (await api.LoginAsync(Accounts.Amira)).UserId;
        var r = await other.PostRaw("sessions", NewSession(SessionType.OneToOne, horizonTeacher, [Guid.NewGuid()], DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Super_admin_sees_aggregates_but_no_institution_learning_content()
    {
        var sa = await api.LoginAsync(Accounts.SuperAdmin);
        var overview = await sa.Get<PlatformOverviewDto>("platform/overview");
        Assert.True(overview.Tenants >= 2);
        Assert.Empty(await sa.Get<List<SessionListItemDto>>("sessions"));
        Assert.Equal(HttpStatusCode.NotFound, (await sa.GetRaw($"sessions/{await HorizonSessionAsync()}")).StatusCode);
    }

    [Fact]
    public async Task Institution_users_cannot_use_platform_administration()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetRaw("platform/tenants")).StatusCode);
    }

    internal static CreateSessionRequest NewSession(SessionType type, Guid? teacher, List<Guid>? students, DateOnly date, Guid? batch = null, RecurrenceRequest? rec = null, string tz = "Asia/Dubai", TimeOnly? time = null)
        => new($"Test {type} {Guid.NewGuid():N}"[..30], type, teacher, batch, null, null, null, null, students, tz, date, time ?? new TimeOnly(9, 0), 60, DeliveryMode.Online, null, null, null, rec, true);
}

public sealed class TeachingModeTests(ApiFixture api)
{
    private static DateOnly Future(int days) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days));

    private async Task<Guid> StudentId(string search) =>
        (await (await api.LoginAsync(Accounts.Admin)).Get<PagedResult<StudentDto>>($"students?search={search}")).Items.Single().Id;

    [Fact]
    public async Task One_to_one_session_works_end_to_end_without_a_batch()
    {
        var teacher = await api.LoginAsync(Accounts.Priya);
        var hiro = await StudentId("Hiro");
        var created = await teacher.Post<CreateSessionResponse>("sessions", TenantIsolationTests.NewSession(SessionType.OneToOne, null, [hiro], Future(45), time: new TimeOnly(6, 0)));
        var id = created.SessionIds.Single();

        var detail = await teacher.Get<SessionDetailDto>($"sessions/{id}");
        Assert.Null(detail.BatchId);
        Assert.Equal(SessionType.OneToOne, detail.SessionType);
        Assert.Single(detail.Participants);

        // The student sees it; an unrelated student does not.
        var student = await api.LoginAsync("hiro@horizon.example.test");
        Assert.Equal(HttpStatusCode.OK, (await student.GetRaw($"sessions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Ben)).GetRaw($"sessions/{id}")).StatusCode);

        // A one-to-one session cannot take a second student.
        var second = await teacher.PostRaw($"sessions/{id}/participants", new AddParticipantRequest(await StudentId("Ben")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task Ad_hoc_group_session_without_batch_and_teacher_double_booking_is_blocked()
    {
        var teacher = await api.LoginAsync(Accounts.Daniel);
        var date = Future(50);
        var students = new List<Guid> { await StudentId("Chloe"), await StudentId("Dev") };
        var created = await teacher.Post<CreateSessionResponse>("sessions", TenantIsolationTests.NewSession(SessionType.Group, null, students, date, time: new TimeOnly(7, 0)));
        Assert.Null((await teacher.Get<SessionDetailDto>($"sessions/{created.SessionIds[0]}")).BatchId);

        var clash = await teacher.PostRaw("sessions", TenantIsolationTests.NewSession(SessionType.Group, null, [await StudentId("Ethan")], date, time: new TimeOnly(7, 30)));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("teacher_conflict", await Client.Code(clash));
    }

    [Fact]
    public async Task Batch_session_requires_a_batch()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var amira = (await api.LoginAsync(Accounts.Amira)).UserId;
        var r = await admin.PostRaw("sessions", TenantIsolationTests.NewSession(SessionType.Batch, amira, null, Future(60)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("batch_required", await Client.Code(r));
    }

    [Fact]
    public async Task Recurring_one_to_one_keeps_local_time_across_daylight_saving_change()
    {
        var teacher = await api.LoginAsync(Accounts.Priya);
        var gabriel = await StudentId("Gabriel");
        // Saturdays 08:00 London either side of the BST change on Sunday 28 March 2027 (GMT, GMT, then BST).
        var req = TenantIsolationTests.NewSession(SessionType.OneToOne, null, [gabriel], new DateOnly(2027, 3, 20),
            rec: new RecurrenceRequest(RecurrenceFrequency.Weekly, 1, WeekDays.Saturday, null, 3), tz: "Europe/London", time: new TimeOnly(8, 0));
        var created = await teacher.Post<CreateSessionResponse>("sessions", req);
        Assert.Equal(3, created.SessionIds.Count);
        Assert.NotNull(created.SeriesId);
        var starts = new List<DateTime>();
        foreach (var id in created.SessionIds) starts.Add((await teacher.Get<SessionDetailDto>($"sessions/{id}")).StartUtc);
        Assert.Equal([8, 8, 7], starts.Select(s => s.Hour));
        Assert.All(starts, s => Assert.Equal(8, ZonedTime.ToLocal(s, "Europe/London").Hour));

        // Re-creating the identical series still works (a different series id), but within a series dates are unique.
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dupes = await db.Sessions.IgnoreQueryFilters().Where(s => s.SeriesId == created.SeriesId).GroupBy(s => s.OccurrenceDate).CountAsync(g => g.Count() > 1);
        Assert.Equal(0, dupes);
    }

    [Fact]
    public async Task Students_never_see_classmates_in_participant_lists()
    {
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var sessionId = (await aisha.Get<List<SessionListItemDto>>("sessions?limit=50")).First(s => s.SessionType == SessionType.Batch).Id;
        var detail = await aisha.Get<SessionDetailDto>($"sessions/{sessionId}");
        Assert.True(detail.ParticipantCount > 1);
        Assert.Single(detail.Participants);
        Assert.Equal(aisha.UserId, detail.Participants[0].StudentId);
    }

    [Fact]
    public async Task Attendance_works_for_a_single_student_session()
    {
        var teacher = await api.LoginAsync(Accounts.Priya);
        var completed = (await teacher.Get<List<SessionListItemDto>>("sessions?status=Completed&limit=50")).First(s => s.SessionType == SessionType.OneToOne);
        var roster = await teacher.Get<AttendanceRosterDto>($"sessions/{completed.Id}/attendance");
        Assert.True(roster.CanEdit);
        var row = Assert.Single(roster.Rows);
        await teacher.Put($"sessions/{completed.Id}/attendance", new MarkAttendanceRequest([new AttendanceEntry(row.StudentId, AttendanceStatus.Late, "Traffic")]));
        Assert.Equal(AttendanceStatus.Late, (await teacher.Get<AttendanceRosterDto>($"sessions/{completed.Id}/attendance")).Rows.Single().Status);
    }
}

public sealed class LearningWorkflowTests(ApiFixture api)
{
    [Fact]
    public async Task Assignment_for_one_to_one_session_is_submitted_and_graded()
    {
        var teacher = await api.LoginAsync(Accounts.Priya);
        var session = (await teacher.Get<List<SessionListItemDto>>("sessions?limit=50")).First(s => s.SessionType == SessionType.OneToOne && s.Title.Contains("Fatima"));
        var id = (await teacher.Post<IdResponse>("assignments", new SaveAssignmentRequest("Balancing equations (test)", "Balance 5 equations.", DateTime.UtcNow.AddDays(2), 10, false, false, session.Id, null, null, null, null))).Id;
        await teacher.Post($"assignments/{id}/publish");

        var fatima = await api.LoginAsync(Accounts.Fatima);
        await fatima.Put($"assignments/{id}/my-submission", new SaveSubmissionRequest("All five balanced.", null, true, null));
        var subs = await teacher.Get<List<SubmissionDto>>($"assignments/{id}/submissions");
        var sub = Assert.Single(subs);
        await teacher.Post($"submissions/{sub.Id}/grade", new GradeSubmissionRequest(9, "Excellent", false, sub.RowVersion));

        var mine = await fatima.Get<SubmissionDto>($"assignments/{id}/my-submission");
        Assert.Equal(9, mine.Marks);
        // Another student cannot open it.
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Ben)).GetRaw($"assignments/{id}")).StatusCode);
    }

    [Fact]
    public async Task Doubt_is_answered_confirmed_and_unresolved_doubts_follow_up_into_next_session_once()
    {
        var fatima = await api.LoginAsync(Accounts.Fatima);
        var subject = (await fatima.Get<List<SubjectDto>>("academics/subjects")).First(s => s.Name == "Chemistry");
        var doubtId = (await fatima.Post<IdResponse>("doubts", new CreateDoubtRequest("Ionic vs covalent (test)", "When does an ionic bond form?", subject.Id, null, null, null, null, true, null))).Id;
        var detail = await fatima.Get<DoubtDetailDto>($"doubts/{doubtId}");
        Assert.Equal(DoubtStatus.Assigned, detail.Status); // routed to the teacher who teaches her chemistry 1:1

        // Follow-up job places it on the next relevant session's agenda, exactly once.
        var tenantId = fatima.Auth.User.TenantId!.Value;
        for (var run = 0; run < 2; run++)
        {
            using var scope = api.Factory.Services.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId);
            await scope.ServiceProvider.GetRequiredService<DoubtService>().RunFollowUpAsync(CancellationToken.None);
        }
        detail = await fatima.Get<DoubtDetailDto>($"doubts/{doubtId}");
        Assert.NotNull(detail.FollowUpSessionId);
        var priya = await api.LoginAsync(Accounts.Priya);
        var agenda = await priya.Get<List<AgendaItemDto>>($"sessions/{detail.FollowUpSessionId}/agenda");
        Assert.Single(agenda, a => a.DoubtId == doubtId);
        using (var scope = api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == fatima.UserId && n.DedupeKey.StartsWith($"doubt-followup:{doubtId}")));
        }

        // Teacher answers; only the student can confirm resolution.
        await priya.Post($"doubts/{doubtId}/replies", new ReplyDoubtRequest("Ionic bonds form between metals and non-metals.", null, true));
        var resolveByTeacher = await priya.PutRaw($"doubts/{doubtId}", new UpdateDoubtRequest(priya.UserId, DoubtPriority.Normal, DoubtStatus.Resolved, detail.FollowUpSessionId, (await priya.Get<DoubtDetailDto>($"doubts/{doubtId}")).RowVersion));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resolveByTeacher.StatusCode);
        await fatima.Post($"doubts/{doubtId}/confirm", new ConfirmDoubtRequest(true, null));
        Assert.Equal(DoubtStatus.Resolved, (await fatima.Get<DoubtDetailDto>($"doubts/{doubtId}")).Status);
    }

    [Fact]
    public async Task Exam_attempt_autosaves_and_submission_is_idempotent_with_auto_grading()
    {
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var exam = (await aisha.Get<List<ExamListItemDto>>("exams")).Single(e => e.Title.StartsWith("Quadratics"));
        var attempt = await aisha.Post<AttemptDto>($"exams/{exam.Id}/attempts", null);
        // Answer keys are never sent to students.
        var raw = await (await aisha.GetRaw($"attempts/{attempt.AttemptId}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("isCorrect", raw);
        var tf = attempt.Questions.First(q => q.Type == QuestionType.TrueFalse);
        await aisha.Put($"attempts/{attempt.AttemptId}/answers", new SaveAnswerRequest(tf.ExamQuestionId, new ExamResponse(OptionIds: [tf.Options.First(o => o.Text == "False").Id])));
        var essay = attempt.Questions.First(q => q.Type == QuestionType.LongAnswer);
        await aisha.Put($"attempts/{attempt.AttemptId}/answers", new SaveAnswerRequest(essay.ExamQuestionId, new ExamResponse(Text: "When it doesn't factorise neatly.")));
        var resumed = await aisha.Post<AttemptDto>($"exams/{exam.Id}/attempts", null);
        Assert.Equal(attempt.AttemptId, resumed.AttemptId);

        var first = await aisha.Post<AttemptResultDto>($"attempts/{attempt.AttemptId}/submit", null);
        var second = await aisha.Post<AttemptResultDto>($"attempts/{attempt.AttemptId}/submit", null);
        Assert.Equal(first.Status, second.Status);
        Assert.True(first.RequiresManualGrading); // the written answer needs a teacher
        Assert.Equal(AttemptStatus.Submitted, first.Status);
        Assert.Null(first.Score); // results not released yet
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await aisha.PutRaw($"attempts/{attempt.AttemptId}/answers", new SaveAnswerRequest(tf.ExamQuestionId, new ExamResponse()))).StatusCode);
    }

    [Fact]
    public async Task Ai_content_requires_consent_and_teacher_approval_before_students_see_it()
    {
        var amira = await api.LoginAsync(Accounts.Amira);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        // The AI demo content belongs to the evening maths batch (her timetable classes have none).
        var sessions = (await amira.Get<List<SessionListItemDto>>("sessions?status=Completed&limit=200")).Where(s => s.Title == "G10 Maths — Evening A").ToList();

        // A session without recorded consent rejects transcript upload.
        var noConsent = sessions.OrderBy(s => s.StartUtc).First();
        using (var form = new MultipartFormDataContent { { new StringContent("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHello"), "file", "t.vtt" } })
        {
            var r = await amira.Http.PostAsync($"/api/v1/sessions/{noConsent.Id}/transcripts", form);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("consent_required", await Client.Code(r));
        }

        // Requesting a draft with no AI provider configured is recorded honestly as blocked.
        var status = await amira.Get<AiSessionStatusDto>($"sessions/{sessions.OrderByDescending(s => s.StartUtc).First().Id}/ai");
        Assert.False(status.LanguageModelConfigured);
        var job = await amira.PostRaw($"sessions/{status.SessionId}/ai/drafts", new GenerateDraftRequest(null));
        if (job.IsSuccessStatusCode) Assert.Equal(AiJobStatus.Blocked, (await job.Content.ReadFromJsonAsync<AiJobDto>(ApiFixture.Json))!.Status);
        else Assert.Equal("draft_exists", await Client.Code(job)); // the demo draft already awaits review

        // The demo draft awaiting review cannot be published, and students cannot open drafts.
        var draft = status.Drafts.First(d => d.Status == AiDraftStatus.AwaitingTeacherReview);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await amira.PostRaw($"ai/drafts/{draft.Id}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.GetRaw($"ai/drafts/{draft.Id}")).StatusCode);
        var notesBefore = (await aisha.Get<List<NoteDto>>($"sessions/{status.SessionId}/notes")).Count;

        await amira.Post($"ai/drafts/{draft.Id}/approve");
        await amira.Post($"ai/drafts/{draft.Id}/publish");
        var notes = await aisha.Get<List<NoteDto>>($"sessions/{status.SessionId}/notes");
        Assert.Equal(notesBefore + 1, notes.Count);
        Assert.Contains(notes, n => n.Source == ContentSource.AiApproved);
    }

    [Fact]
    public async Task Parent_accounts_cannot_sign_in_because_the_institute_has_no_parent_portal()
    {
        var r = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Accounts.ParentKhan, ApiFixture.Password, null, null), ApiFixture.Json);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("parent_access_disabled", await Client.Code(r));
        // A wrong password still gets the generic error, so the role is not revealed.
        var wrong = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Accounts.ParentKhan, "Wrong-Pass-123", null, null), ApiFixture.Json);
        Assert.Equal("invalid_credentials", await Client.Code(wrong));
    }

    [Fact]
    public async Task Private_files_need_an_authorised_short_lived_link()
    {
        var aisha = await api.LoginAsync(Accounts.Aisha);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("%PDF-1.4 test"u8.ToArray());
        content.Headers.ContentType = new("application/pdf");
        form.Add(content, "file", "work.pdf");
        form.Add(new StringContent("DoubtAttachment"), "purpose");
        var upload = await aisha.Http.PostAsync("/api/v1/files", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var file = (await upload.Content.ReadFromJsonAsync<FileDto>(ApiFixture.Json))!;

        var link = await aisha.Get<FileLinkDto>($"files/{file.Id}/link");
        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().GetAsync(link.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.Anonymous().GetAsync("/api/v1/files/download?token=forged")).StatusCode);
        // Another student cannot obtain a link to it.
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Ben)).GetRaw($"files/{file.Id}/link")).StatusCode);
    }

    [Fact]
    public async Task Files_with_mismatched_content_are_rejected()
    {
        var aisha = await api.LoginAsync(Accounts.Aisha);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("<script>alert(1)</script>"u8.ToArray());
        content.Headers.ContentType = new("image/png");
        form.Add(content, "file", "evil.png");
        form.Add(new StringContent("DoubtAttachment"), "purpose");
        Assert.Equal(HttpStatusCode.BadRequest, (await aisha.Http.PostAsync("/api/v1/files", form)).StatusCode);
    }

    [Fact]
    public async Task Prerecorded_video_is_locked_for_students_until_its_rule_or_a_teacher_unlocks_it()
    {
        var amira = await api.LoginAsync(Accounts.Amira);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var upcoming = (await aisha.Get<List<SessionListItemDto>>($"sessions?status=Scheduled&fromUtc={DateTime.UtcNow.AddDays(1):O}&limit=50"))
            .First(s => s.SessionType == SessionType.Batch && s.TeacherId == amira.UserId);

        using var form = new MultipartFormDataContent();
        var video = new ByteArrayContent([0, 0, 0, 0x18, .. "ftypisom"u8.ToArray(), 0, 0, 0, 0]);
        video.Headers.ContentType = new("video/mp4");
        form.Add(video, "file", "lesson.mp4");
        form.Add(new StringContent("Revision: quadratic equations"), "title");
        form.Add(new StringContent("AfterClass"), "unlockRule");
        form.Add(new StringContent("0"), "unlockOffsetMinutes");
        var upload = await amira.Http.PostAsync($"/api/v1/sessions/{upcoming.Id}/videos", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var videoId = (await upload.Content.ReadFromJsonAsync<IdResponse>(ApiFixture.Json))!.Id;
        var teacherView = (await amira.Get<List<PrerecordedVideoDto>>($"sessions/{upcoming.Id}/videos")).Single(v => v.Id == videoId);
        var fileId = teacherView.FileId!.Value;

        // Before the class has happened the student sees it as locked, with no file and no way to get a playback link.
        var locked = (await aisha.Get<List<PrerecordedVideoDto>>($"sessions/{upcoming.Id}/videos")).Single(v => v.Id == videoId);
        Assert.False(locked.IsUnlocked);
        Assert.Null(locked.FileId);
        Assert.Equal(upcoming.EndUtc, locked.UnlocksAtUtc);
        Assert.Equal(HttpStatusCode.NotFound, (await aisha.GetRaw($"files/{fileId}/link")).StatusCode);

        // Students cannot change access; the teacher can unlock early.
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.PostRaw($"videos/{videoId}/access", new SetVideoAccessRequest(VideoAccessOverride.Unlocked))).StatusCode);
        await amira.Post($"videos/{videoId}/access", new SetVideoAccessRequest(VideoAccessOverride.Unlocked));
        var unlocked = (await aisha.Get<PagedResult<PrerecordedVideoDto>>("videos?pageSize=100")).Items.Single(v => v.Id == videoId);
        Assert.True(unlocked.IsUnlocked);
        Assert.Equal(fileId, unlocked.FileId);
        var link = await aisha.Get<FileLinkDto>($"files/{fileId}/link");
        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().GetAsync(link.Url)).StatusCode);

        // Re-locking takes effect immediately for new links.
        await amira.Post($"videos/{videoId}/access", new SetVideoAccessRequest(VideoAccessOverride.Locked));
        Assert.Equal(HttpStatusCode.NotFound, (await aisha.GetRaw($"files/{fileId}/link")).StatusCode);

        // Students outside the class and other institutions see nothing.
        var hiro = await api.LoginAsync(Accounts.Hiro);
        Assert.Equal(HttpStatusCode.NotFound, (await hiro.GetRaw($"sessions/{upcoming.Id}/videos")).StatusCode);
        Assert.DoesNotContain((await hiro.Get<PagedResult<PrerecordedVideoDto>>("videos?pageSize=100")).Items, v => v.Id == videoId);
        var other = await api.LoginAsync(Accounts.OtherTeacher);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostRaw($"videos/{videoId}/access", new SetVideoAccessRequest(VideoAccessOverride.Unlocked))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"files/{fileId}/link")).StatusCode);
    }

    [Fact]
    public async Task Prerecorded_video_rejects_invalid_unlock_offsets_and_non_video_files()
    {
        var amira = await api.LoginAsync(Accounts.Amira);
        var session = (await amira.Get<List<SessionListItemDto>>("sessions?status=Scheduled&limit=50")).First();
        async Task<HttpResponseMessage> Upload(byte[] bytes, string type, string offset)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new(type);
            form.Add(file, "file", "x.bin");
            form.Add(new StringContent("Video"), "title");
            form.Add(new StringContent("BeforeClass"), "unlockRule");
            form.Add(new StringContent(offset), "unlockOffsetMinutes");
            return await amira.Http.PostAsync($"/api/v1/sessions/{session.Id}/videos", form);
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload([1, 2, 3], "video/mp4", "-5")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload("%PDF-1.4"u8.ToArray(), "application/pdf", "60")).StatusCode);
    }
}

public sealed class DataIntegrityTests(ApiFixture api)
{
    [Fact]
    public async Task Demo_seeder_is_idempotent()
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Count only seeded (IsDemo) accounts: other tests create users in parallel.
        var users = await db.Users.CountAsync(u => u.IsDemo);
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        var created = await seeder.SeedAsync(new DemoOptions { Password = ApiFixture.Password }, CancellationToken.None);
        Assert.False(created);
        Assert.Equal(users, await db.Users.CountAsync(u => u.IsDemo));
        // Demo photo seeding also runs on every seed, but accounts that already have a photo are left alone.
        Assert.Equal(0, await seeder.SeedPhotosAsync(CancellationToken.None));
        // The CBSE curriculum step is idempotent too, and the timetable never repeats an occurrence within a series.
        // (0 changes: nothing added, renamed or re-ordered; other tests create sessions in parallel, so totals are not compared.)
        Assert.Equal(0, await seeder.EnsureCurriculumAsync(CancellationToken.None));
        Assert.Equal(0, await db.Sessions.IgnoreQueryFilters().Where(s => s.SeriesId != null).GroupBy(s => new { s.SeriesId, s.OccurrenceDate }).CountAsync(g => g.Count() > 1));
    }

    [Fact]
    public async Task Notifications_are_deduplicated_by_key()
    {
        var ben = await api.LoginAsync(Accounts.Ben);
        using var scope = api.Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenant.BeginScope(ben.Auth.User.TenantId!.Value);
        var notify = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = "test-dedupe:" + Guid.NewGuid();
        await notify.NotifyAsync([ben.UserId], NotificationType.Announcement, "t", "b", null, key, CancellationToken.None);
        await notify.NotifyAsync([ben.UserId], NotificationType.Announcement, "t", "b", null, key, CancellationToken.None);
        await db.SaveChangesAsync();
        await notify.NotifyAsync([ben.UserId], NotificationType.Announcement, "t", "b", null, key, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.DedupeKey == key));
    }

    [Fact]
    public async Task Database_rejects_one_to_one_session_with_a_batch_and_cross_tenant_writes_are_blocked()
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var horizon = await db.Tenants.FirstAsync(t => t.Slug == DemoDataSeeder.PrimarySlug);
        var northbridge = await db.Tenants.FirstAsync(t => t.Slug == DemoDataSeeder.SecondarySlug);
        using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(horizon.Id);
        var existing = await db.Sessions.FirstAsync(s => s.BatchId != null);

        // Check constraint: a one-to-one session can never carry a batch, even if application checks were bypassed.
        var bad = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Sessions SET SessionType = 'OneToOne' WHERE Id = {existing.Id} AND 1 = 0");
        Assert.Equal(0, bad);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Sessions SET SessionType = 'OneToOne' WHERE Id = {existing.Id}"));

        // Writing a record for another tenant from this tenant's scope is blocked.
        db.Holidays.Add(new StudentSync.Domain.Tenancy.InstitutionHoliday { TenantId = northbridge.Id, Date = new DateOnly(2030, 1, 1), Name = "x" });
        await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }
}
