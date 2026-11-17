using System.Net;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner request 2026-11-17: syllabi, classes, courses, subjects and academic years can be edited and removed, and codes
/// are assigned automatically. Only unused entries can be removed (the admin is told what still uses one); editing keeps
/// codes and links; only admins can change the catalogue.
/// </summary>
public sealed class CatalogueEditTests(ApiFixture api)
{
    [Fact]
    public async Task New_entries_get_automatic_codes_and_editing_keeps_them()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Guid.NewGuid().ToString("N")[..5];
        var curriculumId = (await admin.Post<IdResponse>("academics/curricula", new SaveCurriculumRequest("", $"Gulf Board {tag}", CatalogStatus.Active))).Id;
        var curriculum = (await admin.Get<List<CurriculumDto>>("academics/curricula")).Single(c => c.Id == curriculumId);
        Assert.StartsWith("GB", curriculum.Code);

        var gradeId = (await admin.Post<IdResponse>("academics/grades", new SaveGradeLevelRequest($"Level {tag}", 0))).Id;
        var grade = (await admin.Get<List<GradeLevelDto>>("academics/grades")).Single(g => g.Id == gradeId);
        Assert.True(grade.SortOrder > 0, "order is assigned automatically");

        var courseId = (await admin.Post<IdResponse>("academics/courses", new SaveCourseRequest("", $"Level {tag} · Gulf", null, gradeId, null, CatalogStatus.Active, curriculumId))).Id;
        var course = (await admin.Get<List<CourseDto>>("academics/courses")).Single(c => c.Id == courseId);
        Assert.StartsWith($"{curriculum.Code}-C", course.Code);

        var subjectId = (await admin.Post<IdResponse>("academics/subjects", new SaveSubjectRequest(courseId, "", "Mathematics", null, CatalogStatus.Active))).Id;
        var subject = (await admin.Get<List<SubjectDto>>($"academics/subjects?courseId={courseId}")).Single(s => s.Id == subjectId);
        Assert.Equal($"{course.Code}-MATH", subject.Code);

        // Editing (the screen sends the existing code, or none) keeps the code.
        await admin.Put($"academics/subjects/{subjectId}", new SaveSubjectRequest(courseId, "", "Maths", null, CatalogStatus.Active));
        var edited = (await admin.Get<List<SubjectDto>>($"academics/subjects?courseId={courseId}")).Single(s => s.Id == subjectId);
        Assert.Equal(("Maths", subject.Code), (edited.Name, edited.Code));
        await admin.Put($"academics/curricula/{curriculumId}", new SaveCurriculumRequest(curriculum.Code, $"Gulf Board {tag} (renamed)", CatalogStatus.Active));
        Assert.Equal(curriculum.Code, (await admin.Get<List<CurriculumDto>>("academics/curricula")).Single(c => c.Id == curriculumId).Code);
    }

    [Fact]
    public async Task Unused_entries_can_be_removed_and_used_ones_explain_why_not()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Guid.NewGuid().ToString("N")[..5];
        var curriculumId = (await admin.Post<IdResponse>("academics/curricula", new SaveCurriculumRequest("", $"Remove Board {tag}", CatalogStatus.Active))).Id;
        var gradeId = (await admin.Post<IdResponse>("academics/grades", new SaveGradeLevelRequest($"Remove Level {tag}", 0))).Id;
        var courseId = (await admin.Post<IdResponse>("academics/courses", new SaveCourseRequest("", $"Remove course {tag}", null, gradeId, null, CatalogStatus.Active, curriculumId))).Id;
        var used = (await admin.Post<IdResponse>("academics/subjects", new SaveSubjectRequest(courseId, "", "Used subject", null, CatalogStatus.Active))).Id;
        var unused = (await admin.Post<IdResponse>("academics/subjects", new SaveSubjectRequest(courseId, "", "Unused subject", null, CatalogStatus.Active))).Id;
        await admin.Post<IdResponse>($"academics/subjects/{unused}/chapters", new SaveChapterRequest("Chapter 1", 1));

        // A class uses one subject: it (and the course, the class and the syllabus) cannot go.
        var amira = await api.LoginAsync(Accounts.Amira);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var session = TenantIsolationTests.NewSession(SessionType.OneToOne, amira.UserId, [aisha.UserId],
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(200 + Random.Shared.Next(100))), time: new TimeOnly(1, 5 * Random.Shared.Next(0, 12))) with
        { SubjectId = used };
        await admin.Post<CreateSessionResponse>("sessions", session);
        var refused = await admin.DeleteRaw($"academics/subjects/{used}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Still used by 1 class", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteRaw($"academics/courses/{courseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteRaw($"academics/grades/{gradeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteRaw($"academics/curricula/{curriculumId}")).StatusCode);

        // The unused subject goes, with its empty chapter; a teacher cannot remove anything.
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.DeleteRaw($"academics/subjects/{unused}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/subjects/{unused}")).StatusCode);
        Assert.DoesNotContain(await admin.Get<List<SubjectDto>>($"academics/subjects?courseId={courseId}"), s => s.Id == unused);

        // A fresh, unused course (with a subject) and an unused class, syllabus and year can be removed.
        var spareCurriculum = (await admin.Post<IdResponse>("academics/curricula", new SaveCurriculumRequest("", $"Spare Board {tag}", CatalogStatus.Active))).Id;
        var spareGrade = (await admin.Post<IdResponse>("academics/grades", new SaveGradeLevelRequest($"Spare Level {tag}", 0))).Id;
        var spareCourse = (await admin.Post<IdResponse>("academics/courses", new SaveCourseRequest("", $"Spare course {tag}", null, spareGrade, null, CatalogStatus.Active, spareCurriculum))).Id;
        await admin.Post<IdResponse>("academics/subjects", new SaveSubjectRequest(spareCourse, "", "Spare subject", null, CatalogStatus.Active));
        var spareYear = (await admin.Post<IdResponse>("academics/years", new SaveAcademicYearRequest($"Spare {tag}", new DateOnly(2040, 4, 1), new DateOnly(2041, 3, 31), false))).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/courses/{spareCourse}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/grades/{spareGrade}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/curricula/{spareCurriculum}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"academics/years/{spareYear}")).StatusCode);
        Assert.DoesNotContain(await admin.Get<List<CourseDto>>("academics/courses"), c => c.Id == spareCourse);

        // Another institute cannot touch these.
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.OtherAdmin)).DeleteRaw($"academics/courses/{courseId}")).StatusCode);
    }
}
