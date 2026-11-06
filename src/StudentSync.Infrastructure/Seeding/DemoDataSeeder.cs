using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudentSync.Application.AI;
using StudentSync.Application.Common;
using StudentSync.Domain.AI;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;
using StudentSync.Domain.Tenancy;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.Infrastructure.Seeding;

public sealed class DemoOptions
{
    /// <summary>Password for all demo accounts. Supplied by the local .env (DEMO_PASSWORD); never used in production.</summary>
    public string? Password { get; set; }
    public bool ForcePasswordChange { get; set; } = true;
}

/// <summary>
/// Fictional, clearly-labelled development data covering every teaching mode. Idempotent: the whole dataset is created in
/// one transaction and skipped entirely if the demo institution already exists. All emails use the non-deliverable example.test domain.
/// </summary>
public sealed partial class DemoDataSeeder(AppDbContext db, IUserAccountService accounts, ITenantContext tenantContext, IClock clock, IFileStorage storage, ILogger<DemoDataSeeder> logger)
{
    public const string PrimarySlug = "horizon-demo";
    public const string SecondarySlug = "northbridge-demo";

    private string _password = "";
    private bool _forceChange;

    public async Task<bool> SeedAsync(DemoOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Password) || options.Password.Length < 10)
            throw new InvalidOperationException("Set Demo:Password (DEMO_PASSWORD in .env) to a value of at least 10 characters before seeding demo data.");
        _password = options.Password;
        _forceChange = options.ForcePasswordChange;

        if (await db.Tenants.AnyAsync(t => t.Slug == PrimarySlug, ct))
        {
            logger.LogInformation("Demo data already present.");
            await SeedPhotosAsync(ct);
            await EnsureSyllabusAsync(ct);
            await EnsureCurriculumAsync(ct);
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CreateUserAsync(null, "superadmin@studentsync.example.test", "Platform Super Admin (Demo)", UserRole.SuperAdmin, "Asia/Dubai", ct);
        await SeedPrimaryAsync(ct);
        await SeedSecondaryAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Demo data created.");
        await SeedPhotosAsync(ct);
        await EnsureSyllabusAsync(ct);
        await EnsureCurriculumAsync(ct);
        return true;
    }

    /// <summary>Demo staff accounts and the embedded photo for each (Seeding/DemoPhotos/&lt;key&gt;.jpg). Students get no seeded photo.</summary>
    private static readonly (string Slug, string Email, string Key)[] PhotoAccounts =
    [
        (PrimarySlug, "amira@horizon.example.test", "amira"), (PrimarySlug, "priya@horizon.example.test", "priya"),
        (PrimarySlug, "daniel@horizon.example.test", "daniel"), (PrimarySlug, "lucas@horizon.example.test", "lucas"),
        (PrimarySlug, "admin@horizon.example.test", "hana"), (PrimarySlug, "admin2@horizon.example.test", "omar"),
        (SecondarySlug, "teacher@northbridge.example.test", "oliver"), (SecondarySlug, "admin@northbridge.example.test", "nora"),
    ];

    /// <summary>
    /// Gives demo staff accounts a profile photo. Runs on every seed and is idempotent: accounts that already have a
    /// photo (seeded or uploaded) are left alone, and a missing image file is skipped with a warning.
    /// </summary>
    public async Task<int> SeedPhotosAsync(CancellationToken ct)
    {
        var assembly = typeof(DemoDataSeeder).Assembly;
        var added = 0;
        foreach (var (slug, email, key) in PhotoAccounts)
        {
            var tenantId = await db.Tenants.Where(t => t.Slug == slug && t.IsDemo).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
            if (tenantId is not { } tid) continue;
            var normalized = email.ToUpperInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == tid && u.NormalizedEmail == normalized && u.IsDemo, ct);
            if (user is null || user.PhotoFileId is not null) continue;

            await using var resource = assembly.GetManifestResourceStream($"StudentSync.DemoPhotos.{key}.jpg");
            if (resource is null)
            {
                logger.LogWarning("Demo photo {Key}.jpg is not available; {Email} keeps the initials avatar.", key, email);
                continue;
            }
            using var buffer = new MemoryStream();
            await resource.CopyToAsync(buffer, ct);
            byte[] bytes;
            try { bytes = Application.Content.ImageMetadataStripper.StripJpeg(buffer.ToArray()); }
            catch (Application.Content.InvalidImageException ex)
            {
                logger.LogWarning("Demo photo {Key}.jpg is not a valid JPEG ({Reason}); skipped.", key, ex.Message);
                continue;
            }

            using var _ = tenantContext.BeginScope(tid);
            var storageKey = $"{tid:N}/{clock.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
            using (var content = new MemoryStream(bytes)) await storage.SaveAsync(storageKey, content, ct);
            var file = new StoredFile
            {
                TenantId = tid,
                StorageKey = storageKey,
                FileName = $"{key}.jpg",
                ContentType = "image/jpeg",
                SizeBytes = bytes.Length,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                Purpose = FilePurpose.Avatar,
                UploadedByUserId = user.Id,
            };
            db.Files.Add(file);
            user.PhotoFileId = file.Id;
            await db.SaveChangesAsync(ct);
            added++;
        }
        if (added > 0) logger.LogInformation("Added {Count} demo profile photo(s).", added);
        return added;
    }

    /// <summary>
    /// Fictional demo syllabus of the extra coaching subjects, in teaching order. Existing chapter titles are kept as they are.
    /// Class 10 Mathematics (MATH10) follows the NCERT chapters instead (<see cref="EnsureCurriculumAsync"/>).
    /// </summary>
    private static readonly (string SubjectCode, string[] Chapters)[] DemoSyllabus =
    [
        ("PHYS10", ["Measurement and units", "Motion", "Forces", "Energy and work", "Waves"]),
        ("CHEM10", ["Matter and its states", "Atomic Structure", "Chemical bonding", "Acids and bases"]),
        ("IELTS-W", ["Task 1 — describing data", "Writing Task 2", "Vocabulary and coherence"]),
    ];

    /// <summary>
    /// Gives the demo Horizon subjects a full syllabus. Runs on every seed and is idempotent: chapters are matched by title
    /// (case-insensitive), so existing chapters keep their ids and only missing ones are added; sort orders are corrected.
    /// </summary>
    public async Task<int> EnsureSyllabusAsync(CancellationToken ct)
    {
        var tenantId = await db.Tenants.Where(t => t.Slug == PrimarySlug && t.IsDemo).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (tenantId is not { } tid) return 0;
        using var _ = tenantContext.BeginScope(tid);
        var codes = DemoSyllabus.Select(x => x.SubjectCode).ToList();
        var subjects = await db.Subjects.Include(s => s.Chapters).Where(s => s.TenantId == tid && codes.Contains(s.Code)).ToListAsync(ct);
        int added = 0, reordered = 0;
        foreach (var (code, titles) in DemoSyllabus)
        {
            var subject = subjects.FirstOrDefault(s => s.Code == code);
            if (subject is null) continue;
            for (var i = 0; i < titles.Length; i++)
            {
                var existing = subject.Chapters.FirstOrDefault(c => string.Equals(c.Title.Trim(), titles[i], StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    db.Chapters.Add(new Chapter { TenantId = tid, SubjectId = subject.Id, Title = titles[i], SortOrder = i + 1 });
                    added++;
                }
                else if (existing.SortOrder != i + 1)
                {
                    existing.SortOrder = i + 1;
                    reordered++;
                }
            }
        }
        if (added + reordered == 0) return 0;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo syllabus: added {Added} chapter(s), re-ordered {Reordered}.", added, reordered);
        return added;
    }

    private async Task<Guid> CreateUserAsync(Guid? tenantId, string email, string name, UserRole role, string tz, CancellationToken ct)
    {
        var account = await accounts.CreateAsync(tenantId, email, name, role, tz, _password, true, ct);
        var user = await db.Users.FirstAsync(u => u.Id == account.UserId, ct);
        user.MustChangePassword = _forceChange;
        user.EmailConfirmed = true;
        await db.SaveChangesAsync(ct);
        return account.UserId;
    }

    private async Task<Tenant> CreateTenantAsync(string name, string slug, string country, string tz, string currency, string planCode, CancellationToken ct)
    {
        var plan = await db.Plans.FirstAsync(p => p.Code == planCode, ct);
        var tenant = new Tenant
        {
            Name = name,
            Slug = slug,
            CountryCode = country,
            DefaultTimeZoneId = tz,
            CurrencyCode = currency,
            IsDemo = true,
            ContactEmail = $"office@{slug}.example.test",
            RecordingEnabled = true,
            TranscriptionEnabled = true,
            AiAssistantEnabled = true,
        };
        db.Tenants.Add(tenant);
        var sub = new TenantSubscription { TenantId = tenant.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active, StartedAtUtc = clock.UtcNow.AddDays(-60), CurrentPeriodEndUtc = clock.UtcNow.AddDays(30), BillingEmail = tenant.ContactEmail };
        db.Subscriptions.Add(sub);
        db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenant.Id, SubscriptionId = sub.Id, EventType = "created", ToPlanId = plan.Id, ToStatus = SubscriptionStatus.Active, Note = "Demo seed" });
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    private async Task SeedSecondaryAsync(CancellationToken ct)
    {
        // A second institution so tenant isolation can be exercised with real data.
        var t = await CreateTenantAsync("Northbridge Learning Centre (Demo)", SecondarySlug, "GB", "Europe/London", "GBP", "starter", ct);
        using var _ = tenantContext.BeginScope(t.Id);
        await CreateUserAsync(t.Id, "admin@northbridge.example.test", "Nora Whitfield (Demo Admin)", UserRole.InstitutionAdmin, "Europe/London", ct);
        var teacherId = await CreateUserAsync(t.Id, "teacher@northbridge.example.test", "Oliver Grant (Demo Teacher)", UserRole.Teacher, "Europe/London", ct);
        db.Teachers.Add(new Teacher { Id = teacherId, EmployeeCode = "NB-T01" });
        var studentId = await CreateUserAsync(t.Id, "student@northbridge.example.test", "Isla Bennett (Demo Student)", UserRole.Student, "Europe/London", ct);
        db.Students.Add(new Student { Id = studentId, StudentCode = "NB-001" });
        var course = new Course { Code = "GCSE-MATH", Name = "GCSE Mathematics" };
        db.Courses.Add(course);
        var subject = new Subject { CourseId = course.Id, Code = "NB-MATH", Name = "Mathematics" };
        db.Subjects.Add(subject);
        db.CourseEnrolments.Add(new CourseEnrolment { CourseId = course.Id, StudentId = studentId, EnrolledAtUtc = clock.UtcNow.AddDays(-30) });
        await db.SaveChangesAsync(ct);
        var s = Session("Private lesson: Algebra (Northbridge)", SessionType.OneToOne, teacherId, "Europe/London", DateOnly.FromDateTime(clock.UtcNow.AddDays(2)), new TimeOnly(16, 0), 60, [studentId], subjectId: subject.Id);
        db.Sessions.Add(s);
        await db.SaveChangesAsync(ct);
    }

    private LearningSession Session(string title, SessionType type, Guid teacherId, string tz, DateOnly date, TimeOnly time, int minutes, IEnumerable<Guid> students,
        Guid? batchId = null, Guid? arrangementId = null, Guid? courseId = null, Guid? subjectId = null, Guid? chapterId = null, Guid? seriesId = null, bool occurrence = false)
    {
        var start = ZonedTime.ToUtc(date, time, tz).Utc;
        var s = new LearningSession
        {
            Title = title,
            SessionType = type,
            TeacherId = teacherId,
            BatchId = batchId,
            ArrangementId = arrangementId,
            CourseId = courseId,
            SubjectId = subjectId,
            ChapterId = chapterId,
            SeriesId = seriesId,
            OccurrenceDate = occurrence ? date : null,
            StartUtc = start,
            EndUtc = start.AddMinutes(minutes),
            TimeZoneId = tz,
            DeliveryMode = DeliveryMode.Online,
            MeetingProvider = MeetingProvider.Manual,
            MeetingUrl = "https://meet.example.test/demo-" + Guid.NewGuid().ToString("N")[..8],
            Objectives = "Demo session objectives.",
        };
        foreach (var id in students) s.Participants.Add(new SessionParticipant { StudentId = id });
        LearningSession.ValidateShape(type, batchId, s.Participants.Count);
        if (start + TimeSpan.FromMinutes(minutes) < clock.UtcNow) s.Status = SessionStatus.Completed;
        return s;
    }

    private async Task SeedPrimaryAsync(CancellationToken ct)
    {
        const string dubai = "Asia/Dubai";
        var t = await CreateTenantAsync("Horizon Tuition Academy (Demo)", PrimarySlug, "AE", dubai, "AED", "growth", ct);
        using var _ = tenantContext.BeginScope(t.Id);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(now, dubai));

        // ---- People ----
        var admin = await CreateUserAsync(t.Id, "admin@horizon.example.test", "Hana Rahman (Demo Admin)", UserRole.InstitutionAdmin, dubai, ct);
        await CreateUserAsync(t.Id, "admin2@horizon.example.test", "Omar Siddiqui (Demo Admin)", UserRole.InstitutionAdmin, dubai, ct);
        var teacherSpecs = new[]
        {
            ("amira", "Amira Haddad (Demo Teacher)", "Mathematics", dubai), ("daniel", "Daniel Okafor (Demo Teacher)", "Physics", dubai),
            ("priya", "Priya Nair (Demo Teacher)", "Chemistry", "Asia/Kolkata"), ("lucas", "Lucas Moreau (Demo Teacher)", "English (IELTS)", "Europe/London"),
        };
        var teachers = new Dictionary<string, Guid>();
        var n = 1;
        foreach (var (key, name, spec, tz) in teacherSpecs)
        {
            var id = await CreateUserAsync(t.Id, $"{key}@horizon.example.test", name, UserRole.Teacher, tz, ct);
            db.Teachers.Add(new Teacher { Id = id, EmployeeCode = $"HZ-T{n++:00}", Specialisations = spec, Bio = "Fictional demo teacher profile." });
            teachers[key] = id;
        }

        var grades = new[] { "Grade 9", "Grade 10", "Grade 11" }.Select((g, i) => new GradeLevel { Name = g, SortOrder = i + 9 }).ToList();
        db.GradeLevels.AddRange(grades);
        var studentSpecs = new[]
        {
            ("Aisha Khan", dubai), ("Ben Carter", "Europe/London"), ("Chloe Martin", dubai), ("Dev Patel", "Asia/Kolkata"),
            ("Ethan Brooks", "America/New_York"), ("Fatima Al Mansoori", dubai), ("Gabriel Silva", "Europe/London"), ("Hiro Tanaka", dubai),
        };
        var students = new List<Guid>();
        n = 1;
        foreach (var (name, tz) in studentSpecs)
        {
            var first = name.Split(' ')[0].ToLowerInvariant();
            var id = await CreateUserAsync(t.Id, $"{first}@horizon.example.test", $"{name} (Demo Student)", UserRole.Student, tz, ct);
            db.Students.Add(new Student { Id = id, StudentCode = $"HZ-{n++:000}", GradeLevelId = grades[1].Id, SchoolName = "Example Secondary School (fictional)" });
            students.Add(id);
        }
        var parent1 = await CreateUserAsync(t.Id, "parent.khan@horizon.example.test", "Sara Khan (Demo Parent)", UserRole.Parent, dubai, ct);
        var parent2 = await CreateUserAsync(t.Id, "parent.almansoori@horizon.example.test", "Khalid Al Mansoori (Demo Parent)", UserRole.Parent, dubai, ct);
        db.Parents.AddRange(new Parent { Id = parent1 }, new Parent { Id = parent2 });
        await db.SaveChangesAsync(ct);
        db.ParentLinks.AddRange(
            new ParentStudentLink { ParentId = parent1, StudentId = students[0], Relationship = GuardianRelationship.Mother, IsVerified = true, VerifiedAtUtc = now, VerifiedByUserId = admin },
            new ParentStudentLink { ParentId = parent2, StudentId = students[5], Relationship = GuardianRelationship.Father, IsVerified = true, VerifiedAtUtc = now, VerifiedByUserId = admin },
            new ParentStudentLink { ParentId = parent2, StudentId = students[7], Relationship = GuardianRelationship.Guardian, IsVerified = true, VerifiedAtUtc = now, VerifiedByUserId = admin });

        // ---- Academic structure ----
        var year = new AcademicYear { Name = $"{today.Year}–{(today.Year + 1) % 100:00}", StartDate = new DateOnly(today.Year, 9, 1).AddYears(today.Month < 9 ? -1 : 0), IsCurrent = true };
        year.EndDate = year.StartDate.AddYears(1).AddDays(-1);
        year.Terms.Add(new Term { Name = "Term 1", StartDate = year.StartDate, EndDate = year.StartDate.AddMonths(4).AddDays(-1) });
        year.Terms.Add(new Term { Name = "Term 2", StartDate = year.StartDate.AddMonths(4), EndDate = year.EndDate });
        db.AcademicYears.Add(year);
        var science = new Course { Code = "G10-SM", Name = "Grade 10 Science & Mathematics", GradeLevelId = grades[1].Id, AcademicYearId = year.Id, Description = "Demo course." };
        var ielts = new Course { Code = "IELTS", Name = "IELTS Preparation", AcademicYearId = year.Id, Description = "Demo course." };
        db.Courses.AddRange(science, ielts);
        var maths = new Subject { CourseId = science.Id, Code = "MATH10", Name = "Mathematics" };
        var physics = new Subject { CourseId = science.Id, Code = "PHYS10", Name = "Physics" };
        var chemistry = new Subject { CourseId = science.Id, Code = "CHEM10", Name = "Chemistry" };
        var english = new Subject { CourseId = ielts.Id, Code = "IELTS-W", Name = "IELTS Writing" };
        db.Subjects.AddRange(maths, physics, chemistry, english);
        var quadratics = new Chapter { SubjectId = maths.Id, Title = "Quadratic Equations", SortOrder = 1 };
        quadratics.Topics.Add(new Topic { Title = "Factorisation", SortOrder = 1 });
        quadratics.Topics.Add(new Topic { Title = "The quadratic formula", SortOrder = 2 });
        var trig = new Chapter { SubjectId = maths.Id, Title = "Introduction to Trigonometry", SortOrder = 2 };
        var motion = new Chapter { SubjectId = physics.Id, Title = "Motion", SortOrder = 1 };
        var atoms = new Chapter { SubjectId = chemistry.Id, Title = "Atomic Structure", SortOrder = 1 };
        var task2 = new Chapter { SubjectId = english.Id, Title = "Writing Task 2", SortOrder = 1 };
        db.Chapters.AddRange(quadratics, trig, motion, atoms, task2);
        db.TeacherSubjects.AddRange(
            new TeacherSubject { TeacherId = teachers["amira"], SubjectId = maths.Id }, new TeacherSubject { TeacherId = teachers["daniel"], SubjectId = physics.Id },
            new TeacherSubject { TeacherId = teachers["priya"], SubjectId = chemistry.Id }, new TeacherSubject { TeacherId = teachers["lucas"], SubjectId = english.Id });
        foreach (var s in students.Take(7)) db.CourseEnrolments.Add(new CourseEnrolment { CourseId = science.Id, StudentId = s, EnrolledAtUtc = now.AddDays(-60) });
        db.CourseEnrolments.Add(new CourseEnrolment { CourseId = ielts.Id, StudentId = students[6], EnrolledAtUtc = now.AddDays(-30) });

        var batch = new Batch { Code = "G10-MATH-A", Name = "G10 Maths — Evening A", CourseId = science.Id, SubjectId = maths.Id, TeacherId = teachers["amira"], Capacity = 12, StartDate = today.AddDays(-21) };
        foreach (var s in students.Take(5)) batch.Enrolments.Add(new BatchEnrolment { StudentId = s, JoinedAtUtc = now.AddDays(-21) });
        db.Batches.Add(batch);

        var chemOneToOne = new TeachingArrangement { Type = ArrangementType.OneToOne, Title = "Chemistry 1:1 — Fatima", TeacherId = teachers["priya"], CourseId = science.Id, SubjectId = chemistry.Id, StartDate = today.AddDays(-14) };
        chemOneToOne.Students.Add(new TeachingArrangementStudent { StudentId = students[5] });
        var ieltsOneToOne = new TeachingArrangement { Type = ArrangementType.OneToOne, Title = "IELTS Writing 1:1 — Gabriel", TeacherId = teachers["lucas"], CourseId = ielts.Id, SubjectId = english.Id, StartDate = today.AddDays(-7) };
        ieltsOneToOne.Students.Add(new TeachingArrangementStudent { StudentId = students[6] });
        var physicsGroup = new TeachingArrangement { Type = ArrangementType.PrivateGroup, Title = "Physics small group", TeacherId = teachers["daniel"], CourseId = science.Id, SubjectId = physics.Id, StartDate = today.AddDays(-14) };
        physicsGroup.Students.Add(new TeachingArrangementStudent { StudentId = students[1] });
        physicsGroup.Students.Add(new TeachingArrangementStudent { StudentId = students[2] });
        db.Arrangements.AddRange(chemOneToOne, ieltsOneToOne, physicsGroup);
        db.Holidays.Add(new InstitutionHoliday { Date = today.AddDays(40), Name = "Demo public holiday" });
        await db.SaveChangesAsync(ct);

        // ---- Sessions ----
        var batchStudents = students.Take(5).ToList();
        // Batch series: Monday & Wednesday 19:00 Dubai, starting three weeks ago, 12 occurrences.
        var batchStart = today.AddDays(-21);
        var batchSeries = new SessionSeries
        {
            Title = "G10 Maths — Evening A",
            SessionType = SessionType.Batch,
            TeacherId = teachers["amira"],
            BatchId = batch.Id,
            CourseId = science.Id,
            SubjectId = maths.Id,
            TimeZoneId = dubai,
            LocalStartTime = new TimeOnly(19, 0),
            DurationMinutes = 60,
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 1,
            Days = WeekDays.Monday | WeekDays.Wednesday,
            StartDate = batchStart,
            OccurrenceCount = 12,
            DeliveryMode = DeliveryMode.Online,
        };
        db.SessionSeries.Add(batchSeries);
        var batchSessions = batchSeries.Pattern.Dates().Select(d => Session("G10 Maths — Evening A", SessionType.Batch, teachers["amira"], dubai, d, new TimeOnly(19, 0), 60,
            batchStudents, batchId: batch.Id, courseId: science.Id, subjectId: maths.Id, chapterId: quadratics.Id, seriesId: batchSeries.Id, occurrence: true)).ToList();
        db.Sessions.AddRange(batchSessions);

        // Recurring one-to-one (no batch): Saturdays 10:00 Dubai, 6 weeks from two weeks ago.
        var o2oSeries = new SessionSeries
        {
            Title = "Chemistry 1:1 — Fatima",
            SessionType = SessionType.OneToOne,
            TeacherId = teachers["priya"],
            ArrangementId = chemOneToOne.Id,
            CourseId = science.Id,
            SubjectId = chemistry.Id,
            TimeZoneId = dubai,
            LocalStartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 1,
            Days = WeekDays.Saturday,
            StartDate = today.AddDays(-14),
            OccurrenceCount = 6,
            DeliveryMode = DeliveryMode.Online,
        };
        db.SessionSeries.Add(o2oSeries);
        var o2oSessions = o2oSeries.Pattern.Dates().Select(d => Session("Chemistry 1:1 — Fatima", SessionType.OneToOne, teachers["priya"], dubai, d, new TimeOnly(10, 0), 60,
            [students[5]], arrangementId: chemOneToOne.Id, courseId: science.Id, subjectId: chemistry.Id, chapterId: atoms.Id, seriesId: o2oSeries.Id, occurrence: true)).ToList();
        db.Sessions.AddRange(o2oSessions);

        // Single one-to-one in London time, tomorrow 18:00.
        var ieltsSession = Session("IELTS Writing 1:1 — Gabriel", SessionType.OneToOne, teachers["lucas"], "Europe/London", DateOnly.FromDateTime(ZonedTime.ToLocal(now, "Europe/London")).AddDays(1),
            new TimeOnly(18, 0), 60, [students[6]], arrangementId: ieltsOneToOne.Id, courseId: ielts.Id, subjectId: english.Id, chapterId: task2.Id);
        // Ad hoc group without a batch, in three days.
        var adHoc = Session("Physics revision clinic (ad hoc)", SessionType.Group, teachers["daniel"], dubai, today.AddDays(3), new TimeOnly(17, 0), 90,
            [students[0], students[1], students[3]], courseId: science.Id, subjectId: physics.Id, chapterId: motion.Id);
        // Completed private group last week.
        var privatePast = Session("Physics small group — Motion", SessionType.PrivateGroup, teachers["daniel"], dubai, today.AddDays(-6), new TimeOnly(16, 0), 60,
            [students[1], students[2]], arrangementId: physicsGroup.Id, courseId: science.Id, subjectId: physics.Id, chapterId: motion.Id);
        db.Sessions.AddRange(ieltsSession, adHoc, privatePast);
        await db.SaveChangesAsync(ct);

        // One future batch occurrence cancelled; one future one-to-one rescheduled (history preserved).
        var futureBatch = batchSessions.Where(s => s.StartUtc > now).OrderBy(s => s.StartUtc).ToList();
        db.SessionChanges.Add(futureBatch[1].Cancel("Teacher attending a training day (demo)", admin, now));
        var futureO2o = o2oSessions.Where(s => s.StartUtc > now).OrderBy(s => s.StartUtc).ToList();
        var rescheduleTarget = futureO2o.Last();
        var newStart = rescheduleTarget.StartUtc.AddHours(2);
        db.SessionChanges.Add(rescheduleTarget.Reschedule(newStart, newStart.AddMinutes(60), dubai, "Student requested a later time (demo)", teachers["priya"], now));

        // ---- Attendance for completed sessions (group and individual) ----
        var pattern = new[] { AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Present, AttendanceStatus.Absent };
        var i = 0;
        foreach (var s in batchSessions.Concat(o2oSessions).Append(privatePast).Where(s => s.Status == SessionStatus.Completed))
            foreach (var p in s.Participants)
                db.Attendance.Add(new AttendanceRecord { SessionId = s.Id, StudentId = p.StudentId, Status = pattern[i++ % pattern.Length], MarkedAtUtc = s.EndUtc, MarkedByUserId = s.TeacherId });

        // ---- Notes & materials ----
        var lastBatch = batchSessions.Where(s => s.Status == SessionStatus.Completed).OrderBy(s => s.StartUtc).Last();
        db.Notes.Add(new SessionNote
        {
            SessionId = lastBatch.Id,
            AuthorUserId = teachers["amira"],
            Title = "Solving quadratics by factorisation",
            Status = PublicationStatus.Published,
            PublishedAtUtc = lastBatch.EndUtc,
            Visibility = NoteVisibility.Participants,
            AvailableOffline = true,
            BodyMarkdown = "## Key idea\nA quadratic $ax^2 + bx + c = 0$ can often be factorised into two brackets.\n\n| Step | Action |\n|---|---|\n| 1 | Find two numbers that multiply to *ac* and add to *b* |\n| 2 | Split the middle term |\n| 3 | Factorise in pairs |\n\n**Homework:** Worksheet 4, questions 1–10.\n\n_Demo content._",
        });
        db.Notes.Add(new SessionNote { SessionId = futureBatch[0].Id, AuthorUserId = teachers["amira"], Title = "Lesson plan: the quadratic formula (draft)", BodyMarkdown = "Draft plan — not visible to students.\n\n_Demo content._", Visibility = NoteVisibility.TeacherOnly });
        db.Materials.Add(new LearningMaterial { Title = "Quadratics practice (external link)", Kind = MaterialKind.Link, Url = "https://example.test/quadratics-practice", SubjectId = maths.Id, ChapterId = quadratics.Id, BatchId = batch.Id, CreatedByTeacherOrAdminId = teachers["amira"], Description = "Fictional demo link." });
        db.Materials.Add(new LearningMaterial { Title = "Periodic table reference (external link)", Kind = MaterialKind.Link, Url = "https://example.test/periodic-table", SessionId = o2oSessions[0].Id, CreatedByTeacherOrAdminId = teachers["priya"] });

        // ---- Assignments ----
        var worksheet = new Assignment
        {
            Title = "Worksheet 4: Factorising quadratics",
            InstructionsMarkdown = "Complete questions 1–10. Show your working.\n\n_Demo content._",
            TeacherId = teachers["amira"],
            SessionId = lastBatch.Id,
            BatchId = batch.Id,
            SubjectId = maths.Id,
            DueAtUtc = now.AddDays(3),
            MaxMarks = 20,
            AllowLateSubmission = true,
            AllowResubmission = true,
        };
        foreach (var s in batchStudents) worksheet.Recipients.Add(new AssignmentRecipient { StudentId = s });
        worksheet.Publish(now.AddDays(-2));
        var past = new Assignment
        {
            Title = "Atomic structure summary",
            InstructionsMarkdown = "Write a half-page summary of electron shells.\n\n_Demo content._",
            TeacherId = teachers["priya"],
            SessionId = o2oSessions[0].Id,
            SubjectId = chemistry.Id,
            DueAtUtc = now.AddDays(-3),
            MaxMarks = 10,
        };
        past.Recipients.Add(new AssignmentRecipient { StudentId = students[5] });
        past.Publish(now.AddDays(-10));
        db.Assignments.AddRange(worksheet, past);
        await db.SaveChangesAsync(ct);

        var graded = new AssignmentSubmission { AssignmentId = past.Id, StudentId = students[5], TextAnswer = "Electrons occupy shells around the nucleus… (demo answer)" };
        graded.Submit(past, now.AddDays(-4));
        graded.Grade(8, past.MaxMarks, "Clear summary. Add an example of 2,8,1 configuration next time.", false, teachers["priya"], now.AddDays(-2));
        var submitted = new AssignmentSubmission { AssignmentId = worksheet.Id, StudentId = students[0], TextAnswer = "1) (x+2)(x+3) … (demo answer)" };
        submitted.Submit(worksheet, now.AddHours(-5));
        db.Submissions.AddRange(graded, submitted);

        // ---- Doubts ----
        Doubt NewDoubt(Guid student, string title, string desc, Guid subject, Guid? session, Guid? teacher, DoubtStatus status) => new()
        {
            StudentId = student,
            Title = title,
            Description = desc + "\n\n(Demo doubt)",
            SubjectId = subject,
            SessionId = session,
            AssignedTeacherId = teacher,
            Status = status,
            LastActivityAtUtc = now.AddHours(-3),
            IsPrivate = true,
        };
        var answered = NewDoubt(students[0], "Why do we split the middle term?", "I don't understand why splitting bx helps factorise.", maths.Id, lastBatch.Id, teachers["amira"], DoubtStatus.Answered);
        answered.Messages.Add(new DoubtMessage { AuthorUserId = teachers["amira"], IsTeacherReply = true, Body = "Splitting lets us group terms so a common bracket appears. Try x² + 5x + 6 = x² + 2x + 3x + 6." });
        var unresolved = NewDoubt(students[5], "How many electrons fit in the third shell?", "My textbook says 8 but the video said 18.", chemistry.Id, o2oSessions[0].Id, teachers["priya"], DoubtStatus.Assigned);
        var nextChem = futureO2o.First();
        unresolved.FollowUpSessionId = nextChem.Id;
        var resolved = NewDoubt(students[1], "Velocity vs speed", "Is velocity just speed with a sign?", physics.Id, privatePast.Id, teachers["daniel"], DoubtStatus.Resolved);
        resolved.ResolvedAtUtc = now.AddDays(-2);
        var pending = NewDoubt(students[2], "Units of acceleration", "Why is it m/s²?", physics.Id, privatePast.Id, teachers["daniel"], DoubtStatus.PendingConfirmation);
        pending.DiscussedAtUtc = now.AddDays(-1);
        db.Doubts.AddRange(answered, unresolved, resolved, pending);
        await db.SaveChangesAsync(ct);
        db.AgendaItems.Add(new SessionAgendaItem { SessionId = nextChem.Id, Kind = AgendaItemKind.Doubt, DoubtId = unresolved.Id, Title = $"Doubt: {unresolved.Title}", SortOrder = 1 });
        db.DoubtStatusChanges.Add(new DoubtStatusChange { DoubtId = resolved.Id, FromStatus = DoubtStatus.Answered, ToStatus = DoubtStatus.Resolved, ChangedByUserId = students[1], ChangedAtUtc = now.AddDays(-2) });

        // ---- Question bank & exam ----
        var q1 = new Question { SubjectId = maths.Id, ChapterId = quadratics.Id, Type = QuestionType.SingleChoice, Stem = "What are the roots of x² − 5x + 6 = 0?", DefaultMarks = 2, Explanation = "(x−2)(x−3)=0" };
        q1.Options.AddRange([new QuestionOption { Text = "2 and 3", IsCorrect = true, SortOrder = 0 }, new QuestionOption { Text = "−2 and −3", SortOrder = 1 }, new QuestionOption { Text = "1 and 6", SortOrder = 2 }]);
        var q2 = new Question { SubjectId = maths.Id, ChapterId = quadratics.Id, Type = QuestionType.TrueFalse, Stem = "Every quadratic equation has two distinct real roots.", DefaultMarks = 1, Explanation = "The discriminant can be zero or negative." };
        q2.Options.AddRange([new QuestionOption { Text = "True", SortOrder = 0 }, new QuestionOption { Text = "False", IsCorrect = true, SortOrder = 1 }]);
        var q3 = new Question { SubjectId = maths.Id, ChapterId = quadratics.Id, Type = QuestionType.Numerical, Stem = "Evaluate the discriminant of x² + 4x + 1.", DefaultMarks = 2, NumericAnswer = 12, NumericTolerance = 0 };
        var q4 = new Question { SubjectId = maths.Id, ChapterId = quadratics.Id, Type = QuestionType.MultipleChoice, Stem = "Which of these are quadratic expressions?", DefaultMarks = 2 };
        q4.Options.AddRange([new QuestionOption { Text = "x² + 1", IsCorrect = true, SortOrder = 0 }, new QuestionOption { Text = "3x + 2", SortOrder = 1 }, new QuestionOption { Text = "2x² − x", IsCorrect = true, SortOrder = 2 }]);
        var q5 = new Question { SubjectId = maths.Id, ChapterId = quadratics.Id, Type = QuestionType.LongAnswer, Stem = "Explain, in your own words, when the quadratic formula is more useful than factorising.", DefaultMarks = 3 };
        foreach (var q in new[] { q1, q2, q3, q4, q5 }) q.Validate();
        db.Questions.AddRange(q1, q2, q3, q4, q5);
        var exam = new Exam
        {
            Title = "Quadratics checkpoint quiz",
            Instructions = "30 minutes. Demo exam.",
            TeacherId = teachers["amira"],
            SubjectId = maths.Id,
            BatchId = batch.Id,
            OpensAtUtc = now.AddDays(-1),
            ClosesAtUtc = now.AddDays(7),
            DurationMinutes = 30,
            MaxAttempts = 1,
            Status = ExamStatus.Published,
        };
        var order = 0;
        foreach (var q in new[] { q1, q2, q3, q4, q5 }) exam.Questions.Add(new ExamQuestion { QuestionId = q.Id, Marks = q.DefaultMarks, SortOrder = order++ });
        foreach (var s in batchStudents) exam.Recipients.Add(new ExamRecipient { StudentId = s });
        exam.ValidateForPublish();
        db.Exams.Add(exam);
        await db.SaveChangesAsync(ct);

        // One completed attempt awaiting manual grading of the written answer.
        var attempt = new ExamAttempt
        {
            ExamId = exam.Id,
            StudentId = students[1],
            AttemptNumber = 1,
            StartedAtUtc = now.AddHours(-20),
            DeadlineUtc = now.AddHours(-19.5),
            SubmittedAtUtc = now.AddHours(-19.6),
            QuestionOrder = string.Join(',', exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.Id)),
            Status = AttemptStatus.Submitted,
            RequiresManualGrading = true,
        };
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var eqs = exam.Questions.OrderBy(q => q.SortOrder).ToList();
        ExamAnswer Ans(int idx, ExamResponse r, Question q)
        {
            var g = QuestionGrader.Grade(q, r, eqs[idx].Marks);
            return new ExamAnswer { ExamQuestionId = eqs[idx].Id, ResponseJson = JsonSerializer.Serialize(r, json), IsCorrect = g.IsCorrect, AwardedMarks = g.Awarded };
        }
        attempt.Answers.Add(Ans(0, new ExamResponse(OptionIds: [q1.Options[0].Id]), q1));
        attempt.Answers.Add(Ans(1, new ExamResponse(OptionIds: [q2.Options[0].Id]), q2));
        attempt.Answers.Add(Ans(2, new ExamResponse(Number: 12), q3));
        attempt.Answers.Add(Ans(3, new ExamResponse(OptionIds: [q4.Options[0].Id, q4.Options[2].Id]), q4));
        attempt.Answers.Add(Ans(4, new ExamResponse(Text: "When the numbers do not factorise neatly (demo answer)."), q5));
        db.ExamAttempts.Add(attempt);

        // ---- Communication ----
        db.Announcements.Add(new Announcement { Title = "Welcome to the new term (demo)", Body = "Classes resume next week. Check your calendar for your timetable.", Audience = AnnouncementAudience.Everyone, AuthorUserId = admin, PublishedAtUtc = now.AddDays(-3) });
        var convo = new Conversation { Type = ConversationType.Direct, DirectKey = Conversation.BuildDirectKey(teachers["amira"], students[0]), LastMessageAtUtc = now.AddHours(-6) };
        convo.Participants.Add(new ConversationParticipant { UserId = teachers["amira"], LastReadAtUtc = now.AddHours(-6) });
        convo.Participants.Add(new ConversationParticipant { UserId = students[0] });
        db.Conversations.Add(convo);
        db.Messages.Add(new Message { ConversationId = convo.Id, SenderUserId = teachers["amira"], Body = "Great progress today, Aisha. Remember worksheet 4 is due on Friday. (demo)", CreatedAtUtc = now.AddHours(-6) });
        foreach (var s in batchStudents)
            db.Notifications.Add(new Notification { TenantId = t.Id, UserId = s, Type = NotificationType.AssignmentPublished, Title = $"New assignment: {worksheet.Title}", Body = "Due in 3 days.", Link = $"/assignments/{worksheet.Id}", DedupeKey = $"assignment-published:{worksheet.Id}", CreatedAtUtc = now.AddDays(-2) });
        db.Notifications.Add(new Notification { TenantId = t.Id, UserId = students[5], Type = NotificationType.DoubtFollowUp, Title = "Your open doubt will be discussed", Body = $"'{unresolved.Title}' is on the agenda for your next chemistry lesson.", Link = $"/doubts/{unresolved.Id}", DedupeKey = $"doubt-followup:{unresolved.Id}:{nextChem.Id}", CreatedAtUtc = now.AddHours(-1) });

        // ---- AI Class Assistant demo (clearly labelled; not real model output) ----
        var aiSessions = batchSessions.Where(s => s.Status == SessionStatus.Completed).OrderByDescending(s => s.StartUtc).Take(2).ToList();
        foreach (var s in aiSessions)
            foreach (var p in s.Participants)
                foreach (var type in Enum.GetValues<ConsentType>())
                    db.Consents.Add(new ConsentRecord { SessionId = s.Id, StudentId = p.StudentId, Type = type, Granted = true, GivenByUserId = admin, GivenByRole = "InstitutionAdmin", RecordedAtUtc = s.StartUtc.AddMinutes(-30), Method = "Signed demo consent form" });
        db.Recordings.Add(new ClassRecording { SessionId = aiSessions[0].Id, Title = "Demo recording placeholder (no media file)", Source = RecordingSource.MeetingProvider, Status = RecordingStatus.Pending, RetainUntilUtc = now.AddDays(365), UploadedByUserId = teachers["amira"] });
        var transcriptText = "WEBVTT\n\n00:00:05.000 --> 00:00:12.000\n<v Teacher>Today we are factorising quadratics. (Demo transcript)\n\n00:00:12.500 --> 00:00:20.000\n<v Teacher>For x squared plus five x plus six, find two numbers that multiply to six and add to five.\n\n00:00:21.000 --> 00:00:24.000\n<v Student>Is it two and three?\n\n00:00:24.500 --> 00:00:30.000\n<v Teacher>Yes. So it factorises to x plus two times x plus three. For homework, do worksheet four.\n";
        foreach (var (s, idx) in aiSessions.Select((s, idx) => (s, idx)))
        {
            var transcript = new ClassTranscript { SessionId = s.Id, Source = TranscriptSource.Upload, Language = "en", FullText = "Demo transcript" };
            var seq = 0;
            foreach (var p in TranscriptParser.Parse(transcriptText))
                transcript.Segments.Add(new TranscriptSegment { Sequence = seq++, StartSeconds = p.Start, EndSeconds = p.End, Speaker = p.Speaker, Text = p.Text, Confidence = 0.95 });
            db.Transcripts.Add(transcript);
            var content = new LessonDraftContent(
                "[Demo content — not generated by an AI model] The class practised factorising quadratics by finding two numbers that multiply to c and add to b.",
                ["Factorise simple quadratics of the form x² + bx + c"],
                [new TopicNote("Factorisation", "Find two numbers that multiply to c and add to b, then write two brackets.", [new SourceRef(12.5, "find two numbers that multiply to six and add to five")])],
                [new DefinitionItem("Quadratic", "A polynomial of degree two.", "ax² + bx + c = 0", null)],
                [new WorkedExample("x² + 5x + 6", ["2 × 3 = 6 and 2 + 3 = 5", "(x + 2)(x + 3)"], new SourceRef(24.5, "x plus two times x plus three"))],
                [new QaItem("Is it two and three?", "Yes.", "a student", new SourceRef(21, "Is it two and three?"))],
                [],
                [new HomeworkItem("Worksheet four", null, 0.9, new SourceRef(24.5, "For homework, do worksheet four."))],
                "Factorise by finding a pair of numbers with the right product and sum.",
                [new PracticeQuestion("short_answer", "Factorise x² + 7x + 12.", null, "(x + 3)(x + 4)", "3 × 4 = 12 and 3 + 4 = 7")],
                [new Flashcard("Factorising x² + bx + c", "Find p, q with pq = c and p + q = b")],
                ["Introduce the quadratic formula"],
                [new TimestampLink(5, "Introduction"), new TimestampLink(12.5, "Worked example")]);
            var contentJson = JsonSerializer.Serialize(content, json);
            var draft = new AiLessonDraft { SessionId = s.Id, TranscriptId = transcript.Id, ContentJson = contentJson, Provider = "demo-seed", Model = "none", IsDemo = true, Status = AiDraftStatus.AwaitingTeacherReview };
            draft.Versions.Add(new AiLessonDraftVersion { VersionNumber = 1, ContentJson = contentJson, ChangeNote = "Demo seed (not real AI output)" });
            if (idx == 1) draft.Approve(teachers["amira"], now.AddDays(-1));
            db.AiDrafts.Add(draft);
        }
        await db.SaveChangesAsync(ct);
    }
}
