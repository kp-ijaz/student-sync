using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Content;
using StudentSync.Domain.Scheduling;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.IntegrationTests;

/// <summary>CBSE curriculum: subject kinds, a student's subjects (core + chosen languages + assigned extras), the admin API and teacher authoring.</summary>
public sealed class CurriculumTests(ApiFixture api)
{
    private const string Meera = "meera@horizon.example.test";
    private const string Lucas = "lucas@horizon.example.test";
    private const string Ethan = "ethan@horizon.example.test";

    private async Task<Dictionary<string, SubjectDto>> SubjectsByCode(Client admin) =>
        (await admin.Get<List<SubjectDto>>("academics/subjects")).ToDictionary(s => s.Code);

    [Fact]
    public async Task Student_sees_core_subjects_her_chosen_languages_and_assigned_extras_only()
    {
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var subjects = await aisha.Get<List<MySubjectDto>>("my/subjects");
        var codes = subjects.Select(s => s.Code).ToHashSet();
        Assert.Superset(new HashSet<string> { "C10-ENG", "MATH10", "C10-SCI", "C10-SST", "C10-L2-HIN", "C10-L3-FRE", "PHYS10" }, codes);
        // Not the other options of her language groups, not extras she was not given, nothing from other classes.
        Assert.DoesNotContain("C10-L2-MAL", codes);
        Assert.DoesNotContain("C10-L2-FRE", codes);
        Assert.DoesNotContain("C10-L3-HIN", codes);
        Assert.DoesNotContain("CHEM10", codes);
        Assert.DoesNotContain("IELTS-W", codes);
        Assert.DoesNotContain("C9-ENG", codes);

        var hindi = subjects.Single(s => s.Code == "C10-L2-HIN");
        Assert.Equal(("language", "Second language"), (hindi.Kind, hindi.Group));
        Assert.Equal(("additional", (string?)null), (subjects.Single(s => s.Code == "PHYS10").Kind, subjects.Single(s => s.Code == "PHYS10").Group));
        Assert.Equal(subjects.OrderBy(s => s.Kind switch { "core" => 0, "language" => 1, _ => 2 }).ThenBy(s => s.Name).Select(s => s.Code), subjects.Select(s => s.Code));

        // A language without any class yet is still listed, with zero counts, its teacher, and an empty syllabus.
        var french = subjects.Single(s => s.Code == "C10-L3-FRE");
        Assert.Equal(("language", "Third language"), (french.Kind, french.Group));
        Assert.Equal(0, french.ClassCount);
        Assert.Null(french.NextSession);
        Assert.Contains(french.Teachers, t => t.Name.StartsWith("Meera Nair"));
        var syllabus = await aisha.Get<SubjectSyllabusDto>($"my/subjects/{french.SubjectId}");
        Assert.Equal("language", syllabus.Kind);
        Assert.Equal("Third language", syllabus.Group);
        Assert.Empty(syllabus.Chapters);
        Assert.Null(syllabus.NextSession);

        // The student catalogue endpoint follows the same rule.
        var catalogue = (await aisha.Get<List<SubjectDto>>("academics/subjects")).Select(s => s.Code).ToHashSet();
        Assert.Equal(codes, catalogue);
    }

    [Fact]
    public async Task Subjects_that_are_not_the_students_are_404()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var all = await SubjectsByCode(admin);
        var ethan = await api.LoginAsync(Ethan);
        Assert.Equal(HttpStatusCode.NotFound, (await ethan.GetRaw($"my/subjects/{all["CHEM10"].Id}")).StatusCode);      // unassigned additional
        Assert.Equal(HttpStatusCode.NotFound, (await ethan.GetRaw($"my/subjects/{all["C10-L2-ARA"].Id}")).StatusCode);   // language he did not choose
        Assert.Equal(HttpStatusCode.NotFound, (await ethan.GetRaw($"my/subjects/{all["C9-ENG"].Id}")).StatusCode);       // another standard
        Assert.Equal(HttpStatusCode.OK, (await ethan.GetRaw($"my/subjects/{all["C10-SST"].Id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_sets_language_choices_and_additional_subjects_with_validation()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var all = await SubjectsByCode(admin);
        var created = await admin.Post<CreatedPersonResponse>("students",
            new CreateStudentRequest("Curriculum Test (Demo Student)", $"curriculum-{Guid.NewGuid():N}@horizon.example.test", $"CT-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"));
        var studentId = created.Id;
        await admin.Post($"academics/courses/{all["C10-ENG"].CourseId}/enrolments", new EnrolStudentRequest(studentId));

        var before = await admin.Get<StudentSubjectsDto>($"students/{studentId}/subjects");
        Assert.Equal(["English", "Mathematics", "Science", "Social Science"], before.Core.Select(s => s.Name).Order());
        Assert.Equal(["Second language", "Third language"], before.ChoiceGroups.Select(g => g.Group));
        Assert.All(before.ChoiceGroups, g => Assert.Equal(["Arabic", "French", "Hindi", "Malayalam"], g.Options.Select(o => o.Name)));
        Assert.All(before.ChoiceGroups, g => Assert.Empty(g.ChosenSubjectIds));
        Assert.Empty(before.Additional);

        async Task Rejected(SaveStudentSubjectsRequest r, HttpStatusCode expected = HttpStatusCode.BadRequest)
            => Assert.Equal(expected, (await admin.PutRaw($"students/{studentId}/subjects", r)).StatusCode);
        var l2 = "Second language";
        await Rejected(new([new("Third language", all["C10-L2-HIN"].Id)], []));                         // option of another group
        await Rejected(new([new(l2, all["C9-L2-HIN"].Id)], []));                                         // group of a course they are not in
        await Rejected(new([new(l2, all["C10-L2-HIN"].Id), new(l2, all["C10-L2-MAL"].Id)], []));         // two options in one group
        await Rejected(new([new(l2, all["C10-ENG"].Id)], []));                                           // a core subject is not a choice
        await Rejected(new([], [all["C10-ENG"].Id]));                                                    // already a core subject
        await Rejected(new([], [all["C10-L2-FRE"].Id]));                                                 // languages go through their group
        await Rejected(new([], [Guid.NewGuid()]));                                                       // unknown subject
        var northbridgeAdmin = await api.LoginAsync(Accounts.OtherAdmin);
        var northbridgeSubject = (await northbridgeAdmin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == "NB-MATH").Id;
        await Rejected(new([], [northbridgeSubject]));                                                   // another institution's subject
        await Rejected(new([new(l2, northbridgeSubject)], []));
        // Rejected requests change nothing.
        Assert.Equal(before, await admin.Get<StudentSubjectsDto>($"students/{studentId}/subjects"), new StudentSubjectsComparer());

        // Language choices plus extras from any course (coaching subject, another standard's core subject).
        var r = await admin.PutRaw($"students/{studentId}/subjects", new SaveStudentSubjectsRequest(
            [new(" second LANGUAGE ", all["C10-L2-MAL"].Id), new("Third language", all["C10-L3-ARA"].Id)], [all["PHYS10"].Id, all["C11S-PHY"].Id, all["PHYS10"].Id]));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var saved = (await r.Content.ReadFromJsonAsync<StudentSubjectsDto>(ApiFixture.Json))!;
        Assert.Equal([all["C10-L2-MAL"].Id], saved.ChoiceGroups.Single(g => g.Group == l2).ChosenSubjectIds);
        Assert.Equal([all["C10-L3-ARA"].Id], saved.ChoiceGroups.Single(g => g.Group == "Third language").ChosenSubjectIds);
        Assert.Equal(["C11S-PHY", "PHYS10"], saved.Additional.Select(a => a.Code).Order());

        // Replacing: dropping an extra and switching a language removes the old rows.
        await admin.Put($"students/{studentId}/subjects", new SaveStudentSubjectsRequest([new(l2, all["C10-L2-HIN"].Id)], [all["PHYS10"].Id]));
        var replaced = await admin.Get<StudentSubjectsDto>($"students/{studentId}/subjects");
        Assert.Equal([all["C10-L2-HIN"].Id], replaced.ChoiceGroups.Single(g => g.Group == l2).ChosenSubjectIds);
        Assert.Empty(replaced.ChoiceGroups.Single(g => g.Group == "Third language").ChosenSubjectIds);
        Assert.Equal(["PHYS10"], replaced.Additional.Select(a => a.Code));

        using (var scope = api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = studentId.ToString();
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "student.subjects_updated" && a.EntityId == id && a.UserId == admin.UserId));
            Assert.Equal(2, await db.StudentSubjects.IgnoreQueryFilters().CountAsync(x => x.StudentId == studentId));
        }

        // Only institution admins; other institutions cannot even find the student.
        Assert.Equal(HttpStatusCode.NotFound, (await northbridgeAdmin.GetRaw($"students/{studentId}/subjects")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await northbridgeAdmin.PutRaw($"students/{studentId}/subjects", new SaveStudentSubjectsRequest([], []))).StatusCode);
        var isla = (await northbridgeAdmin.Get<PagedResult<StudentDto>>("students?search=Isla")).Items.Single().Id;
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"students/{isla}/subjects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).GetRaw($"students/{studentId}/subjects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Aisha)).PutRaw($"students/{studentId}/subjects", new SaveStudentSubjectsRequest([], []))).StatusCode);
    }

    [Fact]
    public async Task Teachers_see_the_subjects_they_are_assigned_to_even_without_classes()
    {
        var meera = await api.LoginAsync(Meera);
        var mine = await meera.Get<List<MySubjectDto>>("my/subjects");
        Assert.Equal(["C10-L2-ARA", "C10-L2-FRE", "C10-L2-HIN", "C10-L2-MAL", "C10-L3-ARA", "C10-L3-FRE", "C10-L3-HIN", "C10-L3-MAL"], mine.Select(s => s.Code).Order());
        Assert.All(mine, s => Assert.Equal("language", s.Kind));
        var arabic = mine.Single(s => s.Code == "C10-L3-ARA");
        Assert.Equal(0, arabic.ClassCount);
        Assert.Equal(HttpStatusCode.OK, (await meera.GetRaw($"my/subjects/{arabic.SubjectId}")).StatusCode);
        Assert.True(mine.Single(s => s.Code == "C10-L2-HIN").ClassCount > 0); // her timetable classes

        var lucas = await api.LoginAsync(Lucas);
        var lucasSubjects = await lucas.Get<List<MySubjectDto>>("my/subjects");
        Assert.Equal(["C10-ENG", "IELTS-W"], lucasSubjects.Select(s => s.Code).Order());
        Assert.Equal(HttpStatusCode.NotFound, (await lucas.GetRaw($"my/subjects/{arabic.SubjectId}")).StatusCode);
    }

    [Fact]
    public async Task Subject_kind_and_choice_group_are_saved_and_kept_by_older_callers()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var courseId = (await SubjectsByCode(admin))["C9-ENG"].CourseId;
        var code = $"T{Guid.NewGuid():N}"[..12];
        var missingGroup = await admin.PostRaw("academics/subjects", new SaveSubjectRequest(courseId, code, "Sanskrit", null, CatalogStatus.Active, SubjectKind.LanguageOption, " "));
        Assert.Equal(HttpStatusCode.BadRequest, missingGroup.StatusCode);

        var id = (await admin.Post<IdResponse>("academics/subjects", new SaveSubjectRequest(courseId, code, "Sanskrit", null, CatalogStatus.Active, SubjectKind.LanguageOption, "Third language"))).Id;
        var saved = (await admin.Get<List<SubjectDto>>($"academics/subjects?courseId={courseId}")).Single(s => s.Id == id);
        Assert.Equal((SubjectKind.LanguageOption, "Third language"), (saved.Kind, saved.ChoiceGroup));

        // An update without kind fields (an older client) keeps them.
        await admin.Put($"academics/subjects/{id}", new SaveSubjectRequest(courseId, code, "Sanskrit (optional)", null, CatalogStatus.Active));
        saved = (await admin.Get<List<SubjectDto>>($"academics/subjects?courseId={courseId}")).Single(s => s.Id == id);
        Assert.Equal(("Sanskrit (optional)", SubjectKind.LanguageOption, "Third language"), (saved.Name, saved.Kind, saved.ChoiceGroup));
        await admin.Put($"academics/subjects/{id}", new SaveSubjectRequest(courseId, code, "Sanskrit (optional)", null, CatalogStatus.Archived, SubjectKind.Core, "ignored"));
        saved = (await admin.Get<List<SubjectDto>>($"academics/subjects?courseId={courseId}")).Single(s => s.Id == id);
        Assert.Equal((SubjectKind.Core, (string?)null), (saved.Kind, saved.ChoiceGroup));
    }

    [Fact]
    public async Task Teacher_authors_notes_assignments_and_iphone_videos_from_the_app_and_students_see_only_published_notes()
    {
        var lucas = await api.LoginAsync(Lucas);
        var aisha = await api.LoginAsync(Accounts.Aisha);
        var englishId = (await aisha.Get<List<MySubjectDto>>("my/subjects")).Single(s => s.Code == "C10-ENG").SubjectId;
        var session = (await lucas.Get<List<SessionListItemDto>>($"sessions?fromUtc={DateTime.UtcNow.AddDays(1):O}&limit=50"))
            .First(s => s.Title == "Class 10 — English");

        // Notes: a draft is invisible to students until it is published to participants.
        var noteId = (await lucas.Post<IdResponse>($"sessions/{session.Id}/notes", new SaveNoteRequest("App note (test)", "Read pages 1–3.", NoteVisibility.Participants, false, null))).Id;
        var hidden = (await lucas.Post<IdResponse>($"sessions/{session.Id}/notes", new SaveNoteRequest("Teacher-only plan (test)", "Plan.", NoteVisibility.TeacherOnly, false, null))).Id;
        await lucas.Post($"notes/{hidden}/publish");
        Assert.DoesNotContain((await aisha.Get<PagedResult<NoteDto>>($"library/notes?subjectId={englishId}&pageSize=100")).Items, n => n.Id == noteId || n.Id == hidden);
        await lucas.Post($"notes/{noteId}/publish");
        var library = await aisha.Get<PagedResult<NoteDto>>($"library/notes?subjectId={englishId}&pageSize=100");
        Assert.Contains(library.Items, n => n.Id == noteId && n.Status == PublicationStatus.Published);
        Assert.DoesNotContain(library.Items, n => n.Id == hidden);
        Assert.All(library.Items, n => Assert.Equal(NoteVisibility.Participants, n.Visibility));
        // Another student in no English class sees none of it.
        var hiro = await api.LoginAsync(Accounts.Hiro);
        Assert.DoesNotContain((await hiro.Get<PagedResult<NoteDto>>($"library/notes?subjectId={englishId}&pageSize=100")).Items, n => n.Id == noteId);

        // Assignments linked to a session default to the session's participants.
        var detail = await lucas.Get<SessionDetailDto>($"sessions/{session.Id}");
        var assignmentId = (await lucas.Post<IdResponse>("assignments", new SaveAssignmentRequest("App homework (test)", "Answer the questions.", DateTime.UtcNow.AddDays(3), 10, true, false, session.Id, null, null, null, null))).Id;
        await lucas.Post($"assignments/{assignmentId}/publish");
        var assignment = await lucas.Get<AssignmentDetailDto>($"assignments/{assignmentId}");
        Assert.Equal(detail.ParticipantCount, assignment.Recipients.Count);
        Assert.Contains(assignment.Recipients, r => r.Id == aisha.UserId);
        Assert.Equal(HttpStatusCode.OK, (await aisha.GetRaw($"assignments/{assignmentId}")).StatusCode);

        // iPhone camera-roll videos are QuickTime (.mov, "ftyp" + brand "qt  ").
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0, 0, 0, 0x14, .. "ftypqt  "u8.ToArray(), 0, 0, 0, 0, .. "qt  "u8.ToArray()]);
        file.Headers.ContentType = new("video/quicktime");
        form.Add(file, "file", "IMG_0001.MOV");
        form.Add(new StringContent("Recorded on a phone (test)"), "title");
        form.Add(new StringContent("BeforeClass"), "unlockRule");
        form.Add(new StringContent("30"), "unlockOffsetMinutes");
        form.Add(new StringContent("75"), "durationSeconds");
        var upload = await lucas.Http.PostAsync($"/api/v1/sessions/{session.Id}/videos", form);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        var videoId = (await upload.Content.ReadFromJsonAsync<IdResponse>(ApiFixture.Json))!.Id;
        var video = (await lucas.Get<List<PrerecordedVideoDto>>($"sessions/{session.Id}/videos")).Single(v => v.Id == videoId);
        Assert.Equal((VideoUnlockRule.BeforeClass, 30, 75), (video.UnlockRule, video.UnlockOffsetMinutes, video.DurationSeconds));
        Assert.False((await aisha.Get<List<PrerecordedVideoDto>>($"sessions/{session.Id}/videos")).Single(v => v.Id == videoId).IsUnlocked);
    }

    [Fact]
    public async Task Demo_timetable_is_hourly_and_never_double_books_a_teacher_or_student()
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var series = await db.SessionSeries.IgnoreQueryFilters().Where(s => s.Title.StartsWith("Class 10 — ")).Select(s => s.Id).ToListAsync();
        Assert.Equal(6, series.Count);
        var sessions = await db.Sessions.IgnoreQueryFilters().Where(s => s.SeriesId != null && series.Contains(s.SeriesId.Value))
            .Select(s => new { s.Id, s.TeacherId, s.StartUtc, s.EndUtc, Students = s.Participants.Select(p => p.StudentId).ToList() }).ToListAsync();
        Assert.True(sessions.Count > 60);
        Assert.All(sessions, s => Assert.Equal(TimeSpan.FromHours(1), s.EndUtc - s.StartUtc));
        foreach (var t in sessions)
            Assert.DoesNotContain(sessions, o => o.Id != t.Id && o.StartUtc < t.EndUtc && o.EndUtc > t.StartUtc && (o.TeacherId == t.TeacherId || o.Students.Intersect(t.Students).Any()));

        // Nor does it overlap the other demo classes that existed when it was seeded (tests add more classes later).
        var ids = sessions.Select(s => s.Id).ToList();
        var seededAt = await db.Sessions.IgnoreQueryFilters().Where(s => ids.Contains(s.Id)).MaxAsync(s => s.CreatedAtUtc);
        var demo = await db.Sessions.IgnoreQueryFilters().Where(s => s.Status != SessionStatus.Cancelled && !ids.Contains(s.Id) && s.CreatedAtUtc <= seededAt)
            .Select(s => new { s.TeacherId, s.StartUtc, s.EndUtc, Students = s.Participants.Select(p => p.StudentId).ToList() }).ToListAsync();
        Assert.NotEmpty(demo);
        foreach (var t in sessions)
            Assert.DoesNotContain(demo, o => o.StartUtc < t.EndUtc && o.EndUtc > t.StartUtc && (o.TeacherId == t.TeacherId || o.Students.Intersect(t.Students).Any()));
    }
}

/// <summary>Structural equality for the nested lists of <see cref="StudentSubjectsDto"/>.</summary>
internal sealed class StudentSubjectsComparer : IEqualityComparer<StudentSubjectsDto>
{
    public bool Equals(StudentSubjectsDto? a, StudentSubjectsDto? b) =>
        a is not null && b is not null && a.Core.SequenceEqual(b.Core) && a.Additional.SequenceEqual(b.Additional)
        && a.ChoiceGroups.Count == b.ChoiceGroups.Count
        && a.ChoiceGroups.Zip(b.ChoiceGroups).All(p => p.First.Group == p.Second.Group && p.First.Options.SequenceEqual(p.Second.Options) && p.First.ChosenSubjectIds.SequenceEqual(p.Second.ChosenSubjectIds));
    public int GetHashCode(StudentSubjectsDto obj) => obj.Core.Count;
}
