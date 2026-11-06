using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.People;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Seeding;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Student academic profile (CLAUDE.md addendum C): curricula, syllabus and academic year on the student, weak subjects
/// recorded by the student / a parent via the admin / a teacher, staff goals and observations, and who may see what.
/// </summary>
public sealed class LearningProfileTests(ApiFixture api)
{
    private const string Meera = "meera@horizon.example.test"; // language teacher: does not teach Fatima
    private const string ClassTenCurriculumCode = "NCERT";

    private static async Task<Guid> SubjectIdAsync(Client c, string code) => (await c.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == code).Id;

    [Fact]
    public async Task Admin_manages_curricula_and_records_syllabus_and_year_on_the_student()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var curricula = await admin.Get<List<CurriculumDto>>("academics/curricula");
        Assert.Contains(curricula, c => c.Code == ClassTenCurriculumCode);
        Assert.Contains(curricula, c => c.Code == "KERALA");

        var code = $"T{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var created = await admin.Post<IdResponse>("academics/curricula", new SaveCurriculumRequest(code.ToLowerInvariant(), $"Test board {code}", CatalogStatus.Active));
        Assert.Equal(code, (await admin.Get<List<CurriculumDto>>("academics/curricula")).Single(c => c.Id == created.Id).Code); // codes are upper-cased
        var dup = await admin.PostRaw("academics/curricula", new SaveCurriculumRequest(code, "Another name", CatalogStatus.Active));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).PostRaw("academics/curricula", new SaveCurriculumRequest("X1", "Teachers cannot", CatalogStatus.Active))).StatusCode);

        var year = (await admin.Get<List<AcademicYearDto>>("academics/years")).First();
        var student = await admin.Post<CreatedPersonResponse>("students",
            new CreateStudentRequest("Profile Test (Demo Student)", $"profile{Guid.NewGuid():N}@horizon.example.test", $"P-{Guid.NewGuid():N}"[..12], null, "Example School", "Asia/Kolkata", created.Id, year.Id));
        var profile = await admin.Get<StudentProfileDto>($"students/{student.Id}");
        Assert.Equal((created.Id, $"Test board {code}", year.Id, year.Name), (profile.Student.CurriculumId, profile.Student.CurriculumName, profile.Student.AcademicYearId, profile.Student.AcademicYearName));

        // Unknown or other-institution ids are plain validation errors.
        var bad = await admin.PutRaw($"students/{student.Id}", new UpdateStudentRequest("Profile Test (Demo Student)", profile.Student.StudentCode, null, null, PersonStatus.Active, "Asia/Kolkata", Guid.NewGuid(), year.Id));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("curriculumId", await bad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Student_reports_her_own_weak_subjects_and_only_from_her_own_subjects()
    {
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var plan = await aisha.Get<LearningPlanDto>("my/learning-plan");
        Assert.Equal("CBSE / NCERT", plan.CurriculumName);
        Assert.Contains(plan.Subjects, s => s.Detail == "C10-SST");
        Assert.DoesNotContain(plan.Subjects, s => s.Detail == "CHEM10");
        Assert.Contains(plan.Needs, n => n.Source == LearningNeedSource.Parent);              // seeded, recorded by the admin for her mother
        Assert.All(plan.Goals, g => Assert.Equal(StudentNoteKind.Goal, g.Kind));             // never staff observations

        var sst = plan.Subjects.Single(s => s.Detail == "C10-SST").Id;
        var before = plan.Needs.Count;
        var added = await aisha.Post<LearningNeedDto>("my/learning-needs", new AddLearningNeedRequest(sst, null, LearningNeedSource.Teacher, "History dates (test)"));
        Assert.Equal(LearningNeedSource.Student, added.Source);                              // the requested source is ignored for students
        Assert.Equal(HttpStatusCode.BadRequest, (await aisha.PostRaw("my/learning-needs", new AddLearningNeedRequest(sst, null, null, null))).StatusCode); // duplicate
        var admin = await api.LoginAsync(Accounts.Admin);
        var chem = await SubjectIdAsync(admin, "CHEM10");
        Assert.Equal(HttpStatusCode.BadRequest, (await aisha.PostRaw("my/learning-needs", new AddLearningNeedRequest(chem, null, null, null))).StatusCode); // not her subject

        // A chapter of the subject narrows the need; a chapter of another subject is refused.
        var syllabus = await aisha.Get<SubjectSyllabusDto>($"my/subjects/{sst}");
        var chapter = syllabus.Chapters.First(c => c.ChapterId is not null).ChapterId!.Value;
        var narrowed = await aisha.Post<LearningNeedDto>("my/learning-needs", new AddLearningNeedRequest(sst, chapter, null, null));
        Assert.Equal(chapter, narrowed.ChapterId);
        var maths = plan.Subjects.Single(s => s.Detail == "MATH10").Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await aisha.PostRaw("my/learning-needs", new AddLearningNeedRequest(maths, chapter, null, null))).StatusCode);

        plan = await aisha.Get<LearningPlanDto>("my/learning-plan");
        Assert.Equal(before + 2, plan.Needs.Count);

        // She may withdraw what she said, not what her parent said.
        var parentNeed = plan.Needs.First(n => n.Source == LearningNeedSource.Parent);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.Http.DeleteAsync($"/api/v1/my/learning-needs/{parentNeed.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await aisha.Http.DeleteAsync($"/api/v1/my/learning-needs/{added.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await aisha.Http.DeleteAsync($"/api/v1/my/learning-needs/{narrowed.Id}")).StatusCode);
        Assert.Equal(before, (await aisha.Get<LearningPlanDto>("my/learning-plan")).Needs.Count);

        // Students cannot use the staff endpoints, even for themselves.
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.PostRaw($"students/{aisha.UserId}/learning-needs", new AddLearningNeedRequest(sst, null, LearningNeedSource.Parent, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.PostRaw($"students/{aisha.UserId}/notes", new AddStudentNoteRequest(StudentNoteKind.Goal, "x", null))).StatusCode);
    }

    [Fact]
    public async Task Staff_record_and_resolve_needs_and_notes_only_for_students_they_may_see()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var priya = await api.LoginAsync(Accounts.Priya);
        var meera = await api.LoginAsync(Meera);
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        var chem = await SubjectIdAsync(admin, "CHEM10");
        var mathsSubject = (await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == "MATH10");
        var maths = mathsSubject.Id;

        // A brand-new student: no teacher teaches them yet, so every teacher gets 404 (the record is not revealed).
        var created = await admin.Post<CreatedPersonResponse>("students",
            new CreateStudentRequest("Needs Test (Demo Student)", $"needs{Guid.NewGuid():N}@horizon.example.test", $"N-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"));
        var studentId = created.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await priya.GetRaw($"students/{studentId}/learning-needs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await priya.PostRaw($"students/{studentId}/learning-needs", new AddLearningNeedRequest(maths, null, null, null))).StatusCode);

        // Once Priya teaches them one-to-one she may record needs; Meera still may not; another institution never.
        var priyaId = (await admin.Get<PagedResult<TeacherDto>>("teachers?search=priya&pageSize=5")).Items.Single(t => t.Email == Accounts.Priya).Id;
        await admin.Post<IdResponse>("academics/arrangements", new SaveArrangementRequest(ArrangementType.OneToOne, "Chemistry 1:1 (test)", priyaId, null, chem, DateOnly.FromDateTime(DateTime.UtcNow), null, [studentId], CatalogStatus.Active));
        Assert.Empty(await priya.Get<List<LearningNeedDto>>($"students/{studentId}/learning-needs"));
        Assert.Equal(HttpStatusCode.NotFound, (await meera.GetRaw($"students/{studentId}/learning-needs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetRaw($"students/{studentId}/learning-needs")).StatusCode);

        var byTeacher = await priya.Post<LearningNeedDto>($"students/{studentId}/learning-needs", new AddLearningNeedRequest(maths, null, null, "Quadratics (test)"));
        Assert.Equal(LearningNeedSource.Teacher, byTeacher.Source);
        var byAdminForParent = await admin.Post<LearningNeedDto>($"students/{studentId}/learning-needs", new AddLearningNeedRequest(maths, mathsSubject.Chapters.First().Id, LearningNeedSource.Parent, "Father mentioned it (test)"));
        Assert.Equal(LearningNeedSource.Parent, byAdminForParent.Source);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{studentId}/learning-needs", new AddLearningNeedRequest(maths, null, LearningNeedSource.Diagnostic, null))).StatusCode);

        // The student sees both as open; the teacher resolves one with a reason; it leaves the open list but stays in the history.
        var profile = await admin.Get<StudentProfileDto>($"students/{studentId}");
        var student = await api.LoginAsync(profile.Student.Email, created.TemporaryPassword);
        await student.Post("me/password", new ChangePasswordRequest(created.TemporaryPassword, "NeedsTestPass123"));
        student = await api.LoginAsync(profile.Student.Email, "NeedsTestPass123");
        Assert.Equal(2, (await student.Get<LearningPlanDto>("my/learning-plan")).Needs.Count);
        await priya.Post($"students/{studentId}/learning-needs/{byTeacher.Id}/resolve", new ResolveLearningNeedRequest("Scored 90% on the chapter test (test)"));
        Assert.DoesNotContain((await student.Get<LearningPlanDto>("my/learning-plan")).Needs, n => n.Id == byTeacher.Id);
        Assert.DoesNotContain(await priya.Get<List<LearningNeedDto>>($"students/{studentId}/learning-needs"), n => n.Id == byTeacher.Id);
        var resolved = (await priya.Get<List<LearningNeedDto>>($"students/{studentId}/learning-needs?includeResolved=true")).Single(n => n.Id == byTeacher.Id);
        Assert.Equal((LearningNeedStatus.Resolved, "Scored 90% on the chapter test (test)"), (resolved.Status, resolved.ReviewNote));
        Assert.StartsWith("Priya", resolved.ResolvedBy);
        Assert.Equal(HttpStatusCode.NotFound, (await priya.PostRaw($"students/{studentId}/learning-needs/{byTeacher.Id}/resolve", new ResolveLearningNeedRequest(null))).StatusCode); // already resolved
        // The student cannot withdraw what the parent reported.
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Http.DeleteAsync($"/api/v1/my/learning-needs/{byAdminForParent.Id}")).StatusCode);

        // Goals reach the student; observations stay with staff; only the author or an admin deletes.
        var goal = await priya.Post<StudentNoteDto>($"students/{studentId}/notes", new AddStudentNoteRequest(StudentNoteKind.Goal, "Finish the mole-concept worksheet by Friday (test)", chem));
        var observation = await priya.Post<StudentNoteDto>($"students/{studentId}/notes", new AddStudentNoteRequest(StudentNoteKind.Observation, "Shy about asking in group classes (test)", null));
        var studentView = (await student.Get<LearningPlanDto>("my/learning-plan")).Goals;
        Assert.Contains(studentView, g => g.Id == goal.Id);
        Assert.DoesNotContain(studentView, g => g.Id == observation.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await meera.GetRaw($"students/{studentId}/notes")).StatusCode);
        Assert.Contains(await admin.Get<List<StudentNoteDto>>($"students/{studentId}/notes"), n => n.Id == observation.Id && n.CanDelete);
        Assert.Equal(HttpStatusCode.NoContent, (await priya.Http.DeleteAsync($"/api/v1/students/{studentId}/notes/{observation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync($"/api/v1/students/{studentId}/notes/{goal.Id}")).StatusCode);

        // Nothing another student can reach.
        var aisha = await api.LoginAsync(Accounts.Aisha);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.GetRaw($"students/{studentId}/learning-needs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.GetRaw($"students/{studentId}/notes")).StatusCode);

        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "student.learning_need_resolved" && a.EntityId == studentId.ToString()));
    }

    [Fact]
    public async Task Demo_seeder_learning_profiles_are_idempotent()
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var needs = await db.LearningNeeds.IgnoreQueryFilters().CountAsync();
        var curricula = await db.Curricula.IgnoreQueryFilters().CountAsync();
        Assert.True(needs >= 3 && curricula >= 2);
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        Assert.False(await seeder.SeedAsync(new DemoOptions { Password = ApiFixture.Password, ForcePasswordChange = false }, CancellationToken.None));
        Assert.Equal(needs, await db.LearningNeeds.IgnoreQueryFilters().CountAsync());
        Assert.Equal(curricula, await db.Curricula.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Students.IgnoreQueryFilters().CountAsync(s => s.Status == PersonStatus.Suspended));
    }
}
