using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Tuition-centre admission: the admin creates the student with contact details, only the tuition subjects they need,
/// their goal and last school result (with the scanned report) in one step; access to all of it follows the student.
/// </summary>
public sealed class StudentAdmissionTests(ApiFixture api)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n% fictional test report\n%%EOF\n"u8.ToArray();

    private static async Task<FileDto> UploadReport(Client c)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Pdf); content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "term1-report.pdf"); form.Add(new StringContent("SchoolReport"), "purpose");
        var r = await c.Http.PostAsync("/api/v1/files", form);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<FileDto>(ApiFixture.Json))!;
    }

    private static CreateStudentRequest Admission(string tag, List<Guid> subjects, SaveSchoolReportRequest? report) => new(
        "Hafiz Admission (Demo Student)", $"admit-{tag}@horizon.example.test", $"AD-{tag}"[..12], null, "Fictional Public School", "Asia/Dubai", null, null,
        "+971 50 111 2222", "Fictional Parent", "+971 50 333 4444", new DateOnly(2026, 9, 1), subjects, "Wants to reach 85% in Maths by the final exam.", report);

    [Fact]
    public async Task Admin_admits_a_student_with_only_their_tuition_subjects_goal_and_school_report()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        // The centre's simple subject list: adding a name twice returns the same subject.
        var maths = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest("Maths"))).Id;
        var arabic = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest("Arabic"))).Id;
        Assert.Equal(maths, (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest("Maths"))).Id);
        Assert.True((await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Id == maths).IsCentreSubject);

        var file = await UploadReport(admin);
        var tag = Guid.NewGuid().ToString("N");
        var created = await admin.Post<CreatedPersonResponse>("students", Admission(tag, [maths, arabic, maths],
            new SaveSchoolReportRequest("Term 1 2026", new DateOnly(2026, 9, 20), 72.5m, "Weak in algebra.",
                [new("Mathematics", 58, 100, "C"), new("Arabic", 41, 50)], file.Id)));
        var id = created.Id;

        // Only the subjects they need tuition for, no course core subjects.
        var subjects = await admin.Get<StudentSubjectsDto>($"students/{id}/subjects");
        Assert.Empty(subjects.Core);
        Assert.Equal(new[] { maths, arabic }.Order(), subjects.Additional.Select(s => s.Id).Order());

        var profile = await admin.Get<StudentProfileDto>($"students/{id}");
        Assert.Equal(("+971 50 111 2222", "Fictional Parent", "+971 50 333 4444", new DateOnly(2026, 9, 1)),
            (profile.Student.Phone, profile.Student.GuardianName, profile.Student.GuardianPhone, profile.Student.JoinedOn));
        Assert.Contains(await admin.Get<List<StudentNoteDto>>($"students/{id}/notes"), n => n.Text.StartsWith("Wants to reach 85%"));

        var report = Assert.Single(await admin.Get<List<SchoolReportDto>>($"students/{id}/school-reports"));
        Assert.Equal((72.5m, "term1-report.pdf"), (report.OverallPercent, report.File!.FileName));
        Assert.Equal(["Mathematics", "Arabic"], report.Marks.Select(m => m.Subject));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetRaw($"files/{file.Id}/link")).StatusCode);

        // A teacher who now teaches the student sees the report and the file, but never the phone numbers.
        // (Teachers book only their own subjects, so the admin first adds Maths to Amira's profile.)
        var amira = await api.LoginAsync(Accounts.Amira);
        await admin.Post($"academics/subjects/{maths}/teachers", new AssignTeacherRequest(amira.UserId));
        Assert.Equal(HttpStatusCode.NotFound, (await amira.GetRaw($"students/{id}/school-reports")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await amira.GetRaw($"files/{file.Id}/link")).StatusCode);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(400 + Random.Shared.Next(200)));
        await admin.Post<IdResponse>($"students/{id}/packages", new SavePackageRequest(10, 5000)); // classes must fit the hours added
        await amira.Post<CreateSessionResponse>("sessions", new CreateSessionRequest("Maths · Hafiz", SessionType.OneToOne, null, null, null, null, maths, null, [id],
            "Asia/Dubai", day, new TimeOnly(3, 15), 45, DeliveryMode.InPerson, null, null, null, null));
        Assert.Single(await amira.Get<List<SchoolReportDto>>($"students/{id}/school-reports"));
        Assert.Equal(HttpStatusCode.OK, (await amira.GetRaw($"files/{file.Id}/link")).StatusCode);
        var asTeacher = await amira.Get<StudentProfileDto>($"students/{id}");
        Assert.Equal((null, null, null), (asTeacher.Student.Phone, asTeacher.Student.GuardianName, asTeacher.Student.GuardianPhone));
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.PostRaw($"students/{id}/school-reports", new SaveSchoolReportRequest("Teacher try"))).StatusCode);

        // Other students and other institutions cannot reach it.
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Ben)).GetRaw($"students/{id}/school-reports")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.OtherAdmin)).GetRaw($"students/{id}/school-reports")).StatusCode);

        // Removing the report hides its file too.
        await admin.Http.DeleteAsync($"/api/v1/students/{id}/school-reports/{report.Id}");
        Assert.Empty(await admin.Get<List<SchoolReportDto>>($"students/{id}/school-reports"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"files/{file.Id}/link")).StatusCode);
    }

    [Fact]
    public async Task A_bad_admission_creates_nothing_and_only_admins_admit()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var maths = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest("Maths"))).Id;
        var tag = Guid.NewGuid().ToString("N");

        // Invalid parts of the admission roll the whole student back.
        foreach (var bad in new[]
        {
            Admission(tag, [maths], new SaveSchoolReportRequest("Term 1", null, 120m)),                       // percentage over 100
            Admission(tag, [maths], new SaveSchoolReportRequest("Term 1", null, null, null, [new("Maths", 120, 100)])), // more marks than the maximum
            Admission(tag, [Guid.NewGuid()], null),                                                             // unknown subject
            Admission(tag, [maths], null) with { GuardianPhone = "ring the office" },
            Admission(tag, [maths], new SaveSchoolReportRequest("Term 1", null, null, null, null, Guid.NewGuid())), // file not uploaded by the admin
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("students", bad)).StatusCode);
            Assert.Empty((await admin.Get<PagedResult<StudentDto>>($"students?search={Uri.EscapeDataString(bad.Email)}")).Items);
        }

        var amira = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.PostRaw("students", Admission(tag, [maths], null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.PostRaw("academics/centre-subjects", new CentreSubjectRequest("Physics"))).StatusCode);
    }
}
