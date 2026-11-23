using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.People;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Admin CRUD added 2026-11-23: deleting students and teachers (permanently when nothing is recorded, otherwise archived
/// with history kept), the centre's fee plans, correcting a package, and removing a teacher's pay rate.
/// </summary>
public sealed class AdminCrudTests(ApiFixture api)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static CreateStudentRequest NewStudent(string tag, Guid? gradeId = null) => new(
        "Delete Test (Demo Student)", $"del-{tag}@horizon.example.test", $"DL-{tag}"[..12], gradeId, "Fictional Public School", "Asia/Dubai");

    private static CreateTeacherRequest NewTeacher(string tag, List<Guid>? subjects = null) => new(
        "Delete Test (Demo Teacher)", $"del-t-{tag}@horizon.example.test", null, null, null, "Asia/Dubai", subjects);

    private async Task<(Client Admin, Guid Subject, Guid Teacher)> TeacherWithSubjectAsync(string tag)
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Crud {tag}"))).Id;
        var teacher = (await admin.Post<CreatedPersonResponse>("teachers", NewTeacher(tag, [subject]))).Id;
        return (admin, subject, teacher);
    }

    private static Task<CreateSessionResponse> Book(Client admin, Guid teacher, Guid subject, Guid student, DateOnly day) =>
        admin.Post<CreateSessionResponse>("sessions", new CreateSessionRequest(
            "Crud class", SessionType.OneToOne, teacher, null, null, null, subject, null, [student], "Asia/Dubai",
            day, new TimeOnly(Random.Shared.Next(6, 20), 0), 60, DeliveryMode.InPerson, null, null, null, null));

    [Fact]
    public async Task A_student_with_no_records_is_deleted_for_good()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var id = (await admin.Post<CreatedPersonResponse>("students", NewStudent(tag) with { Goals = "Pass Class 10 maths" })).Id;

        var result = await (await admin.DeleteRaw($"students/{id}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json);
        Assert.True(result!.Deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"students/{id}")).StatusCode);
        Assert.DoesNotContain((await admin.Get<PagedResult<StudentDto>>("students?status=Archived&pageSize=100")).Items, s => s.Id == id);
        // The account is gone too, so the email can be used again.
        Assert.False((await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest($"del-{tag}@horizon.example.test", "anything-1", null, "tests"), ApiFixture.Json)).IsSuccessStatusCode);
        await admin.Post<CreatedPersonResponse>("students", NewStudent(tag));
    }

    [Fact]
    public async Task A_student_with_fees_is_archived_keeps_history_and_can_be_restored()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var id = (await admin.Post<CreatedPersonResponse>("students", NewStudent(Tag()))).Id;
        await admin.Post<IdResponse>($"students/{id}/packages", new SavePackageRequest(10, 5000, PaidNow: 1000));

        var result = await (await admin.DeleteRaw($"students/{id}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json);
        Assert.False(result!.Deleted);
        Assert.Contains("archived", result.Message);

        // Hidden from the normal list, findable with the Archived filter; fees and payments are untouched.
        Assert.DoesNotContain((await admin.Get<PagedResult<StudentDto>>("students?pageSize=100&search=DL-")).Items, s => s.Id == id);
        Assert.Contains((await admin.Get<PagedResult<StudentDto>>("students?status=Archived&pageSize=100")).Items, s => s.Id == id);
        var fees = await admin.Get<StudentFeesDto>($"students/{id}/fees");
        Assert.Equal((10m, 1000m), (fees.HoursBought, fees.Paid));

        // Deleting again is harmless; restoring is "set Active" through the normal edit.
        Assert.False((await (await admin.DeleteRaw($"students/{id}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json))!.Deleted);
        var profile = await admin.Get<StudentProfileDto>($"students/{id}");
        Assert.Equal(PersonStatus.Archived, profile.Student.Status);
        var s = profile.Student;
        await admin.Put($"students/{id}", new UpdateStudentRequest(s.FullName, s.StudentCode, s.GradeLevelId, s.SchoolName, PersonStatus.Active, s.TimeZoneId));
        Assert.Contains((await admin.Get<PagedResult<StudentDto>>("students?pageSize=100&search=DL-")).Items, x => x.Id == id);
    }

    [Fact]
    public async Task People_with_upcoming_classes_cannot_be_deleted_and_teachers_who_taught_are_archived()
    {
        var tag = Tag();
        var (admin, subject, teacher) = await TeacherWithSubjectAsync(tag);
        var student = (await admin.Post<CreatedPersonResponse>("students", NewStudent(tag))).Id;
        await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(20, 1000));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var past = (await Book(admin, teacher, subject, student, today.AddDays(-Random.Shared.Next(10, 60)))).SessionIds.Single();
        var upcoming = (await Book(admin, teacher, subject, student, today.AddDays(Random.Shared.Next(10, 60)))).SessionIds.Single();

        var refused = await admin.DeleteRaw($"students/{student}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("has_upcoming_classes", await Client.Code(refused));
        refused = await admin.DeleteRaw($"teachers/{teacher}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("has_upcoming_classes", await Client.Code(refused));

        // Cancel the upcoming class: the teacher has taught a class, so they are archived and the class keeps its teacher.
        await admin.Post($"sessions/{upcoming}/cancel", new CancelSessionRequest("Leaving the centre"));
        var result = await (await admin.DeleteRaw($"teachers/{teacher}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json);
        Assert.False(result!.Deleted);
        Assert.DoesNotContain((await admin.Get<PagedResult<TeacherDto>>("teachers?pageSize=100")).Items, t => t.Id == teacher);
        Assert.Contains((await admin.Get<PagedResult<TeacherDto>>("teachers?status=Archived&pageSize=100")).Items, t => t.Id == teacher);
        Assert.Equal(teacher, (await admin.Get<SessionDetailDto>($"sessions/{past}")).TeacherId);
    }

    [Fact]
    public async Task A_new_teacher_with_subjects_and_a_rate_is_deleted_for_good()
    {
        var tag = Tag();
        var (admin, _, teacher) = await TeacherWithSubjectAsync(tag);
        await admin.Put($"teachers/{teacher}/pay/rate", new SetTeacherRateRequest(300, null));

        var result = await (await admin.DeleteRaw($"teachers/{teacher}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json);
        Assert.True(result!.Deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"teachers/{teacher}/overview")).StatusCode);
    }

    [Fact]
    public async Task Only_the_centre_admin_can_delete_people()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var id = (await admin.Post<CreatedPersonResponse>("students", NewStudent(Tag()))).Id;
        var teacher = await api.LoginAsync(Accounts.Amira);
        var student = await api.LoginAsync(Accounts.Aisha);
        var other = await api.LoginAsync(Accounts.OtherAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.DeleteRaw($"students/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.DeleteRaw($"students/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.DeleteRaw($"teachers/{teacher.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteRaw($"students/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteRaw($"teachers/{teacher.UserId}")).StatusCode);
        Assert.Equal(PersonStatus.Active, (await admin.Get<StudentProfileDto>($"students/{id}")).Student.Status);
    }

    [Fact]
    public async Task Fee_plans_support_full_crud_and_offer_only_plans_that_fit_the_student()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var grades = await admin.Get<List<GradeLevelDto>>("academics/grades");
        Assert.True(grades.Count >= 2);
        var (mine, other) = (grades[0].Id, grades[1].Id);
        var student = (await admin.Post<CreatedPersonResponse>("students", NewStudent(Tag(), mine))).Id;
        var tag = Tag();

        var anyone = (await admin.Post<IdResponse>("fee-plans", new SaveFeePlanRequest(30, 15000, $"Any {tag}"))).Id;
        var forMine = (await admin.Post<IdResponse>("fee-plans", new SaveFeePlanRequest(20, 9000, $"Mine {tag}", mine))).Id;
        var forOther = (await admin.Post<IdResponse>("fee-plans", new SaveFeePlanRequest(20, 8000, $"Other {tag}", other))).Id;
        var hidden = (await admin.Post<IdResponse>("fee-plans", new SaveFeePlanRequest(10, 4000, $"Hidden {tag}", IsActive: false))).Id;

        var all = await admin.Get<List<FeePlanDto>>("fee-plans");
        var plan = Assert.Single(all, p => p.Id == anyone);
        Assert.Equal(500m, plan.PricePerHour);
        Assert.Contains(all, p => p.Id == hidden && !p.IsActive);

        var offered = (await admin.Get<List<FeePlanDto>>($"fee-plans?studentId={student}")).Select(p => p.Id).ToList();
        Assert.Contains(anyone, offered);
        Assert.Contains(forMine, offered);
        Assert.DoesNotContain(forOther, offered);
        Assert.DoesNotContain(hidden, offered);

        // Edit, then delete; a package added from the plan keeps its own hours and price.
        await admin.Put($"fee-plans/{anyone}", new SaveFeePlanRequest(40, 18000, $"Any {tag}"));
        Assert.Equal((40m, 18000m, 450m), (await admin.Get<List<FeePlanDto>>("fee-plans")).Single(p => p.Id == anyone) is var e ? (e.Hours, e.Price, e.PricePerHour) : default);
        await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(40, 18000, Name: $"Any {tag}"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"fee-plans/{anyone}")).StatusCode);
        Assert.DoesNotContain(await admin.Get<List<FeePlanDto>>("fee-plans"), p => p.Id == anyone);
        Assert.Equal((40m, 18000m), (await admin.Get<StudentFeesDto>($"students/{student}/fees")).Packages.Single() is var pk ? (pk.Hours, pk.Price) : default);

        // A class with a fee plan cannot be deleted without a clear message.
        var gradeDelete = await admin.DeleteRaw($"academics/grades/{other}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, gradeDelete.StatusCode);
        Assert.False(gradeDelete.IsSuccessStatusCode);

        // Validation, roles and other institutions.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("fee-plans", new SaveFeePlanRequest(0, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("fee-plans", new SaveFeePlanRequest(10, -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("fee-plans", new SaveFeePlanRequest(10, 100, GradeLevelId: Guid.NewGuid()))).StatusCode);
        var teacher = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetRaw("fee-plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostRaw("fee-plans", new SaveFeePlanRequest(10, 100))).StatusCode);
        var outsider = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.DoesNotContain(await outsider.Get<List<FeePlanDto>>("fee-plans"), p => p.Id == forMine);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutRaw($"fee-plans/{forMine}", new SaveFeePlanRequest(1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteRaw($"fee-plans/{forMine}")).StatusCode);

        foreach (var id in new[] { forMine, forOther, hidden }) await admin.DeleteRaw($"fee-plans/{id}");
    }

    [Fact]
    public async Task A_package_can_be_corrected_but_not_below_what_was_paid()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var student = (await admin.Post<CreatedPersonResponse>("students", NewStudent(Tag()))).Id;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var package = (await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 5000, PaidNow: 2000))).Id;

        await admin.Put($"students/{student}/packages/{package}", new UpdatePackageRequest(12, 6000, today.AddDays(-3), today.AddDays(7), "Term package", "Corrected"));
        var fees = await admin.Get<StudentFeesDto>($"students/{student}/fees");
        var p = fees.Packages.Single();
        Assert.Equal((12m, 6000m, "Term package", 4000m, today.AddDays(7)), (p.Hours, p.Price, p.Name, p.Balance, p.DueOn));
        Assert.Equal(12m, fees.HoursBought);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{student}/packages/{package}", new UpdatePackageRequest(12, 1500, today))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"students/{student}/packages/{package}", new UpdatePackageRequest(0, 6000, today))).StatusCode);
        var teacher = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutRaw($"students/{student}/packages/{package}", new UpdatePackageRequest(1, 1, today))).StatusCode);
        var other = (await admin.Post<CreatedPersonResponse>("students", NewStudent(Tag()))).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutRaw($"students/{other}/packages/{package}", new UpdatePackageRequest(1, 3000, today))).StatusCode);

        // A cancelled package cannot be edited.
        var spare = (await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(5, 100))).Id;
        await admin.Post($"students/{student}/packages/{spare}/cancel");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutRaw($"students/{student}/packages/{spare}", new UpdatePackageRequest(5, 100, today))).StatusCode);
    }

    [Fact]
    public async Task A_mistaken_teacher_rate_can_be_removed()
    {
        var (admin, _, teacher) = await TeacherWithSubjectAsync(Tag());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await admin.Put($"teachers/{teacher}/pay/rate", new SetTeacherRateRequest(300, today.AddDays(-30)));
        await admin.Put($"teachers/{teacher}/pay/rate", new SetTeacherRateRequest(3000, today));
        var pay = await admin.Get<TeacherPayDto>($"teachers/{teacher}/pay");
        Assert.Equal(3000m, pay.CurrentRate);
        var wrong = pay.Rates.Single(r => r.HourlyRate == 3000);

        var amira = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.DeleteRaw($"teachers/{teacher}/pay/rates/{wrong.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"teachers/{teacher}/pay/rates/{wrong.Id}")).StatusCode);
        pay = await admin.Get<TeacherPayDto>($"teachers/{teacher}/pay");
        Assert.Equal(300m, pay.CurrentRate);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteRaw($"teachers/{teacher}/pay/rates/{wrong.Id}")).StatusCode);
    }
}

/// <summary>Edit/delete added 2026-11-23 for chapters, topics, terms, holidays, announcements, assignments, exams and doubts.</summary>
public sealed class AdminCrudMoreTests(ApiFixture api)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Chapters_and_topics_can_be_renamed_and_removed_unless_in_use()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Chapters {tag}"))).Id;
        var chapter = (await admin.Post<IdResponse>($"academics/subjects/{subject}/chapters", new SaveChapterRequest("Algebra", 1))).Id;
        var topic = (await admin.Post<IdResponse>($"academics/chapters/{chapter}/topics", new SaveTopicRequest("Linear equations", 1))).Id;

        await admin.Put($"academics/chapters/{chapter}", new SaveChapterRequest("Algebra basics", 1));
        await admin.Put($"academics/topics/{topic}", new SaveTopicRequest("Equations in one variable", 1));
        var saved = (await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Id == subject).Chapters.Single();
        Assert.Equal(("Algebra basics", "Equations in one variable"), (saved.Title, saved.Topics.Single().Title));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"academics/chapters/{chapter}", new SaveChapterRequest(" ", 1))).StatusCode);

        // A chapter a student's weak-subject note points to is kept, with the reason.
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Chapter Test (Demo Student)", $"ch-{tag}@horizon.example.test", $"CH-{tag}", null, null, "Asia/Dubai"))).Id;
        await admin.Post<LearningNeedDto>($"students/{student}/learning-needs", new AddLearningNeedRequest(subject, chapter, LearningNeedSource.Admin, null));
        var refused = await admin.DeleteRaw($"academics/chapters/{chapter}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("still_in_use", await Client.Code(refused));

        var spare = (await admin.Post<IdResponse>($"academics/subjects/{subject}/chapters", new SaveChapterRequest("Spare", 2))).Id;
        await admin.Post<IdResponse>($"academics/chapters/{spare}/topics", new SaveTopicRequest("Spare topic", 1));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/topics/{topic}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/chapters/{spare}")).StatusCode);
        Assert.DoesNotContain((await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Id == subject).Chapters, c => c.Id == spare);

        var teacher = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.DeleteRaw($"academics/chapters/{chapter}")).StatusCode);
        var outsider = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutRaw($"academics/chapters/{chapter}", new SaveChapterRequest("X", 1))).StatusCode);
    }

    [Fact]
    public async Task Terms_can_be_edited_and_removed()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var start = new DateOnly(2040 + Random.Shared.Next(0, 50), 4, 1);
        var year = (await admin.Post<IdResponse>("academics/years", new SaveAcademicYearRequest($"Y {Tag()}", start, start.AddYears(1).AddDays(-1), false))).Id;
        var t1 = (await admin.Post<IdResponse>($"academics/years/{year}/terms", new SaveTermRequest("Term 1", start, start.AddMonths(4)))).Id;
        var t2 = (await admin.Post<IdResponse>($"academics/years/{year}/terms", new SaveTermRequest("Term 2", start.AddMonths(5), start.AddMonths(8)))).Id;

        await admin.Put($"academics/terms/{t1}", new SaveTermRequest("First term", start, start.AddMonths(3)));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"academics/terms/{t1}", new SaveTermRequest("First term", start, start.AddMonths(6)))).StatusCode); // overlaps Term 2
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"academics/terms/{t1}", new SaveTermRequest("First term", start.AddDays(-5), start.AddMonths(1)))).StatusCode); // outside the year
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/terms/{t2}")).StatusCode);
        var terms = (await admin.Get<List<AcademicYearDto>>("academics/years")).Single(y => y.Id == year).Terms;
        Assert.Equal("First term", Assert.Single(terms).Name);
        await admin.DeleteRaw($"academics/years/{year}");
    }

    [Fact]
    public async Task Holidays_can_be_edited()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var day = new DateOnly(2050 + Random.Shared.Next(0, 40), Random.Shared.Next(1, 12), Random.Shared.Next(1, 28));
        var id = (await admin.Post<IdResponse>("institution/holidays", new SaveHolidayRequest(day, "Onam (test)"))).Id;
        var other = (await admin.Post<IdResponse>("institution/holidays", new SaveHolidayRequest(day.AddDays(1), "Second day (test)"))).Id;

        await admin.Put($"institution/holidays/{id}", new SaveHolidayRequest(day.AddDays(-1), "Onam eve (test)"));
        Assert.Contains(await admin.Get<List<HolidayDto>>("institution/holidays"), h => h.Id == id && h.Date == day.AddDays(-1) && h.Name == "Onam eve (test)");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"institution/holidays/{id}", new SaveHolidayRequest(day.AddDays(1), "Clash"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).PutRaw($"institution/holidays/{id}", new SaveHolidayRequest(day, "X"))).StatusCode);
        await admin.DeleteRaw($"institution/holidays/{id}");
        await admin.DeleteRaw($"institution/holidays/{other}");
    }

    [Fact]
    public async Task Announcements_are_edited_and_deleted_by_the_admin_or_their_author_only()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var id = (await admin.Post<IdResponse>("announcements", new CreateAnnouncementRequest("Holiday notice (test)", "Closed on Friday.", AnnouncementAudience.Everyone, null))).Id;
        await admin.Put($"announcements/{id}", new UpdateAnnouncementRequest("Holiday notice (test)", "Closed on Saturday."));
        var mine = (await admin.Get<List<AnnouncementDto>>("announcements")).Single(a => a.Id == id);
        Assert.Equal(("Closed on Saturday.", admin.UserId), (mine.Body, mine.AuthorUserId));

        var teacher = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.PutRaw($"announcements/{id}", new UpdateAnnouncementRequest("X", "Y"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.DeleteRaw($"announcements/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Aisha)).DeleteRaw($"announcements/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.OtherAdmin)).DeleteRaw($"announcements/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"announcements/{id}")).StatusCode);
        Assert.DoesNotContain(await admin.Get<List<AnnouncementDto>>("announcements"), a => a.Id == id);
    }

    [Fact]
    public async Task Assignments_and_exams_are_deleted_only_before_students_take_part()
    {
        var priya = await api.LoginAsync(Accounts.Priya);
        var fatima = await api.LoginAsync(Accounts.Fatima);
        var session = (await priya.Get<List<SessionListItemDto>>("sessions?limit=50")).First(s => s.SessionType == SessionType.OneToOne && s.Title.Contains("Fatima"));
        SaveAssignmentRequest Work(string title) => new(title, "Do it.", DateTime.UtcNow.AddDays(2), 10, false, false, session.Id, null, null, null, null);

        var draft = (await priya.Post<IdResponse>("assignments", Work("Draft to delete (test)"))).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Amira)).DeleteRaw($"assignments/{draft}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await priya.DeleteRaw($"assignments/{draft}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await priya.GetRaw($"assignments/{draft}")).StatusCode);

        var handedIn = (await priya.Post<IdResponse>("assignments", Work("Handed in (test)"))).Id;
        await priya.Post($"assignments/{handedIn}/publish");
        await fatima.Put($"assignments/{handedIn}/my-submission", new SaveSubmissionRequest("Done.", null, true, null));
        var refused = await priya.DeleteRaw($"assignments/{handedIn}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("assignment_has_submissions", await Client.Code(refused));

        // Exams: a draft goes; once a student started an attempt, it stays.
        var chemistry = (await priya.Get<List<SubjectDto>>("academics/subjects")).First(s => s.Name == "Chemistry");
        var question = (await priya.Post<IdResponse>("questions", new SaveQuestionRequest(chemistry.Id, null, QuestionType.SingleChoice, Difficulty.Easy, $"Delete test {Tag()}", null, 1, null, null, 0,
            [new SaveQuestionOption("Yes", true, null), new SaveQuestionOption("No", false, null)]))).Id;
        SaveExamRequest Exam(string title) => new(title, null, chemistry.Id, null, null, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddDays(1), 30, 1, false,
            [new ExamQuestionSelection(question, null)], [fatima.UserId]);
        var examDraft = (await priya.Post<IdResponse>("exams", Exam("Exam draft (test)"))).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await priya.DeleteRaw($"exams/{examDraft}")).StatusCode);
        var taken = (await priya.Post<IdResponse>("exams", Exam("Exam taken (test)"))).Id;
        await priya.Post($"exams/{taken}/publish");
        await fatima.Post<AttemptDto>($"exams/{taken}/attempts", null);
        var kept = await priya.DeleteRaw($"exams/{taken}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, kept.StatusCode);
        Assert.Equal("exam_has_attempts", await Client.Code(kept));
    }

    [Fact]
    public async Task Only_the_admin_can_delete_a_doubt()
    {
        var fatima = await api.LoginAsync(Accounts.Fatima);
        var chemistry = (await fatima.Get<List<SubjectDto>>("academics/subjects")).First(s => s.Name == "Chemistry");
        var id = (await fatima.Post<IdResponse>("doubts", new CreateDoubtRequest("Posted by mistake (test)", "Ignore this.", chemistry.Id, null, null, null, null, true, null))).Id;
        var priya = await api.LoginAsync(Accounts.Priya);
        await priya.Post($"doubts/{id}/replies", new ReplyDoubtRequest("Noted.", null, false));

        Assert.Equal(HttpStatusCode.Forbidden, (await fatima.DeleteRaw($"doubts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await priya.DeleteRaw($"doubts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.OtherAdmin)).DeleteRaw($"doubts/{id}")).StatusCode);
        var admin = await api.LoginAsync(Accounts.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"doubts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"doubts/{id}")).StatusCode);
    }
}
