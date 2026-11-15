using System.Net;
using StudentSync.Contracts;
using StudentSync.Domain.People;

namespace StudentSync.IntegrationTests;

/// <summary>Teacher profile: subjects taught, qualifications and contact details, set when the admin adds or edits a teacher.</summary>
public sealed class TeacherProfileTests(ApiFixture api)
{
    private static async Task<TeacherDto> Find(Client c, string email) =>
        (await c.Get<PagedResult<TeacherDto>>($"teachers?pageSize=100&search={Uri.EscapeDataString(email)}")).Items.Single(t => t.Email == email);

    [Fact]
    public async Task Admin_adds_a_teacher_with_subjects_qualifications_and_details()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var subjects = (await admin.Get<List<SubjectDto>>("academics/subjects")).ToDictionary(s => s.Code);
        var email = $"profile-{Guid.NewGuid():N}@horizon.example.test";
        await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest("Profile Test (Demo Teacher)", email, null, null, "Patient maths teacher.", "Asia/Dubai",
            [subjects["MATH10"].Id, subjects["C10-L2-ARA"].Id, subjects["MATH10"].Id], "+971 50 123 4567", 6, "English, Malayalam, Arabic", new DateOnly(2025, 6, 1),
            [new("B.Ed", "University of Calicut", 2016), new("MSc Mathematics", null, 2014)]));

        var t = await Find(admin, email);
        Assert.Equal(["C10-L2-ARA", "MATH10"], t.Subjects.Select(s => subjects.Values.Single(x => x.Id == s.Id).Code).Order());
        Assert.Equal(["B.Ed", "MSc Mathematics"], t.Qualifications!.Select(q => q.Title));
        Assert.Equal(("+971 50 123 4567", 6, "English, Malayalam, Arabic", new DateOnly(2025, 6, 1)), (t.Phone, t.ExperienceYears, t.Languages, t.JoinedOn));

        // The new teacher is offered for Maths in "Add classes" straight away (same subject links).
        var maths = (await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == "MATH10");
        Assert.Contains(maths.Teachers, x => x.Id == t.Id);

        // Editing replaces subjects and qualifications; leaving them out keeps them.
        await admin.Put($"teachers/{t.Id}", new UpdateTeacherRequest(t.FullName, null, null, t.Bio, PersonStatus.Active, "Asia/Dubai",
            [subjects["C10-SCI"].Id], t.Phone, 7, t.Languages, t.JoinedOn, [new("B.Ed", "University of Calicut", 2016)]));
        t = await Find(admin, email);
        Assert.Equal(["C10-SCI"], t.Subjects.Select(s => subjects.Values.Single(x => x.Id == s.Id).Code));
        Assert.Equal(["B.Ed"], t.Qualifications!.Select(q => q.Title));
        Assert.Equal(7, t.ExperienceYears);
        await admin.Put($"teachers/{t.Id}", new UpdateTeacherRequest(t.FullName, null, null, t.Bio, PersonStatus.Active, "Asia/Dubai", null, t.Phone, 7, t.Languages, t.JoinedOn, null));
        t = await Find(admin, email);
        Assert.Single(t.Subjects);
        Assert.Single(t.Qualifications!);
    }

    [Fact]
    public async Task Teacher_profile_input_is_validated_and_admin_only()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var amira = await Find(admin, Accounts.Amira);
        UpdateTeacherRequest Update(List<Guid>? subjects = null, string? phone = null, int? years = null, List<TeacherQualificationDto>? quals = null) =>
            new(amira.FullName, amira.EmployeeCode, amira.Specialisations, amira.Bio, PersonStatus.Active, amira.TimeZoneId, subjects, phone, years, amira.Languages, amira.JoinedOn, quals);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{amira.Id}", Update(subjects: [Guid.NewGuid()]))).StatusCode);
        var northbridge = await api.LoginAsync(Accounts.OtherAdmin);
        var otherSubject = (await northbridge.Get<List<SubjectDto>>("academics/subjects")).First().Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{amira.Id}", Update(subjects: [otherSubject]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{amira.Id}", Update(phone: "call me"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{amira.Id}", Update(years: 99))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{amira.Id}", Update(quals: [new(" ")]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{amira.Id}", Update(quals: [new("B.Ed", null, 1800)]))).StatusCode);
        // Rejected requests changed nothing.
        var after = await Find(admin, Accounts.Amira);
        Assert.Equal(amira.Subjects.Select(s => s.Id).Order(), after.Subjects.Select(s => s.Id).Order());

        // Only admins change profiles; other staff never see a teacher's phone number.
        await admin.Put($"teachers/{amira.Id}", Update(subjects: amira.Subjects.Select(s => s.Id).ToList(), phone: "+971 4 000 0000"));
        Assert.Equal("+971 4 000 0000", (await Find(admin, Accounts.Amira)).Phone);
        var daniel = await api.LoginAsync(Accounts.Daniel);
        Assert.Equal(HttpStatusCode.Forbidden, (await daniel.PutRaw($"teachers/{amira.Id}", Update())).StatusCode);
        var asTeacher = await daniel.GetRaw("teachers?pageSize=100");
        if (asTeacher.StatusCode == HttpStatusCode.OK)
            Assert.Null((await daniel.Get<PagedResult<TeacherDto>>("teachers?pageSize=100")).Items.Single(t => t.Email == Accounts.Amira).Phone);
    }
}
