using System.Net;
using StudentSync.Application.Academics;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner request 2026-11-16 ("adding courses should be very easy"): choose a class and a syllabus, the usual subjects are
/// suggested, one save creates the class course and its subjects. Saving again only adds what is missing.
/// </summary>
public sealed class QuickClassTests(ApiFixture api)
{
    [Fact]
    public async Task Add_a_class_suggests_subjects_creates_the_course_once_and_adds_only_missing_subjects()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        // A fresh syllabus whose name says ICSE, so the ICSE subject list is suggested and no course exists yet.
        var curriculumId = (await admin.Post<IdResponse>("academics/curricula", new SaveCurriculumRequest($"QT{tag}", $"ICSE quick test {tag}", CatalogStatus.Active))).Id;
        var grades = await admin.Get<List<GradeLevelDto>>("academics/grades");
        var grade = grades.FirstOrDefault(g => DefaultCatalogue.ClassNumber(g.Name) == 7);
        var gradeId = grade?.Id ?? (await admin.Post<IdResponse>("academics/grades", new SaveGradeLevelRequest("Class 7", 7))).Id;

        var suggestion = await admin.Get<QuickClassSuggestionDto>($"academics/catalogue/suggest?gradeLevelId={gradeId}&curriculumId={curriculumId}");
        Assert.Null(suggestion.CourseId);
        Assert.NotEmpty(suggestion.Suggested);
        Assert.Empty(suggestion.Existing);

        var first = suggestion.Suggested.Take(2).Append("Robotics").ToList();
        var courseId = (await admin.Post<IdResponse>("academics/catalogue/classes", new QuickClassRequest(gradeId, curriculumId, first))).Id;

        // Saving again with a repeat (different case) and one new subject reuses the course and adds just the new one.
        var again = (await admin.Post<IdResponse>("academics/catalogue/classes", new QuickClassRequest(gradeId, curriculumId, [first[0], "robotics", "Abacus"]))).Id;
        Assert.Equal(courseId, again);
        var subjects = (await admin.Get<List<SubjectDto>>("academics/subjects")).Where(s => s.CourseId == courseId).ToList();
        Assert.Equal(4, subjects.Count);
        Assert.Equal(subjects.Count, subjects.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var after = await admin.Get<QuickClassSuggestionDto>($"academics/catalogue/suggest?gradeLevelId={gradeId}&curriculumId={curriculumId}");
        Assert.Equal(courseId, after.CourseId);
        Assert.Equal(4, after.Existing.Count);
        var course = Assert.Single(await admin.Get<List<CourseDto>>("academics/courses"), c => c.Id == courseId);
        Assert.Equal(curriculumId, course.CurriculumId);
        Assert.Equal(gradeId, course.GradeLevelId);

        // Bad input is a validation error, not a half-made class.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("academics/catalogue/classes", new QuickClassRequest(gradeId, curriculumId, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("academics/catalogue/classes", new QuickClassRequest(gradeId, curriculumId, [new string('x', 101)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("academics/catalogue/classes", new QuickClassRequest(Guid.NewGuid(), curriculumId, ["Maths"]))).StatusCode);
    }

    [Fact]
    public async Task Only_admins_of_the_same_institute_can_add_a_class()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var grade = (await admin.Get<List<GradeLevelDto>>("academics/grades")).First();
        var curriculum = (await admin.Get<List<CurriculumDto>>("academics/curricula")).First();
        var body = new QuickClassRequest(grade.Id, curriculum.Id, ["Mathematics"]);
        var suggest = $"academics/catalogue/suggest?gradeLevelId={grade.Id}&curriculumId={curriculum.Id}";

        foreach (var who in new[] { Accounts.Amira, Accounts.Aisha })
        {
            var client = await api.LoginAsync(who);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetRaw(suggest)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostRaw("academics/catalogue/classes", body)).StatusCode);
        }
        // Another institute's admin cannot use this institute's class or syllabus.
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.GetRaw(suggest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostRaw("academics/catalogue/classes", body)).StatusCode);
    }
}
