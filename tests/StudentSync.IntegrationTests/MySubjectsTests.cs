using System.Net;
using System.Net.Http.Json;
using StudentSync.Contracts;
using StudentSync.Domain.Content;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>GET /my/subjects and /my/subjects/{id}: the student learning path (subject → syllabus → classes and videos).</summary>
public sealed class MySubjectsTests(ApiFixture api)
{
    private const string Chloe = "chloe@horizon.example.test";     // only in the completed physics private group
    private const string NorthbridgeStudent = "student@northbridge.example.test";

    // One sign-in per account for the whole class. Concurrent sign-ins of the same account from other test classes can hit a
    // pre-existing 409 (the login updates the user row), so fewer sign-ins keeps this class stable; a 409 is retried once.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<Client>>> Sessions = new();

    private Task<Client> SignIn(string email) => Sessions.GetOrAdd(email, e => new Lazy<Task<Client>>(async () =>
    {
        var probe = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(e, ApiFixture.Password, null, "tests"), ApiFixture.Json);
        if (probe.StatusCode == HttpStatusCode.Conflict) await Task.Delay(250);
        else if (probe.IsSuccessStatusCode)
        {
            var auth = (await probe.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
            var http = api.Factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
            return new Client(http, auth);
        }
        return await api.LoginAsync(e);
    })).Value;

    private static async Task<MySubjectDto> Subject(Client c, string code) => (await c.Get<List<MySubjectDto>>("my/subjects")).Single(s => s.Code == code);

    private static readonly string[] NcertClass10Maths =
    [
        "Real Numbers", "Polynomials", "Pair of Linear Equations in Two Variables", "Quadratic Equations", "Arithmetic Progressions",
        "Triangles", "Coordinate Geometry", "Introduction to Trigonometry", "Some Applications of Trigonometry", "Circles",
        "Areas Related to Circles", "Surface Areas and Volumes", "Statistics", "Probability",
    ];

    [Fact]
    public async Task Batch_student_sees_her_subjects_with_the_whole_syllabus_in_order()
    {
        var aisha = await SignIn(Accounts.Aisha);
        var subjects = await aisha.Get<List<MySubjectDto>>("my/subjects");

        // Class 10 core subjects, her two languages, and the physics coaching she has classes in; ordered core → language → additional, then name.
        Assert.Equal(["C10-ENG", "MATH10", "C10-SCI", "C10-SST", "C10-L3-FRE", "C10-L2-HIN", "PHYS10"], subjects.Select(s => s.Code));
        Assert.Equal(["core", "core", "core", "core", "language", "language", "additional"], subjects.Select(s => s.Kind));

        var maths = subjects.Single(s => s.Code == "MATH10");
        Assert.Equal("Mathematics", maths.Name);
        Assert.Equal("Class 10 (CBSE)", maths.CourseName);
        Assert.Equal(14, maths.ChapterCount);
        Assert.True(maths.ClassCount > maths.CompletedClassCount && maths.CompletedClassCount > 0);
        Assert.NotNull(maths.NextSession);
        Assert.True(maths.NextSession!.EndUtc > DateTime.UtcNow);
        Assert.Contains(maths.NextSession.Status, new[] { SessionStatus.Scheduled, SessionStatus.Live });
        var amira = Assert.Single(maths.Teachers);
        Assert.StartsWith("Amira Haddad", amira.Name);
        Assert.NotNull(amira.PhotoUrl);
        Assert.NotNull(maths.NextSession.TeacherPhotoUrl);

        var syllabus = await aisha.Get<SubjectSyllabusDto>($"my/subjects/{maths.SubjectId}");
        Assert.Equal("core", syllabus.Kind);
        Assert.Equal(NcertClass10Maths, syllabus.Chapters.Select(c => c.Title));
        Assert.Equal(Enumerable.Range(1, 14), syllabus.Chapters.Select(c => c.SortOrder));
        Assert.All(syllabus.Chapters, c => Assert.NotNull(c.ChapterId)); // every demo class has a chapter: no "Other classes"

        // The timetable has not reached the last chapter yet.
        var probability = syllabus.Chapters[^1];
        Assert.Equal("notScheduled", probability.Status);
        Assert.Empty(probability.Sessions);
        Assert.Empty(probability.Videos);

        // The evening batch works on quadratic equations: past, cancelled and future classes.
        var quadratics = syllabus.Chapters.Single(c => c.Title == "Quadratic Equations");
        Assert.Equal("inProgress", quadratics.Status);
        Assert.Equal(quadratics.Sessions.OrderBy(s => s.StartUtc).Select(s => s.Id), quadratics.Sessions.Select(s => s.Id));
        Assert.All(quadratics.Sessions, s => Assert.Equal(SessionType.Batch, s.SessionType));
        Assert.Contains(quadratics.Sessions, s => s.Status == SessionStatus.Cancelled); // cancelled classes are listed with their status
        Assert.Contains(syllabus.Chapters.SelectMany(c => c.Sessions), s => s.Id == syllabus.NextSession!.Id);

        Assert.Equal(syllabus.ClassCount, syllabus.Chapters.Sum(c => c.Sessions.Count(s => s.Status != SessionStatus.Cancelled)));
        Assert.Equal(syllabus.Chapters.Count(c => c.Status == "completed"), syllabus.CompletedChapterCount);
        Assert.Equal(14, syllabus.ChapterCount);
    }

    [Fact]
    public async Task Chapter_status_is_completed_for_past_classes_and_upcoming_for_future_ones()
    {
        // Chloe only attended last week's physics private group (Motion): the chapter is done and nothing is next.
        var chloe = await SignIn(Chloe);
        var physics = await Subject(chloe, "PHYS10");
        Assert.Null(physics.NextSession);
        Assert.Equal(1, physics.CompletedChapterCount);
        Assert.Equal(physics.ClassCount, physics.CompletedClassCount);
        var syllabus = await chloe.Get<SubjectSyllabusDto>($"my/subjects/{physics.SubjectId}");
        var motion = syllabus.Chapters.Single(c => c.Title == "Motion");
        Assert.Equal("completed", motion.Status);
        Assert.All(syllabus.Chapters.Where(c => c.Title != "Motion"), c => Assert.Equal("notScheduled", c.Status));

        // Aisha only has the future ad hoc clinic in Motion.
        var aisha = await SignIn(Accounts.Aisha);
        var aishaPhysics = await aisha.Get<SubjectSyllabusDto>($"my/subjects/{physics.SubjectId}");
        var aishaMotion = aishaPhysics.Chapters.Single(c => c.Title == "Motion");
        Assert.Equal("upcoming", aishaMotion.Status);
        Assert.Equal(0, aishaPhysics.CompletedChapterCount);
        Assert.NotNull(aishaPhysics.NextSession);
        // Each student sees only their own classes in the shared chapter.
        Assert.DoesNotContain(aishaMotion.Sessions, s => motion.Sessions.Any(m => m.Id == s.Id));
    }

    [Fact]
    public async Task Chapter_videos_follow_the_same_lock_rules_as_the_videos_endpoint()
    {
        var amira = await SignIn(Accounts.Amira);
        var aisha = await SignIn(Accounts.Aisha);
        var upcoming = (await aisha.Get<List<SessionListItemDto>>($"sessions?status=Scheduled&fromUtc={DateTime.UtcNow.AddDays(1):O}&limit=50"))
            .First(s => s.SessionType == SessionType.Batch && s.TeacherId == amira.UserId);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0, 0, 0, 0x18, .. "ftypisom"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new("video/mp4");
        form.Add(file, "file", "syllabus.mp4");
        form.Add(new StringContent("Syllabus test: worked examples"), "title");
        form.Add(new StringContent("AfterClass"), "unlockRule");
        form.Add(new StringContent("0"), "unlockOffsetMinutes");
        var upload = await amira.Http.PostAsync($"/api/v1/sessions/{upcoming.Id}/videos", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var videoId = (await upload.Content.ReadFromJsonAsync<IdResponse>(ApiFixture.Json))!.Id;

        var maths = await Subject(aisha, "MATH10");
        Assert.True(maths.VideoCount >= 1);
        Assert.True(maths.UnlockedVideoCount < maths.VideoCount);

        // The student sees the locked video in the class's chapter, without a file.
        var studentView = await aisha.Get<SubjectSyllabusDto>($"my/subjects/{maths.SubjectId}");
        var chapter = studentView.Chapters.Single(c => c.Sessions.Any(s => s.Id == upcoming.Id));
        Assert.NotNull(chapter.ChapterId);
        var locked = chapter.Videos.Single(v => v.Id == videoId);
        Assert.False(locked.IsUnlocked);
        Assert.Null(locked.FileId);
        Assert.False(locked.CanManage);
        Assert.Equal(upcoming.EndUtc, locked.UnlocksAtUtc);
        Assert.Equal(locked, (await aisha.Get<List<PrerecordedVideoDto>>($"sessions/{upcoming.Id}/videos")).Single(v => v.Id == videoId));

        // The class teacher manages it, so she gets the file even while it is locked for students.
        var teacherView = (await amira.Get<SubjectSyllabusDto>($"my/subjects/{maths.SubjectId}")).Chapters.SelectMany(c => c.Videos).Single(v => v.Id == videoId);
        Assert.NotNull(teacherView.FileId);
        Assert.True(teacherView.CanManage);

        // Unlocking by hand shows straight away in the syllabus.
        await amira.Post($"videos/{videoId}/access", new SetVideoAccessRequest(VideoAccessOverride.Unlocked));
        var unlocked = (await aisha.Get<SubjectSyllabusDto>($"my/subjects/{maths.SubjectId}")).Chapters.SelectMany(c => c.Videos).Single(v => v.Id == videoId);
        Assert.True(unlocked.IsUnlocked);
        Assert.Equal(teacherView.FileId, unlocked.FileId);
        await amira.Post($"videos/{videoId}/access", new SetVideoAccessRequest(VideoAccessOverride.Locked));
    }

    [Fact]
    public async Task Classes_without_a_chapter_are_grouped_under_other_classes()
    {
        var daniel = await SignIn(Accounts.Daniel);
        var admin = await SignIn(Accounts.Admin);
        var hiroId = (await admin.Get<PagedResult<StudentDto>>("students?search=Hiro")).Items.Single().Id;
        var physicsId = (await Subject(daniel, "PHYS10")).SubjectId;
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(70));

        // Physics coaching is an additional subject: Hiro only sees it once an admin assigns it to him (keeping his languages).
        var hiro = await SignIn("hiro@horizon.example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await hiro.GetRaw($"my/subjects/{physicsId}")).StatusCode);
        var current = await admin.Get<StudentSubjectsDto>($"students/{hiroId}/subjects");
        await admin.Put($"students/{hiroId}/subjects", new SaveStudentSubjectsRequest(
            current.ChoiceGroups.SelectMany(g => g.ChosenSubjectIds.Select(id => new SubjectChoiceRequest(g.Group, id))).ToList(),
            [.. current.Additional.Select(a => a.Id), physicsId]));

        var request = TenantIsolationTests.NewSession(SessionType.Group, null, [hiroId], date, time: new TimeOnly(6, 0)) with { SubjectId = physicsId };
        var sessionId = (await daniel.Post<CreateSessionResponse>("sessions", request)).SessionIds.Single();

        var syllabus = await hiro.Get<SubjectSyllabusDto>($"my/subjects/{physicsId}");
        Assert.Equal("additional", syllabus.Kind);
        var other = syllabus.Chapters[^1];
        Assert.Null(other.ChapterId);
        Assert.Equal("Other classes", other.Title);
        Assert.Equal(int.MaxValue, other.SortOrder);
        Assert.Equal("upcoming", other.Status);
        Assert.Equal(sessionId, Assert.Single(other.Sessions).Id);
        Assert.Equal(5, syllabus.ChapterCount); // the pseudo chapter is not part of the syllabus counts
        Assert.All(syllabus.Chapters.SkipLast(1), c => Assert.Equal("notScheduled", c.Status));
        Assert.Equal(sessionId, syllabus.NextSession?.Id);
    }

    [Fact]
    public async Task Student_gets_404_for_a_subject_that_is_not_theirs()
    {
        var fatima = await SignIn(Accounts.Fatima);
        var chemistry = await Subject(fatima, "CHEM10");
        Assert.Equal(HttpStatusCode.OK, (await fatima.GetRaw($"my/subjects/{chemistry.SubjectId}")).StatusCode);

        var aisha = await SignIn(Accounts.Aisha);
        Assert.DoesNotContain(await aisha.Get<List<MySubjectDto>>("my/subjects"), s => s.SubjectId == chemistry.SubjectId);
        Assert.Equal(HttpStatusCode.NotFound, (await aisha.GetRaw($"my/subjects/{chemistry.SubjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await aisha.GetRaw($"my/subjects/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Other_institutions_never_see_horizon_subjects()
    {
        var aisha = await SignIn(Accounts.Aisha);
        var horizonMaths = await Subject(aisha, "MATH10");

        var isla = await SignIn(NorthbridgeStudent);
        var subjects = await isla.Get<List<MySubjectDto>>("my/subjects");
        var only = Assert.Single(subjects);
        Assert.Equal("NB-MATH", only.Code);
        Assert.Equal(HttpStatusCode.NotFound, (await isla.GetRaw($"my/subjects/{horizonMaths.SubjectId}")).StatusCode);

        var otherAdmin = await SignIn(Accounts.OtherAdmin);
        Assert.DoesNotContain(await otherAdmin.Get<List<MySubjectDto>>("my/subjects"), s => s.SubjectId == horizonMaths.SubjectId);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAdmin.GetRaw($"my/subjects/{horizonMaths.SubjectId}")).StatusCode);
    }

    [Fact]
    public async Task Teachers_see_only_subjects_they_teach_and_platform_staff_are_refused()
    {
        var amira = await SignIn(Accounts.Amira);
        var mine = await amira.Get<List<MySubjectDto>>("my/subjects");
        Assert.Equal(["MATH10"], mine.Select(s => s.Code));
        Assert.All(mine.SelectMany(s => s.Teachers), t => Assert.Equal(amira.UserId, t.Id));

        var physicsId = (await Subject(await SignIn(Accounts.Daniel), "PHYS10")).SubjectId;
        Assert.Equal(HttpStatusCode.NotFound, (await amira.GetRaw($"my/subjects/{physicsId}")).StatusCode);

        // Admins see every subject with classes.
        var admin = await SignIn(Accounts.Admin);
        Assert.Superset(new HashSet<string> { "MATH10", "PHYS10", "CHEM10", "IELTS-W" }, (await admin.Get<List<MySubjectDto>>("my/subjects")).Select(s => s.Code).ToHashSet());

        // Only institution admins, teachers and students may call it (parents cannot sign in at all).
        var superAdmin = await SignIn(Accounts.SuperAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await superAdmin.GetRaw("my/subjects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await superAdmin.GetRaw($"my/subjects/{physicsId}")).StatusCode);
    }
}
