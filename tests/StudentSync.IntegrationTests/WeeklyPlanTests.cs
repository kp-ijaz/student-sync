using System.Net;
using StudentSync.Contracts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.IntegrationTests;

/// <summary>
/// "Add classes" weekly plans: one student, a different subject and teacher on different days, created together
/// (all or nothing) through POST /sessions/batch, with the teachers notified.
/// </summary>
public sealed class WeeklyPlanTests(ApiFixture api)
{
    private static DateOnly NextMonday(int weeksAhead)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7 * weeksAhead);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        return d;
    }

    private static CreateSessionRequest Part(string title, Guid teacher, Guid subject, Guid student, DateOnly start, WeekDays days, TimeOnly time, int weeks = 2)
        => new(title, SessionType.OneToOne, teacher, null, null, null, subject, null, [student], "Asia/Dubai", start, time, 60,
            DeliveryMode.InPerson, null, null, null, new RecurrenceRequest(RecurrenceFrequency.Weekly, 1, days, start.AddDays(weeks * 7 - 1), null));

    private async Task<(Client Admin, Guid Student, Guid Amira, Guid Daniel, Guid Maths, Guid Science)> SetupAsync()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Weekly Plan (Demo Student)",
            $"weekly-{Guid.NewGuid():N}@horizon.example.test", $"WP-{Guid.NewGuid():N}"[..12], null, null, "Asia/Kolkata"))).Id;
        var teachers = (await admin.Get<PagedResult<TeacherDto>>("teachers?pageSize=100")).Items;
        var subjects = (await admin.Get<List<SubjectDto>>("academics/subjects")).ToDictionary(s => s.Code);
        return (admin, student, teachers.Single(t => t.Email == Accounts.Amira).Id, teachers.Single(t => t.Email == Accounts.Daniel).Id,
            subjects["MATH10"].Id, subjects["C10-SCI"].Id);
    }

    [Fact]
    public async Task Admin_creates_a_different_subject_and_teacher_per_day_in_one_go()
    {
        var (admin, student, amira, daniel, maths, science) = await SetupAsync();
        var start = NextMonday(70);
        var plan = new CreateSessionBatchRequest([
            Part("Mathematics · Weekly", amira, maths, student, start, WeekDays.Monday | WeekDays.Wednesday, new TimeOnly(4, 30)),
            Part("Science · Weekly", daniel, science, student, start, WeekDays.Tuesday, new TimeOnly(4, 30)),
        ]);

        var check = await admin.Post<ConflictCheckResponse>("sessions/batch/check", plan);
        Assert.False(check.HasBlockingConflicts);
        Assert.Empty(check.Conflicts);
        Assert.Equal(6, check.Occurrences.Count(o => !o.SkippedHoliday)); // 2 weeks × (Mon + Wed + Tue)

        var created = await admin.Post<CreateSessionResponse>("sessions/batch", plan);
        Assert.Equal(6, created.SessionIds.Count);
        var sessions = new List<SessionDetailDto>();
        foreach (var id in created.SessionIds) sessions.Add(await admin.Get<SessionDetailDto>($"sessions/{id}"));
        Assert.All(sessions.Where(s => s.SubjectId == maths), s => Assert.Equal(amira, s.TeacherId));
        Assert.All(sessions.Where(s => s.SubjectId == science), s => Assert.Equal(daniel, s.TeacherId));
        Assert.Equal(4, sessions.Count(s => s.SubjectId == maths));
        Assert.Equal(2, sessions.Count(s => s.SubjectId == science));
        Assert.All(sessions, s => Assert.Equal(SessionType.OneToOne, s.SessionType));

        // Both teachers are told about their new classes; the admin does not have to post it anywhere else.
        foreach (var (email, title) in new[] { (Accounts.Amira, "Mathematics · Weekly"), (Accounts.Daniel, "Science · Weekly") })
        {
            var inbox = await (await api.LoginAsync(email)).Get<PagedResult<NotificationDto>>("notifications?pageSize=50");
            Assert.Contains(inbox.Items, n => n.Title == $"New classes for you: {title}");
        }
    }

    [Fact]
    public async Task A_clash_in_one_part_creates_nothing()
    {
        var (admin, student, amira, daniel, maths, science) = await SetupAsync();
        var start = NextMonday(80);
        // Daniel is already busy on that Tuesday.
        var other = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Busy Slot (Demo Student)",
            $"busy-{Guid.NewGuid():N}@horizon.example.test", $"BS-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"))).Id;
        await admin.Post<CreateSessionResponse>("sessions", Part("Busy", daniel, science, other, start.AddDays(1), WeekDays.Tuesday, new TimeOnly(5, 0), weeks: 1));

        var plan = new CreateSessionBatchRequest([
            Part("Maths ok", amira, maths, student, start, WeekDays.Monday, new TimeOnly(5, 0)),
            Part("Science clash", daniel, science, student, start, WeekDays.Tuesday, new TimeOnly(5, 0)),
        ]);
        Assert.True((await admin.Post<ConflictCheckResponse>("sessions/batch/check", plan)).HasBlockingConflicts);
        var r = await admin.PostRaw("sessions/batch", plan);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("teacher_conflict", await Client.Code(r));

        // The Maths part was not created either.
        var from = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
        var list = await admin.Get<List<SessionListItemDto>>($"sessions?studentId={student}&fromUtc={from:O}&toUtc={from.AddDays(30):O}");
        Assert.Empty(list);
    }

    [Fact]
    public async Task A_busy_teacher_gets_free_teacher_and_free_time_suggestions_and_clashing_dates_can_be_skipped()
    {
        var (admin, student, amira, daniel, _, science) = await SetupAsync();
        var start = NextMonday(110);
        // A second Science teacher, free at that time.
        var stand = await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest("Science Stand-in (Demo Teacher)", $"stand-{Guid.NewGuid():N}@horizon.example.test",
            null, null, null, "Asia/Dubai", [science]));
        // Daniel already teaches someone else on the first Tuesday at 05:00.
        var other = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest("Busy Tuesday (Demo Student)",
            $"busy-tue-{Guid.NewGuid():N}@horizon.example.test", $"BT-{Guid.NewGuid():N}"[..12], null, null, "Asia/Dubai"))).Id;
        await admin.Post<CreateSessionResponse>("sessions", Part("Busy", daniel, science, other, start.AddDays(1), WeekDays.Tuesday, new TimeOnly(5, 0), weeks: 1));

        var plan = new CreateSessionBatchRequest([
            Part("Maths", amira, (await admin.Get<List<SubjectDto>>("academics/subjects")).Single(s => s.Code == "MATH10").Id, student, start, WeekDays.Monday, new TimeOnly(5, 0)),
            Part("Science", daniel, science, student, start, WeekDays.Tuesday, new TimeOnly(5, 0)),
        ]);
        var check = await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check", plan);
        Assert.True(check.HasBlockingConflicts);
        Assert.False(check.Parts[0].TeacherBusy);
        var science1 = check.Parts[1];
        Assert.True(science1.TeacherBusy);
        Assert.Equal([start.AddDays(1)], science1.ClashDates);
        Assert.Contains(science1.FreeTeachers, t => t.Id == stand.Id);
        Assert.DoesNotContain(science1.FreeTeachers, t => t.Id == daniel);
        Assert.NotEmpty(science1.FreeTimes);
        Assert.DoesNotContain(new TimeOnly(5, 0), science1.FreeTimes);

        // "Skip these dates": the rest of the plan is created.
        var skipped = plan with { Items = [plan.Items[0], plan.Items[1] with { SkipDates = [.. science1.ClashDates] }] };
        var recheck = await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check", skipped);
        Assert.False(recheck.HasBlockingConflicts);
        Assert.Contains(recheck.Occurrences, o => o.LocalDate == start.AddDays(1) && o.SkippedHoliday && o.Resolution.StartsWith("Skipped: you chose"));
        Assert.Equal(3, (await admin.Post<CreateSessionResponse>("sessions/batch", skipped)).SessionIds.Count); // 2 Mondays + 1 of 2 Tuesdays
    }

    [Fact]
    public async Task Classes_outside_a_teachers_recorded_hours_are_flagged_not_blocked()
    {
        var (admin, student, _, _, maths, _) = await SetupAsync();
        var teacher = await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest("Evenings Only (Demo Teacher)", $"evening-{Guid.NewGuid():N}@horizon.example.test",
            null, null, null, "Asia/Dubai", [maths]));
        await admin.Put($"teachers/{teacher.Id}/availability", new SaveAvailabilityRequest(
            [.. Enum.GetValues<DayOfWeek>().Select(d => new AvailabilitySlot(d, new TimeOnly(16, 0), new TimeOnly(20, 0)))], "Asia/Dubai"));
        var start = NextMonday(120);
        var morning = new CreateSessionBatchRequest([Part("Morning maths", teacher.Id, maths, student, start, WeekDays.Monday, new TimeOnly(6, 0))]);
        var check = await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check", morning);
        Assert.False(check.HasBlockingConflicts);
        Assert.True(check.Parts[0].OutsideAvailability);
        var evening = new CreateSessionBatchRequest([Part("Evening maths", teacher.Id, maths, student, start, WeekDays.Monday, new TimeOnly(17, 0))]);
        Assert.False((await admin.Post<WeeklyPlanCheckResponse>("sessions/batch/check", evening)).Parts[0].OutsideAvailability);
    }

    [Fact]
    public async Task Overlapping_parts_mixed_students_and_other_teachers_are_rejected()
    {
        var (admin, student, amira, daniel, maths, science) = await SetupAsync();
        var start = NextMonday(90);

        // Two parts on the same day and time for one student.
        var overlap = await admin.PostRaw("sessions/batch", new CreateSessionBatchRequest([
            Part("A", amira, maths, student, start, WeekDays.Monday, new TimeOnly(6, 0)),
            Part("B", daniel, science, student, start, WeekDays.Monday, new TimeOnly(6, 30)),
        ]));
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);

        // A plan is for one student only.
        var hiro = (await admin.Get<PagedResult<StudentDto>>("students?search=Hiro")).Items.Single().Id;
        var mixed = await admin.PostRaw("sessions/batch", new CreateSessionBatchRequest([
            Part("A", amira, maths, student, start, WeekDays.Monday, new TimeOnly(6, 0)),
            Part("B", daniel, science, hiro, start, WeekDays.Tuesday, new TimeOnly(6, 0)),
        ]));
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("sessions/batch", new CreateSessionBatchRequest([]))).StatusCode);

        // Teachers schedule only themselves, also inside a weekly plan.
        var asAmira = await (await api.LoginAsync(Accounts.Amira)).PostRaw("sessions/batch", new CreateSessionBatchRequest([
            Part("Mine", amira, maths, student, start, WeekDays.Wednesday, new TimeOnly(6, 0)),
            Part("Daniel's", daniel, science, student, start, WeekDays.Thursday, new TimeOnly(6, 0)),
        ]));
        Assert.Equal(HttpStatusCode.Forbidden, asAmira.StatusCode);

        // Another institution cannot book this student at all.
        var other = await (await api.LoginAsync(Accounts.OtherAdmin)).PostRaw("sessions/batch", new CreateSessionBatchRequest([
            Part("X", amira, maths, student, start, WeekDays.Friday, new TimeOnly(6, 0)),
        ]));
        Assert.NotEqual(HttpStatusCode.OK, other.StatusCode);
    }
}
