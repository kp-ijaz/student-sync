using System.Net;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Owner request 2026-11-17: admins see how many active students there are per class, per syllabus and per academic
/// year (Reports ▸ Students by class). Only admins; another institute's students never count.
/// </summary>
public sealed class StudentNumbersTests(ApiFixture api)
{
    [Fact]
    public async Task Counts_students_by_class_syllabus_and_year_for_admins_only()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var gradeId = (await admin.Post<IdResponse>("academics/grades", new SaveGradeLevelRequest($"Count Level {tag}", 0))).Id;
        var curriculumId = (await admin.Post<IdResponse>("academics/curricula", new SaveCurriculumRequest("", $"Count Board {tag}", CatalogStatus.Active))).Id;
        var yearId = (await admin.Post<IdResponse>("academics/years", new SaveAcademicYearRequest($"Count {tag}", new DateOnly(2050, 4, 1), new DateOnly(2051, 3, 31), false))).Id;
        for (var i = 0; i < 2; i++)
            await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Count {tag} {i} (Demo Student)", $"count-{tag}-{i}@horizon.example.test",
                $"CN-{tag}-{i}", gradeId, null, "Asia/Dubai", curriculumId, yearId));

        // Filtered to the new year: exactly the two students, in the one class × syllabus cell.
        var year = await admin.Get<StudentsBreakdownDto>($"reports/students?academicYearId={yearId}");
        Assert.Equal(2, year.Total);
        Assert.Equal(2, year.Classes.Single(c => c.Id == gradeId).Count);
        Assert.Equal(2, year.Syllabi.Single(c => c.Id == curriculumId).Count);
        Assert.Equal(2, Assert.Single(year.Cells).Count);
        Assert.Equal((gradeId, curriculumId), (year.Cells[0].ClassId, year.Cells[0].SyllabusId));

        // Unfiltered: the year list always counts every student; the new class and syllabus hold just these two.
        var all = await admin.Get<StudentsBreakdownDto>("reports/students");
        Assert.Equal(2, all.Years.Single(y => y.Id == yearId).Count);
        Assert.Equal(2, all.Classes.Single(c => c.Id == gradeId).Count);
        Assert.Equal(all.Total, all.Cells.Sum(c => c.Count));
        Assert.Equal(all.Total, all.Years.Sum(y => y.Count));

        // Teachers and students cannot see it; another institute sees none of these.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).GetRaw("reports/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Aisha)).GetRaw("reports/students")).StatusCode);
        var other = await (await api.LoginAsync(Accounts.OtherAdmin)).Get<StudentsBreakdownDto>($"reports/students?academicYearId={yearId}");
        Assert.Equal(0, other.Total);
        Assert.DoesNotContain(other.Classes, c => c.Id == gradeId);
    }
}
