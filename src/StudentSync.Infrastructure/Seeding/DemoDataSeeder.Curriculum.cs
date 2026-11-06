using System.Text.Json;
using StudentSync.Domain.Assessments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Infrastructure.Seeding;

/// <summary>
/// Demo CBSE / NCERT curriculum for Horizon (Development only): Classes 1–12 with NCERT core subjects, configurable language
/// choice groups (no language rules are hard-coded in the product), Class 10 NCERT chapters, a language teacher, the demo
/// students' enrolment and language choices, and a fictional hourly Class 10 timetable.
/// The catalogue part is idempotent on every seed (matched by code/title, only missing rows added). The one-off part — moving
/// the existing demo students onto the curriculum and creating the timetable — runs once, when the Class 10 English subject
/// does not exist yet, so later seeds never duplicate sessions or undo an admin's changes.
/// </summary>
public sealed partial class DemoDataSeeder
{
    private const string Dubai = "Asia/Dubai";
    public const string SecondLanguage = "Second language";
    public const string ThirdLanguage = "Third language";
    public const string OptionalLanguage = "Language (optional)";
    public const string ElectiveGroup = "Elective";
    public const string Class10Course = "CBSE-C10";
    public const string LanguageTeacherEmail = "meera@horizon.example.test";
    /// <summary>Marker subject: the one-off curriculum migration has run once it exists.</summary>
    private const string CurriculumMarkerCode = "C10-ENG";

    private static readonly (string Name, string Code)[] Languages = [("Hindi", "HIN"), ("Malayalam", "MAL"), ("Arabic", "ARA"), ("French", "FRE")];

    private sealed record SubjectSpec(string Code, string Name, SubjectKind Kind, string? Group);
    private sealed record CourseSpec(int Standard, string Code, string Name, string? LegacyCode, List<SubjectSpec> Subjects);

    private static List<CourseSpec> CbseCatalogue()
    {
        static List<SubjectSpec> LanguageGroup(string prefix, string tag, string group) =>
            Languages.Select(l => new SubjectSpec($"{prefix}-{tag}-{l.Code}", l.Name, SubjectKind.LanguageOption, group)).ToList();

        var list = new List<CourseSpec>();
        for (var n = 1; n <= 10; n++)
        {
            var p = $"C{n}";
            var subjects = new List<SubjectSpec>
            {
                new($"{p}-ENG", "English", SubjectKind.Core, null),
                // The existing demo maths subject (code MATH10, with its classes and chapters) is Class 10 Mathematics.
                new(n == 10 ? "MATH10" : $"{p}-MATH", "Mathematics", SubjectKind.Core, null),
            };
            if (n is >= 3 and <= 5) subjects.Add(new($"{p}-EVS", "The World Around Us (EVS)", SubjectKind.Core, null));
            if (n >= 6)
            {
                subjects.Add(new($"{p}-SCI", "Science", SubjectKind.Core, null));
                subjects.Add(new($"{p}-SST", "Social Science", SubjectKind.Core, null));
            }
            subjects.AddRange(LanguageGroup(p, "L2", SecondLanguage));
            if (n >= 6) subjects.AddRange(LanguageGroup(p, "L3", ThirdLanguage));
            list.Add(new CourseSpec(n, $"CBSE-C{n}", $"Class {n} (CBSE)", n == 10 ? "G10-SM" : null, subjects));
        }
        foreach (var n in new[] { 11, 12 })
        {
            foreach (var (letter, stream, tag) in new[] { ("S", "Science", "SCI"), ("C", "Commerce", "COM"), ("H", "Humanities", "HUM") })
            {
                var p = $"C{n}{letter}";
                var subjects = new List<SubjectSpec> { new($"{p}-ENG", "English Core", SubjectKind.Core, null) };
                subjects.AddRange(stream switch
                {
                    "Science" =>
                    [
                        new($"{p}-PHY", "Physics", SubjectKind.Core, null), new($"{p}-CHEM", "Chemistry", SubjectKind.Core, null),
                        new($"{p}-MATH", "Mathematics", SubjectKind.Core, null), new($"{p}-BIO", "Biology", SubjectKind.Core, null),
                    ],
                    "Commerce" =>
                    [
                        new($"{p}-ACC", "Accountancy", SubjectKind.Core, null), new($"{p}-BST", "Business Studies", SubjectKind.Core, null),
                        new($"{p}-ECO", "Economics", SubjectKind.Core, null),
                        // Electives are assigned per student by an admin.
                        new($"{p}-MATH", "Mathematics", SubjectKind.Additional, ElectiveGroup),
                        new($"{p}-AMATH", "Applied Mathematics", SubjectKind.Additional, ElectiveGroup),
                        new($"{p}-IP", "Informatics Practices", SubjectKind.Additional, ElectiveGroup),
                    ],
                    _ =>
                    [
                        new($"{p}-HIST", "History", SubjectKind.Core, null), new($"{p}-POL", "Political Science", SubjectKind.Core, null),
                        new($"{p}-GEO", "Geography", SubjectKind.Core, null), new($"{p}-ECO", "Economics", SubjectKind.Core, null),
                        new($"{p}-PSY", "Psychology", SubjectKind.Additional, ElectiveGroup),
                    ],
                });
                subjects.AddRange(LanguageGroup(p, "LO", OptionalLanguage));
                list.Add(new CourseSpec(n, $"CBSE-C{n}-{tag}", $"Class {n} {stream} (CBSE)", null, subjects));
            }
        }
        return list;
    }

    /// <summary>Chapter titles of the current (rationalised) NCERT Class 10 textbooks, in book order. Titles only.</summary>
    private static readonly (string SubjectCode, string[] Chapters)[] Class10Chapters =
    [
        ("MATH10",
        [
            "Real Numbers", "Polynomials", "Pair of Linear Equations in Two Variables", "Quadratic Equations", "Arithmetic Progressions",
            "Triangles", "Coordinate Geometry", "Introduction to Trigonometry", "Some Applications of Trigonometry", "Circles",
            "Areas Related to Circles", "Surface Areas and Volumes", "Statistics", "Probability",
        ]),
        ("C10-SCI",
        [
            "Chemical Reactions and Equations", "Acids, Bases and Salts", "Metals and Non-metals", "Carbon and its Compounds", "Life Processes",
            "Control and Coordination", "How do Organisms Reproduce?", "Heredity", "Light – Reflection and Refraction",
            "The Human Eye and the Colourful World", "Electricity", "Magnetic Effects of Electric Current", "Our Environment",
        ]),
        // First Flight (prose).
        ("C10-ENG",
        [
            "A Letter to God", "Nelson Mandela: Long Walk to Freedom", "Two Stories about Flying", "From the Diary of Anne Frank",
            "Glimpses of India", "Mijbil the Otter", "Madam Rides the Bus", "The Sermon at Benares", "The Proposal",
        ]),
        // History, Geography, Political Science and Economics (a selection of ten chapters).
        ("C10-SST",
        [
            "The Rise of Nationalism in Europe", "Nationalism in India", "Resources and Development", "Water Resources", "Agriculture",
            "Power Sharing", "Federalism", "Development", "Sectors of the Indian Economy", "Money and Credit",
        ]),
    ];

    /// <summary>Earlier demo chapter titles mapped onto the NCERT title (the chapter id is kept).</summary>
    private static readonly (string SubjectCode, string From, string To)[] ChapterRenames = [("MATH10", "Trigonometry", "Introduction to Trigonometry")];

    /// <summary>Earlier fictional demo chapters that are not NCERT chapters: removed when nothing references them, otherwise kept last.</summary>
    private static readonly (string SubjectCode, string Title)[] LegacyChapters = [("MATH10", "Algebra basics"), ("MATH10", "Functions and graphs")];

    /// <summary>Existing demo subjects that become extra coaching (Additional) subjects.</summary>
    private static readonly string[] ExtraCoachingCodes = ["PHYS10", "CHEM10", "IELTS-W"];

    /// <summary>Fictional language choices per demo student: (second language, third language).</summary>
    private static readonly (string Email, string Second, string Third)[] DemoLanguageChoices =
    [
        ("aisha@horizon.example.test", "HIN", "FRE"), ("ben@horizon.example.test", "MAL", "HIN"), ("chloe@horizon.example.test", "HIN", "MAL"),
        ("dev@horizon.example.test", "MAL", "ARA"), ("ethan@horizon.example.test", "HIN", "FRE"), ("fatima@horizon.example.test", "ARA", "HIN"),
        ("gabriel@horizon.example.test", "FRE", "HIN"), ("hiro@horizon.example.test", "ARA", "MAL"),
    ];

    /// <summary>
    /// Ensures the CBSE catalogue for the Horizon demo institution and, once, moves the demo onto it. Returns the number of
    /// changes made (0 when everything was already in place).
    /// </summary>
    public async Task<int> EnsureCurriculumAsync(CancellationToken ct)
    {
        var tenantId = await db.Tenants.Where(t => t.Slug == PrimarySlug && t.IsDemo).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (tenantId is not { } tid) return 0;
        using var _ = tenantContext.BeginScope(tid);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var firstRun = !await db.Subjects.AnyAsync(s => s.Code == CurriculumMarkerCode, ct);
        var changes = 0;
        var catalogue = CbseCatalogue();

        // ---- Grades: "Class 1" … "Class 12" (earlier demo "Grade n" rows are renamed, keeping their ids) ----
        var grades = await db.GradeLevels.ToListAsync(ct);
        var gradeIds = new Dictionary<int, Guid>();
        for (var n = 1; n <= 12; n++)
        {
            var name = $"Class {n}";
            var g = grades.FirstOrDefault(x => x.Name == name) ?? grades.FirstOrDefault(x => x.Name == $"Grade {n}");
            if (g is null) { g = new GradeLevel { Name = name, SortOrder = n }; db.GradeLevels.Add(g); changes++; }
            else if (g.Name != name || g.SortOrder != n) { g.Name = name; g.SortOrder = n; changes++; }
            gradeIds[n] = g.Id;
        }
        await db.SaveChangesAsync(ct);

        // ---- Courses: one per standard, one per stream for 11 and 12 ----
        var yearId = await db.AcademicYears.Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct);
        var courses = await db.Courses.ToListAsync(ct);
        var courseIds = new Dictionary<string, Guid>();
        foreach (var spec in catalogue)
        {
            var c = courses.FirstOrDefault(x => x.Code == spec.Code) ?? (spec.LegacyCode is { } legacy ? courses.FirstOrDefault(x => x.Code == legacy) : null);
            if (c is null)
            {
                c = new Course { Code = spec.Code, Name = spec.Name, GradeLevelId = gradeIds[spec.Standard], AcademicYearId = yearId, Description = "CBSE / NCERT curriculum (demo)." };
                db.Courses.Add(c);
                changes++;
            }
            else if (c.Code != spec.Code || c.Name != spec.Name || c.GradeLevelId != gradeIds[spec.Standard])
            {
                c.Code = spec.Code; c.Name = spec.Name; c.GradeLevelId = gradeIds[spec.Standard];
                c.Description = "CBSE / NCERT curriculum (demo).";
                changes++;
            }
            courseIds[spec.Code] = c.Id;
        }
        await db.SaveChangesAsync(ct);

        // ---- Subjects (matched by code; codes are unique per institution) ----
        var subjects = await db.Subjects.Include(s => s.Chapters).ToListAsync(ct);
        foreach (var spec in catalogue)
        {
            foreach (var ss in spec.Subjects)
            {
                var s = subjects.FirstOrDefault(x => x.Code == ss.Code);
                if (s is null)
                {
                    s = new Subject { CourseId = courseIds[spec.Code], Code = ss.Code, Name = ss.Name };
                    s.SetKind(ss.Kind, ss.Group);
                    db.Subjects.Add(s);
                    subjects.Add(s);
                    changes++;
                }
                else if (s.CourseId != courseIds[spec.Code] || s.Name != ss.Name || s.Kind != ss.Kind || s.ChoiceGroup != ss.Group)
                {
                    s.CourseId = courseIds[spec.Code]; s.Name = ss.Name;
                    s.SetKind(ss.Kind, ss.Group);
                    changes++;
                }
            }
        }
        foreach (var s in subjects.Where(x => ExtraCoachingCodes.Contains(x.Code) && x.Kind != SubjectKind.Additional))
        {
            s.SetKind(SubjectKind.Additional, null);
            changes++;
        }
        await db.SaveChangesAsync(ct);

        // ---- Class 10 NCERT chapters (matched by title; existing chapter ids are kept) ----
        changes += await EnsureClass10ChaptersAsync(tid, subjects, ct);

        // ---- Language teacher and subject teachers ----
        var (meeraId, created) = await EnsureLanguageTeacherAsync(tid, ct);
        if (created) changes++;
        var teacherIds = await TeacherIdsAsync(tid, ct);
        teacherIds["meera"] = meeraId;
        var assignments = new List<(string Teacher, string Code)>
        {
            ("lucas", "C10-ENG"), ("amira", "MATH10"), ("daniel", "C10-SCI"), ("priya", "C10-SST"),
        };
        assignments.AddRange(Languages.SelectMany(l => new[] { ("meera", $"C10-L2-{l.Code}"), ("meera", $"C10-L3-{l.Code}") }));
        var existingAssignments = await db.TeacherSubjects.Select(t => new { t.TeacherId, t.SubjectId }).ToListAsync(ct);
        foreach (var (teacher, code) in assignments)
        {
            if (!teacherIds.TryGetValue(teacher, out var teacherId)) continue;
            var subjectId = subjects.Single(s => s.Code == code).Id;
            if (existingAssignments.Any(a => a.TeacherId == teacherId && a.SubjectId == subjectId)) continue;
            db.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacherId, SubjectId = subjectId });
            changes++;
        }
        await db.SaveChangesAsync(ct);

        if (firstRun)
        {
            changes += await MoveDemoStudentsOntoCurriculumAsync(tid, courseIds[Class10Course], subjects, ct);
            changes += await SeedClass10TimetableAsync(courseIds[Class10Course], subjects, teacherIds, ct);
        }
        changes += await EnsureLearningProfilesAsync(tid, subjects, teacherIds, ct);
        changes += await EnsureDemoDiagnosticAsync(tid, subjects, teacherIds, ct);
        await tx.CommitAsync(ct);
        if (changes > 0) logger.LogInformation("Demo CBSE curriculum: {Changes} change(s){FirstRun}.", changes, firstRun ? " (first run: students and timetable)" : "");
        return changes;
    }

    private async Task<int> EnsureClass10ChaptersAsync(Guid tid, List<Subject> subjects, CancellationToken ct)
    {
        var changes = 0;
        foreach (var (code, titles) in Class10Chapters)
        {
            var subject = subjects.Single(s => s.Code == code);
            var chapters = subject.Chapters;
            Chapter? Find(string title) => chapters.FirstOrDefault(c => string.Equals(c.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
            foreach (var (_, from, to) in ChapterRenames.Where(r => r.SubjectCode == code))
            {
                if (Find(to) is null && Find(from) is { } old) { old.Title = to; changes++; }
            }
            for (var i = 0; i < titles.Length; i++)
            {
                var existing = Find(titles[i]);
                if (existing is null)
                {
                    var chapter = new Chapter { TenantId = tid, SubjectId = subject.Id, Title = titles[i], SortOrder = i + 1 };
                    db.Chapters.Add(chapter); // EF relationship fix-up also adds it to subject.Chapters
                    changes++;
                }
                else if (existing.SortOrder != i + 1) { existing.SortOrder = i + 1; changes++; }
            }
            var next = titles.Length + 1;
            foreach (var (_, title) in LegacyChapters.Where(l => l.SubjectCode == code))
            {
                if (Find(title) is not { } legacy) continue;
                var id = legacy.Id;
                var referenced = await db.Sessions.AnyAsync(s => s.ChapterId == id, ct) || await db.Materials.AnyAsync(m => m.ChapterId == id, ct)
                    || await db.Questions.AnyAsync(q => q.ChapterId == id, ct) || await db.Doubts.AnyAsync(d => d.ChapterId == id, ct)
                    || await db.Topics.AnyAsync(t => t.ChapterId == id, ct);
                if (!referenced) { db.Chapters.Remove(legacy); chapters.Remove(legacy); changes++; }
                else if (legacy.SortOrder != next) { legacy.SortOrder = next; changes++; }
                next++;
            }
        }
        await db.SaveChangesAsync(ct);
        return changes;
    }

    private async Task<(Guid Id, bool Created)> EnsureLanguageTeacherAsync(Guid tid, CancellationToken ct)
    {
        var normalized = LanguageTeacherEmail.ToUpperInvariant();
        var existing = await db.Users.Where(u => u.TenantId == tid && u.NormalizedEmail == normalized).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (existing is { } id) return (id, false);
        var userId = await CreateUserAsync(tid, LanguageTeacherEmail, "Meera Nair (Demo Teacher)", UserRole.Teacher, Dubai, ct);
        var code = await db.Teachers.AnyAsync(t => t.EmployeeCode == "HZ-T05", ct) ? null : "HZ-T05";
        db.Teachers.Add(new Teacher { Id = userId, EmployeeCode = code, Specialisations = "Hindi, Malayalam, Arabic, French", Bio = "Fictional demo teacher profile." });
        await db.SaveChangesAsync(ct);
        return (userId, true);
    }

    private async Task<Dictionary<string, Guid>> TeacherIdsAsync(Guid tid, CancellationToken ct)
    {
        var result = new Dictionary<string, Guid>();
        foreach (var key in new[] { "amira", "daniel", "priya", "lucas" })
        {
            var normalized = $"{key}@horizon.example.test".ToUpperInvariant();
            var id = await db.Users.Where(u => u.TenantId == tid && u.NormalizedEmail == normalized && u.IsDemo).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
            if (id is { } teacherId && await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) result[key] = teacherId;
        }
        return result;
    }

    /// <summary>Enrols every demo student in Class 10 (CBSE), gives them language choices and keeps their extra coaching subjects.</summary>
    private async Task<int> MoveDemoStudentsOntoCurriculumAsync(Guid tid, Guid class10Id, List<Subject> subjects, CancellationToken ct)
    {
        var changes = 0;
        var adminEmail = "ADMIN@HORIZON.EXAMPLE.TEST";
        var adminId = await db.Users.Where(u => u.TenantId == tid && u.NormalizedEmail == adminEmail).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        var emails = DemoLanguageChoices.Select(c => c.Email.ToUpperInvariant()).ToList();
        var students = await db.Users.Where(u => u.TenantId == tid && u.IsDemo && u.Role == UserRole.Student && emails.Contains(u.NormalizedEmail!))
            .Select(u => new { u.Id, u.NormalizedEmail }).ToListAsync(ct);
        var assigned = await db.StudentSubjects.Select(x => new { x.StudentId, x.SubjectId }).ToListAsync(ct);
        void Assign(Guid studentId, Guid subjectId)
        {
            if (assigned.Any(a => a.StudentId == studentId && a.SubjectId == subjectId)) return;
            db.StudentSubjects.Add(new StudentSubject { StudentId = studentId, SubjectId = subjectId, AddedByUserId = adminId });
            assigned.Add(new { StudentId = studentId, SubjectId = subjectId });
            changes++;
        }

        foreach (var (email, second, third) in DemoLanguageChoices)
        {
            var student = students.FirstOrDefault(s => s.NormalizedEmail == email.ToUpperInvariant());
            if (student is null) continue;
            var enrolment = await db.CourseEnrolments.FirstOrDefaultAsync(e => e.CourseId == class10Id && e.StudentId == student.Id, ct);
            if (enrolment is null)
            {
                db.CourseEnrolments.Add(new CourseEnrolment { CourseId = class10Id, StudentId = student.Id, EnrolledAtUtc = clock.UtcNow.AddDays(-60) });
                changes++;
            }
            // Only fill a group the student has no choice in yet.
            foreach (var (tag, code) in new[] { ("L2", second), ("L3", third) })
            {
                var group = subjects.Where(s => s.Code.StartsWith($"C10-{tag}-", StringComparison.Ordinal)).Select(s => s.Id).ToList();
                if (assigned.Any(a => a.StudentId == student.Id && group.Contains(a.SubjectId))) continue;
                Assign(student.Id, subjects.Single(s => s.Code == $"C10-{tag}-{code}").Id);
            }
        }

        // Extra coaching subjects go to exactly the students who already have classes in them.
        foreach (var code in ExtraCoachingCodes)
        {
            var subject = subjects.FirstOrDefault(s => s.Code == code);
            if (subject is null) continue;
            var subjectId = subject.Id;
            var learners = await db.SessionParticipants.Where(p => p.Status == ParticipantStatus.Active && p.Session!.SubjectId == subjectId)
                .Select(p => p.StudentId).Distinct().ToListAsync(ct);
            foreach (var studentId in learners) Assign(studentId, subjectId);
        }
        await db.SaveChangesAsync(ct);
        return changes;
    }

    private sealed record TimetableSlot(string Title, string Teacher, string SubjectCode, TimeOnly Start, WeekDays Days, bool Section, string? LanguageCode, int ClassesPerChapter);

    /// <summary>
    /// A fictional hourly Class 10 timetable (Asia/Dubai, Mon–Fri) for the Class 10 section, from two weeks ago to four weeks
    /// ahead. Occurrences that would overlap an existing class of the same teacher or student are skipped, as the scheduling
    /// service's conflict rule would require.
    /// </summary>
    private async Task<int> SeedClass10TimetableAsync(Guid class10Id, List<Subject> subjects, Dictionary<string, Guid> teachers, CancellationToken ct)
    {
        var maths = await db.Batches.Include(b => b.Enrolments).FirstOrDefaultAsync(b => b.Code == "G10-MATH-A", ct);
        if (maths is null || !teachers.ContainsKey("lucas")) return 0;
        var sectionStudents = maths.Enrolments.Where(e => e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).ToList();
        if (sectionStudents.Count == 0) return 0;

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(now, Dubai));
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var (from, to) = (monday.AddDays(-14), monday.AddDays(27));
        var changes = 0;

        var section = await db.Batches.Include(b => b.Enrolments).FirstOrDefaultAsync(b => b.Code == "C10-A", ct);
        if (section is null)
        {
            section = new Batch { Code = "C10-A", Name = "Class 10 — Section A", CourseId = class10Id, TeacherId = teachers["lucas"], Capacity = 40, StartDate = from };
            foreach (var s in sectionStudents) section.Enrolments.Add(new BatchEnrolment { StudentId = s, JoinedAtUtc = now.AddDays(-21) });
            db.Batches.Add(section);
            await db.SaveChangesAsync(ct);
            changes++;
        }

        var weekdays = WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday;
        TimetableSlot[] slots =
        [
            new("Class 10 — English", "lucas", "C10-ENG", new TimeOnly(14, 30), weekdays, true, null, 6),
            new("Class 10 — Mathematics", "amira", "MATH10", new TimeOnly(15, 30), weekdays, true, null, 6),
            new("Class 10 — Science", "daniel", "C10-SCI", new TimeOnly(16, 30), weekdays, true, null, 6),
            new("Class 10 — Social Science", "priya", "C10-SST", new TimeOnly(17, 30), WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, true, null, 4),
            // Second-language groups alternate with Social Science; one language teacher teaches each group on its own day.
            new("Class 10 — Hindi (Second language)", "meera", "C10-L2-HIN", new TimeOnly(17, 30), WeekDays.Tuesday, false, "HIN", 0),
            new("Class 10 — Malayalam (Second language)", "meera", "C10-L2-MAL", new TimeOnly(17, 30), WeekDays.Thursday, false, "MAL", 0),
        ];
        var holidays = (await db.Holidays.Where(h => h.Date >= from && h.Date <= to).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var created = new Dictionary<string, List<LearningSession>>();
        var skipped = 0;

        foreach (var slot in slots)
        {
            if (!teachers.TryGetValue(slot.Teacher, out var teacherId)) continue;
            if (await db.SessionSeries.AnyAsync(x => x.Title == slot.Title && x.TeacherId == teacherId, ct)) continue;
            var subject = subjects.Single(s => s.Code == slot.SubjectCode);
            List<Guid> students;
            if (slot.Section) students = sectionStudents;
            else
            {
                var subjectId = subject.Id;
                students = await db.StudentSubjects.Where(x => x.SubjectId == subjectId && sectionStudents.Contains(x.StudentId)).Select(x => x.StudentId).ToListAsync(ct);
            }
            if (students.Count == 0) continue;
            var type = slot.Section ? SessionType.Batch : SessionType.Group;
            var series = new SessionSeries
            {
                Title = slot.Title,
                SessionType = type,
                TeacherId = teacherId,
                BatchId = slot.Section ? section.Id : null,
                CourseId = class10Id,
                SubjectId = subject.Id,
                TimeZoneId = Dubai,
                LocalStartTime = slot.Start,
                DurationMinutes = 60,
                Frequency = RecurrenceFrequency.Weekly,
                Interval = 1,
                Days = slot.Days,
                StartDate = from,
                EndDate = to,
                SkipHolidays = true,
                DeliveryMode = DeliveryMode.Online,
            };
            db.SessionSeries.Add(series);
            var chapters = subject.Chapters.DistinctBy(c => c.Id).OrderBy(c => c.SortOrder).ToList();
            var list = new List<LearningSession>();
            foreach (var date in series.Pattern.Dates().Where(d => !holidays.Contains(d)))
            {
                var startUtc = ZonedTime.ToUtc(date, slot.Start, Dubai).Utc;
                var endUtc = startUtc.AddMinutes(60);
                var clash = await db.Sessions.AnyAsync(s => s.Status != SessionStatus.Cancelled && s.StartUtc < endUtc && s.EndUtc > startUtc
                    && (s.TeacherId == teacherId || s.Participants.Any(p => p.Status == ParticipantStatus.Active && students.Contains(p.StudentId))), ct);
                if (clash) { skipped++; continue; }
                Guid? chapterId = slot.ClassesPerChapter > 0 && chapters.Count > 0 ? chapters[Math.Min(list.Count / slot.ClassesPerChapter, chapters.Count - 1)].Id : null;
                var session = Session(slot.Title, type, teacherId, Dubai, date, slot.Start, 60, students,
                    batchId: series.BatchId, courseId: class10Id, subjectId: subject.Id, chapterId: chapterId, seriesId: series.Id, occurrence: true);
                session.Objectives = "Fictional demo timetable class.";
                db.Sessions.Add(session);
                list.Add(session);
            }
            var pattern = new[] { AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Absent };
            var i = 0;
            foreach (var s in list.Where(s => s.Status == SessionStatus.Completed))
                foreach (var p in s.Participants)
                    db.Attendance.Add(new AttendanceRecord { SessionId = s.Id, StudentId = p.StudentId, Status = pattern[i++ % pattern.Length], MarkedAtUtc = s.EndUtc, MarkedByUserId = teacherId });
            await db.SaveChangesAsync(ct);
            created[slot.SubjectCode] = list;
            changes += list.Count + 1;
        }

        // Content for the subject pages: a published note and an unlocked pre-recorded video on recent English and Science classes.
        foreach (var code in new[] { "C10-ENG", "C10-SCI" })
        {
            var past = created.GetValueOrDefault(code, []).Where(s => s.Status == SessionStatus.Completed).OrderByDescending(s => s.StartUtc).Take(2).ToList();
            var chapters = subjects.Single(s => s.Code == code).Chapters;
            foreach (var s in past)
            {
                var chapter = chapters.FirstOrDefault(c => c.Id == s.ChapterId)?.Title ?? s.Title;
                db.Notes.Add(new SessionNote
                {
                    SessionId = s.Id,
                    AuthorUserId = s.TeacherId,
                    Title = $"{chapter}: class notes",
                    Status = PublicationStatus.Published,
                    PublishedAtUtc = s.EndUtc,
                    Visibility = NoteVisibility.Participants,
                    AvailableOffline = true,
                    BodyMarkdown = $"## {chapter}\n\n* Key ideas from today's class\n* Re-read the chapter in the NCERT textbook\n* Try the in-text questions\n\n_Demo content._",
                });
                var file = await StorePlaceholderVideoAsync(s.TenantId, s.TeacherId, ct);
                db.PrerecordedVideos.Add(new PrerecordedVideo
                {
                    SessionId = s.Id,
                    Title = $"{chapter} — recap (demo placeholder, no real video)",
                    Description = "Fictional demo video record.",
                    FileId = file.Id,
                    DurationSeconds = 600,
                    UnlockRule = VideoUnlockRule.AfterClass,
                    UnlockOffsetMinutes = 0,
                    UploadedByUserId = s.TeacherId,
                });
                changes += 2;
            }
        }
        await db.SaveChangesAsync(ct);
        if (skipped > 0) logger.LogInformation("Demo timetable: skipped {Skipped} occurrence(s) that would overlap existing classes.", skipped);
        return changes;
    }

    /// <summary>A tiny MP4 header standing in for a video file (the demo ships no media).</summary>
    private async Task<StoredFile> StorePlaceholderVideoAsync(Guid tid, Guid uploadedBy, CancellationToken ct)
    {
        byte[] bytes = [0, 0, 0, 0x18, .. "ftypisom"u8.ToArray(), 0, 0, 0, 0, .. "isommp41"u8.ToArray()];
        var key = $"{tid:N}/{clock.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
        using (var content = new MemoryStream(bytes)) await storage.SaveAsync(key, content, ct);
        var file = new StoredFile
        {
            TenantId = tid,
            StorageKey = key,
            FileName = "demo-placeholder.mp4",
            ContentType = "video/mp4",
            SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            Purpose = FilePurpose.PrerecordedVideo,
            UploadedByUserId = uploadedBy,
        };
        db.Files.Add(file);
        return file;
    }

    /// <summary>
    /// Curricula (NCERT, Kerala State), each demo student's syllabus and academic year, and a few clearly fictional weak
    /// subjects and goals (addendum C). Matched by code/text so repeated runs change nothing.
    /// </summary>
    private async Task<int> EnsureLearningProfilesAsync(Guid tid, List<Subject> subjects, Dictionary<string, Guid> teachers, CancellationToken ct)
    {
        var changes = 0;
        var curricula = await db.Curricula.ToListAsync(ct);
        Guid Curriculum(string code, string name)
        {
            var c = curricula.FirstOrDefault(x => x.Code == code);
            if (c is null) { c = new Curriculum { Code = code, Name = name }; db.Curricula.Add(c); curricula.Add(c); changes++; }
            return c.Id;
        }
        var ncert = Curriculum("NCERT", "CBSE / NCERT");
        var kerala = Curriculum("KERALA", "Kerala State (SCERT)");
        await db.SaveChangesAsync(ct);

        var yearId = await db.AcademicYears.Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct);
        var students = await db.Students.Include(s => s.User).Where(s => s.User!.IsDemo).ToListAsync(ct);
        foreach (var s in students)
        {
            // Dev Patel follows the Kerala State syllabus; everyone else NCERT. Only fill in what is still empty.
            var wanted = s.User!.Email!.StartsWith("dev@", StringComparison.OrdinalIgnoreCase) ? kerala : ncert;
            if (s.CurriculumId is null) { s.CurriculumId = wanted; changes++; }
            if (s.AcademicYearId is null && yearId is not null) { s.AcademicYearId = yearId; changes++; }
        }
        await db.SaveChangesAsync(ct);

        Guid? StudentId(string first) => students.FirstOrDefault(s => s.User!.Email!.StartsWith(first + "@", StringComparison.OrdinalIgnoreCase))?.Id;
        Guid? SubjectId(string code) => subjects.FirstOrDefault(x => x.Code == code)?.Id;
        var adminId = await db.Users.Where(u => u.TenantId == tid && u.Role == UserRole.InstitutionAdmin).OrderBy(u => u.CreatedAtUtc).Select(u => u.Id).FirstAsync(ct);
        var needs = new (string Student, string Subject, string? Chapter, LearningNeedSource Source, string Note, Guid By)[]
        {
            ("aisha", "MATH10", "Trigonometry", LearningNeedSource.Student, "I get lost with identities (demo).", StudentId("aisha") ?? adminId),
            ("aisha", "C10-SCI", null, LearningNeedSource.Parent, "Mother asked for extra help with Physics numericals (demo, recorded by admin).", adminId),
            ("fatima", "CHEM10", null, LearningNeedSource.Teacher, "Mole concept needs revision before the next test (demo).", teachers.GetValueOrDefault("priya", adminId)),
        };
        var existingNeeds = await db.LearningNeeds.Select(n => new { n.StudentId, n.SubjectId, n.ChapterId }).ToListAsync(ct);
        foreach (var (student, subject, chapter, source, note, by) in needs)
        {
            if (StudentId(student) is not { } sid || SubjectId(subject) is not { } subId) continue;
            var chapterId = chapter is null ? null : await db.Chapters.Where(c => c.SubjectId == subId && c.Title == chapter).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (existingNeeds.Any(n => n.StudentId == sid && n.SubjectId == subId && n.ChapterId == chapterId)) continue;
            db.LearningNeeds.Add(new StudentLearningNeed { StudentId = sid, SubjectId = subId, ChapterId = chapterId, Source = source, Note = note, RecordedByUserId = by });
            changes++;
        }
        const string goal = "Score above 80% in the Class 10 pre-board mathematics paper (demo goal).";
        if (StudentId("aisha") is { } aisha && !await db.StudentNotes.AnyAsync(n => n.StudentId == aisha && n.Text == goal, ct))
        {
            db.StudentNotes.Add(new StudentNote { StudentId = aisha, Kind = StudentNoteKind.Goal, Text = goal, SubjectId = SubjectId("MATH10"), AuthorUserId = adminId });
            db.StudentNotes.Add(new StudentNote { StudentId = aisha, Kind = StudentNoteKind.Observation, Text = "Works carefully but rushes the last questions under time pressure (demo observation).", AuthorUserId = teachers.GetValueOrDefault("amira", adminId) });
            changes += 2;
        }
        await db.SaveChangesAsync(ct);
        return changes;
    }

    /// <summary>
    /// A short Class 10 entry diagnostic (maths across two chapters + science) run by Amira, assigned to Hiro and Aisha, with
    /// Hiro's answers entered from a paper sheet so the breakdown shows one weak chapter. Matched by title; runs once.
    /// </summary>
    private async Task<int> EnsureDemoDiagnosticAsync(Guid tid, List<Subject> subjects, Dictionary<string, Guid> teachers, CancellationToken ct)
    {
        const string title = "Class 10 entry diagnostic (demo)";
        if (await db.Exams.AnyAsync(e => e.Title == title, ct)) return 0;
        var maths = subjects.FirstOrDefault(s => s.Code == "MATH10");
        var science = subjects.FirstOrDefault(s => s.Code == "C10-SCI");
        if (maths is null || science is null || !teachers.TryGetValue("amira", out var amira)) return 0;
        var mathsChapters = await db.Chapters.Where(c => c.SubjectId == maths.Id).OrderBy(c => c.SortOrder).Take(2).ToListAsync(ct);
        var scienceChapter = await db.Chapters.Where(c => c.SubjectId == science.Id).OrderBy(c => c.SortOrder).FirstOrDefaultAsync(ct);
        if (mathsChapters.Count < 2) return 0;
        var students = await db.Students.Include(s => s.User).Where(s => s.User!.IsDemo && (s.User.Email!.StartsWith("hiro@") || s.User.Email.StartsWith("aisha@"))).ToListAsync(ct);
        var hiro = students.FirstOrDefault(s => s.User!.Email!.StartsWith("hiro@"));
        if (hiro is null) return 0;

        Question Mcq(Subject subject, Chapter? chapter, string stem, string correct, params string[] wrong)
        {
            var q = new Question { SubjectId = subject.Id, ChapterId = chapter?.Id, Type = QuestionType.SingleChoice, Difficulty = Difficulty.Easy, Stem = stem + " (demo)", DefaultMarks = 1 };
            var order = 0;
            q.Options.Add(new QuestionOption { Text = correct, IsCorrect = true, SortOrder = order++ });
            foreach (var w in wrong) q.Options.Add(new QuestionOption { Text = w, SortOrder = order++ });
            q.Validate();
            return q;
        }
        var questions = new List<Question>
        {
            Mcq(maths, mathsChapters[0], "Which of these is a rational number?", "3/4", "√2", "π"),
            Mcq(maths, mathsChapters[0], "The HCF of 12 and 18 is", "6", "3", "36"),
            Mcq(maths, mathsChapters[1], "The degree of the polynomial 3x² + 2x + 1 is", "2", "1", "3"),
            Mcq(maths, mathsChapters[1], "A zero of p(x) = x − 5 is", "5", "−5", "0"),
            Mcq(science, scienceChapter, "Water boils at sea level at", "100 °C", "90 °C", "120 °C"),
            Mcq(science, scienceChapter, "The chemical symbol for sodium is", "Na", "S", "So"),
        };
        db.Questions.AddRange(questions);
        var now = clock.UtcNow;
        var exam = new Exam
        {
            Title = title,
            Instructions = "Basic entry questions. Answered on paper; entered by the office. Demo data.",
            TeacherId = amira,
            SubjectId = maths.Id,
            Purpose = ExamPurpose.Diagnostic,
            GradeLevelId = await db.GradeLevels.Where(g => g.Name == "Class 10").Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct),
            CurriculumId = await db.Curricula.Where(c => c.Code == "NCERT").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct),
            OpensAtUtc = now.AddDays(-3),
            ClosesAtUtc = now.AddDays(30),
            DurationMinutes = 45,
            MaxAttempts = 1,
            Status = ExamStatus.Published,
            ResultsReleased = true,
        };
        var order = 0;
        foreach (var q in questions) exam.Questions.Add(new ExamQuestion { QuestionId = q.Id, Marks = 1, SortOrder = order++ });
        foreach (var s in students) exam.Recipients.Add(new ExamRecipient { StudentId = s.Id });
        exam.ValidateForPublish();
        db.Exams.Add(exam);
        await db.SaveChangesAsync(ct);

        // Hiro: first maths chapter wrong (both), second chapter right, science right → one chapter flagged.
        var eqs = exam.Questions.OrderBy(q => q.SortOrder).ToList();
        var picks = new[] { 1, 2, 0, 0, 0, 0 }; // option index chosen per question (0 = correct)
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var attempt = new ExamAttempt
        {
            ExamId = exam.Id,
            StudentId = hiro.Id,
            AttemptNumber = 1,
            StartedAtUtc = now.AddDays(-2),
            DeadlineUtc = now.AddDays(-2),
            SubmittedAtUtc = now.AddDays(-2),
            EntryMode = AttemptEntryMode.PaperEntered,
            EnteredByUserId = amira,
            Status = AttemptStatus.Graded,
            QuestionOrder = string.Join(',', eqs.Select(q => q.Id)),
        };
        decimal score = 0;
        for (var i = 0; i < eqs.Count; i++)
        {
            var q = questions[i];
            var response = new ExamResponse(OptionIds: [q.Options[picks[i]].Id]);
            var g = QuestionGrader.Grade(q, response, eqs[i].Marks);
            attempt.Answers.Add(new ExamAnswer { ExamQuestionId = eqs[i].Id, ResponseJson = JsonSerializer.Serialize(response, json), IsCorrect = g.IsCorrect, AwardedMarks = g.Awarded });
            score += g.Awarded ?? 0;
        }
        attempt.Score = score;
        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return questions.Count + 2;
    }
}
