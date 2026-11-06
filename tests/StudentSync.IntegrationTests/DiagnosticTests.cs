using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.People;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Diagnostic assessments (CLAUDE.md addendum C): an admin builds one from the question bank across subjects, staff enter a
/// student's paper answers against the key, the breakdown shows weak chapters, and staff turn chosen rows into learning
/// needs with source Diagnostic. Keys stay hidden and students never touch the staff endpoints.
/// </summary>
public sealed class DiagnosticTests(ApiFixture api)
{
    private async Task<(Guid StudentId, string Email, string Password)> NewStudentAsync(Client admin)
    {
        var email = $"diag{Guid.NewGuid():N}@horizon.example.test";
        var created = await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Diagnostic Test (Demo Student)", email, $"D-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"));
        var first = await api.LoginAsync(email, created.TemporaryPassword);
        await first.Post("me/password", new ChangePasswordRequest(created.TemporaryPassword, "DiagnosticPass123"));
        return (created.Id, email, "DiagnosticPass123");
    }

    [Fact]
    public async Task Admin_builds_a_diagnostic_staff_enter_paper_answers_and_the_breakdown_feeds_learning_needs()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var subjects = (await admin.Get<List<SubjectDto>>("academics/subjects")).ToDictionary(s => s.Code);
        var maths = subjects["MATH10"]; var science = subjects["C10-SCI"];
        var priyaId = (await admin.Get<PagedResult<TeacherDto>>("teachers?search=priya&pageSize=5")).Items.Single(t => t.Email == Accounts.Priya).Id;
        var (studentId, email, password) = await NewStudentAsync(admin);

        // Bank: two maths questions in different chapters, one science question (single choice, 1 mark each).
        async Task<Guid> Q(SubjectDto s, Guid? chapter, string stem, string correct, string wrong) =>
            (await admin.Post<IdResponse>("questions", new SaveQuestionRequest(s.Id, chapter, QuestionType.SingleChoice, Difficulty.Easy, stem, null, 1, null, null, 0,
                [new SaveQuestionOption(correct, true, null), new SaveQuestionOption(wrong, false, null)]))).Id;
        var chapters = maths.Chapters.OrderBy(c => c.SortOrder).Take(2).ToList();
        var m1 = await Q(maths, chapters[0].Id, "Diag M1 (test)", "right", "wrong");
        var m2 = await Q(maths, chapters[1].Id, "Diag M2 (test)", "right", "wrong");
        var s1 = await Q(science, null, "Diag S1 (test)", "right", "wrong");

        // Admin creates it for one student, naming the teacher who runs it (no session or batch needed).
        var grade = (await admin.Get<List<GradeLevelDto>>("academics/grades")).First(g => g.Name == "Class 10");
        var curriculum = (await admin.Get<List<CurriculumDto>>("academics/curricula")).First(c => c.Code == "NCERT");
        var request = new SaveExamRequest("Entry diagnostic (test)", "Paper sheet", maths.Id, null, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), 45, 1, false,
            [new ExamQuestionSelection(m1, null), new ExamQuestionSelection(m2, null), new ExamQuestionSelection(s1, null)], [studentId], ExamPurpose.Diagnostic, grade.Id, curriculum.Id, priyaId);
        var missingTeacher = await admin.PostRaw("exams", request with { TeacherId = null });
        Assert.Equal(HttpStatusCode.BadRequest, missingTeacher.StatusCode);
        var examId = (await admin.Post<IdResponse>("exams", request)).Id;
        var detail = await admin.Get<ExamDetailDto>($"exams/{examId}");
        Assert.Equal((ExamPurpose.Diagnostic, grade.Id, curriculum.Id, priyaId), (detail.Purpose, detail.GradeLevelId, detail.CurriculumId, detail.TeacherId));
        Assert.All(detail.Questions, q => Assert.NotNull(q.Options));
        Assert.Contains(detail.Questions, q => q.ChapterTitle == chapters[0].Title);
        Assert.Contains(await admin.Get<List<ExamListItemDto>>($"exams?studentId={studentId}&purpose=Diagnostic"), e => e.Id == examId);

        // Draft: no paper entry yet. Published: the running teacher enters the sheet; a different teacher gets 404; a student 403.
        var priya = await api.LoginAsync(Accounts.Priya);
        var q = detail.Questions.ToDictionary(x => x.QuestionId);
        SaveAnswerRequest Pick(Guid questionId, string text) => new(q[questionId].ExamQuestionId, new ExamResponse(OptionIds: [q[questionId].Options!.First(o => o.Text == text).Id]));
        var paper = new PaperAttemptRequest(studentId, [Pick(m1, "wrong"), Pick(m2, "right"), Pick(s1, "right")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await priya.PostRaw($"exams/{examId}/paper-attempts", paper)).StatusCode);
        await admin.Post($"exams/{examId}/publish");
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Amira)).PostRaw($"exams/{examId}/paper-attempts", paper)).StatusCode);
        var student = await api.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostRaw($"exams/{examId}/paper-attempts", paper)).StatusCode);

        // Retried entry with the same Idempotency-Key replays; a fresh entry is refused because the single attempt is used.
        priya.Http.DefaultRequestHeaders.Add("Idempotency-Key", $"paper-{examId}");
        var result = await priya.Post<AttemptResultDto>($"exams/{examId}/paper-attempts", paper);
        var replay = await priya.Post<AttemptResultDto>($"exams/{examId}/paper-attempts", paper);
        priya.Http.DefaultRequestHeaders.Remove("Idempotency-Key");
        Assert.Equal(result.AttemptId, replay.AttemptId);
        Assert.Equal((AttemptStatus.Graded, AttemptEntryMode.PaperEntered, 2m, 3m), (result.Status, result.EntryMode, result.Score, result.TotalMarks));
        var again = await priya.PostRaw($"exams/{examId}/paper-attempts", paper);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal("no_attempts_left", await Client.Code(again));
        Assert.Single(await admin.Get<ExamResultsDto>($"exams/{examId}/results").ContinueWith(t => t.Result.Attempts));

        // Breakdown: chapter 1 flagged, chapter 2 and science fine; the student cannot see it before release, then can.
        var breakdown = await priya.Get<AttemptBreakdownDto>($"attempts/{result.AttemptId}/breakdown");
        var weak = breakdown.ByChapter.Single(r => r.ChapterId == chapters[0].Id);
        Assert.Equal((0m, true, false), (weak.Percent, weak.NeedsAttention, weak.AlreadyRecorded));
        Assert.False(breakdown.ByChapter.Single(r => r.ChapterId == chapters[1].Id).NeedsAttention);
        Assert.Equal(100m, breakdown.ByChapter.Single(r => r.SubjectId == science.Id).Percent);
        Assert.Equal(50m, breakdown.BySubject.Single(r => r.SubjectId == maths.Id).Percent);
        Assert.True(breakdown.CanRecordNeeds);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetRaw($"attempts/{result.AttemptId}/breakdown")).StatusCode);
        var raw = await (await student.GetRaw($"attempts/{result.AttemptId}/result")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswer\":\"right", raw);
        await priya.Post($"exams/{examId}/release-results");
        var mine = await student.Get<AttemptBreakdownDto>($"attempts/{result.AttemptId}/breakdown");
        Assert.False(mine.CanRecordNeeds);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostRaw($"attempts/{result.AttemptId}/learning-needs", new ApplyDiagnosticRequest([new DiagnosticNeedSelection(maths.Id, chapters[0].Id)], null))).StatusCode);

        // Staff record the weak chapter: a learning need with source Diagnostic; sending it again adds nothing; foreign subjects are refused.
        var applied = await priya.Post<ApplyDiagnosticResultDto>($"attempts/{result.AttemptId}/learning-needs", new ApplyDiagnosticRequest([new DiagnosticNeedSelection(maths.Id, chapters[0].Id)], "Needs revision (test)"));
        Assert.Equal((1, 0), (applied.Created, applied.Skipped));
        var twice = await priya.Post<ApplyDiagnosticResultDto>($"attempts/{result.AttemptId}/learning-needs", new ApplyDiagnosticRequest([new DiagnosticNeedSelection(maths.Id, chapters[0].Id)], null));
        Assert.Equal((0, 1), (twice.Created, twice.Skipped));
        var chem = subjects["CHEM10"].Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await priya.PostRaw($"attempts/{result.AttemptId}/learning-needs", new ApplyDiagnosticRequest([new DiagnosticNeedSelection(chem, null)], null))).StatusCode);
        var needs = await priya.Get<List<LearningNeedDto>>($"students/{studentId}/learning-needs");
        var need = Assert.Single(needs);
        Assert.Equal((LearningNeedSource.Diagnostic, maths.Id, chapters[0].Id, "Needs revision (test)"), (need.Source, need.SubjectId, need.ChapterId, need.Note));
        Assert.True((await priya.Get<AttemptBreakdownDto>($"attempts/{result.AttemptId}/breakdown")).ByChapter.Single(r => r.ChapterId == chapters[0].Id).AlreadyRecorded);
        // The student sees it on their plan and cannot withdraw it (not self-reported).
        var plan = await student.Get<LearningPlanDto>("my/learning-plan");
        Assert.Contains(plan.Needs, n => n.Id == need.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Http.DeleteAsync($"/api/v1/my/learning-needs/{need.Id}")).StatusCode);
        // Diagnostic needs cannot be typed in by hand.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{studentId}/learning-needs", new AddLearningNeedRequest(science.Id, null, LearningNeedSource.Diagnostic, null))).StatusCode);
    }

    [Fact]
    public async Task Question_images_are_private_and_served_only_to_staff_of_the_subject_and_the_exams_students()
    {
        var amira = await api.LoginAsync(Accounts.Amira); // maths teacher
        var mathsId = (await amira.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == "MATH10").Id;
        // A real 1×1 PNG.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        FileDto file;
        using (var form = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(png); content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(content, "file", "diagram.png"); form.Add(new StringContent("QuestionImage"), "purpose");
            var r = await amira.Http.PostAsync("/api/v1/files", form);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            file = (await r.Content.ReadFromJsonAsync<FileDto>(ApiFixture.Json))!;
        }
        var questionId = (await amira.Post<IdResponse>("questions", new SaveQuestionRequest(mathsId, null, QuestionType.TrueFalse, Difficulty.Easy, "Image question (test)", null, 1, null, null, 0,
            [new SaveQuestionOption("True", true, null), new SaveQuestionOption("False", false, null)], file.Id))).Id;
        Assert.Equal(file.Id, (await amira.Get<PagedResult<QuestionDto>>($"questions?subjectId={mathsId}&pageSize=100")).Items.Single(x => x.Id == questionId).ImageFileId);
        // Another teacher cannot claim someone else's upload for their own question.
        var priya = await api.LoginAsync(Accounts.Priya);
        var chemId = (await priya.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == "CHEM10").Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await priya.PostRaw("questions", new SaveQuestionRequest(chemId, null, QuestionType.TrueFalse, Difficulty.Easy, "Steal (test)", null, 1, null, null, 0,
            [new SaveQuestionOption("True", true, null), new SaveQuestionOption("False", false, null)], file.Id))).StatusCode);
        // The uploader and admins get a link; a student with no exam using the question does not.
        Assert.Equal(HttpStatusCode.OK, (await amira.GetRaw($"files/{file.Id}/link")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await api.LoginAsync(Accounts.Admin)).GetRaw($"files/{file.Id}/link")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Aisha)).GetRaw($"files/{file.Id}/link")).StatusCode);
    }

    [Fact]
    public async Task Demo_diagnostic_is_seeded_with_a_paper_attempt_and_a_flagged_chapter()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var hiro = (await admin.Get<PagedResult<StudentDto>>("students?search=hiro&pageSize=5")).Items.Single(s => s.Email == Accounts.Hiro);
        var diag = Assert.Single(await admin.Get<List<ExamListItemDto>>($"exams?studentId={hiro.Id}&purpose=Diagnostic"));
        Assert.Equal(ExamPurpose.Diagnostic, diag.Purpose);
        var breakdown = await admin.Get<AttemptBreakdownDto>($"attempts/{diag.LatestAttemptId}/breakdown");
        Assert.Equal(4m, breakdown.Score);
        Assert.Single(breakdown.ByChapter, r => r.NeedsAttention);
        var results = await admin.Get<ExamResultsDto>($"exams/{diag.Id}/results");
        Assert.Equal(AttemptEntryMode.PaperEntered, results.Attempts.Single().EntryMode);
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Exams.IgnoreQueryFilters().CountAsync(e => e.Purpose == ExamPurpose.Diagnostic));
    }
}
