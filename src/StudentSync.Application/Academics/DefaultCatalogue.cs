using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Identity;

namespace StudentSync.Application.Academics;

/// <summary>
/// The starting academic catalogue of an Indian tuition centre (owner request 2026-11-16): the syllabi NCERT (CBSE),
/// Kerala State (SCERT) and ICSE/ISC, Classes 1–12, one course per syllabus and class ("Class 10 · NCERT") with that
/// syllabus's usual subjects, and the current April–March academic year. Subjects are "Additional": a student takes only
/// the ones chosen at admission. Loading is idempotent and merges with what the institute already has — syllabi match by
/// code or name, classes by name including "+1"/"Plus One"/"XI" — so running it twice, or on an institute that already
/// created some of these, adds only what is missing and never renames or removes anything.
/// </summary>
public sealed class DefaultCatalogue(IAppDbContext db, AccessPolicy access, AuditService audit, IClock clock)
{
    private sealed record Syllabus(string Code, string Name, string[] Aliases, (int From, int To, string[] Subjects)[] Bands);

    private static readonly string[] SeniorScience = ["English", "Physics", "Chemistry", "Mathematics", "Biology", "Computer Science"];

    private static readonly Syllabus[] Syllabi =
    [
        new("NCERT", "CBSE / NCERT", ["NCERT", "CBSE"],
        [
            (1, 2, ["English", "Hindi", "Mathematics"]),
            (3, 5, ["English", "Hindi", "Mathematics", "Environmental Studies"]),
            (6, 8, ["English", "Hindi", "Mathematics", "Science", "Social Science", "Sanskrit"]),
            (9, 10, ["English", "Hindi", "Mathematics", "Science", "Social Science"]),
            (11, 12, [.. SeniorScience, "Accountancy", "Business Studies", "Economics"]),
        ]),
        new("KERALA", "Kerala State (SCERT)", ["KERALA", "SCERT"],
        [
            (1, 4, ["Malayalam", "English", "Mathematics", "Environmental Science"]),
            (5, 7, ["Malayalam", "English", "Hindi", "Mathematics", "Basic Science", "Social Science"]),
            (8, 10, ["Malayalam", "English", "Hindi", "Mathematics", "Physics", "Chemistry", "Biology", "Social Science", "Information Technology"]),
            (11, 12, [.. SeniorScience, "Accountancy", "Business Studies", "Economics"]),
        ]),
        new("ICSE", "ICSE / ISC (CISCE)", ["ICSE", "ISC", "CISCE"],
        [
            (1, 5, ["English", "Hindi", "Mathematics", "Environmental Studies"]),
            (6, 8, ["English", "Hindi", "Mathematics", "Physics", "Chemistry", "Biology", "History & Civics", "Geography", "Computer Studies"]),
            (9, 10, ["English", "Hindi", "Mathematics", "Physics", "Chemistry", "Biology", "History & Civics", "Geography", "Computer Applications"]),
            (11, 12, [.. SeniorScience, "Accountancy", "Commerce", "Economics"]),
        ]),
    ];

    /// <summary>Admin action (Academics ▸ Load default catalogue).</summary>
    public Task<DefaultCatalogueResult> LoadAsync(CancellationToken ct)
    {
        access.Require(Permissions.AcademicsManage);
        return LoadForCurrentTenantAsync(ct);
    }

    /// <summary>Also used when a new institute is created (the caller has set the tenant scope; no signed-in user).</summary>
    public async Task<DefaultCatalogueResult> LoadForCurrentTenantAsync(CancellationToken ct)
    {
        int syllabi = 0, classes = 0, courses = 0, subjects = 0, years = 0;

        // ---- Syllabi ----
        var curricula = await db.Curricula.ToListAsync(ct);
        var curriculumFor = new Dictionary<string, Curriculum>();
        foreach (var s in Syllabi)
        {
            var existing = curricula.FirstOrDefault(c => s.Aliases.Any(a => Matches(c.Code, a) || Contains(c.Name, a)));
            if (existing is null)
            {
                existing = new Curriculum { Code = s.Code, Name = s.Name };
                db.Curricula.Add(existing);
                curricula.Add(existing);
                syllabi++;
            }
            curriculumFor[s.Code] = existing;
        }

        // ---- Classes 1–12 ----
        var grades = await db.GradeLevels.ToListAsync(ct);
        var gradeFor = new Dictionary<int, GradeLevel>();
        for (var n = 1; n <= 12; n++)
        {
            var existing = grades.FirstOrDefault(g => ClassNumber(g.Name) == n);
            if (existing is null)
            {
                existing = new GradeLevel { Name = $"Class {n}", SortOrder = n };
                db.GradeLevels.Add(existing);
                grades.Add(existing);
                classes++;
            }
            gradeFor[n] = existing;
        }

        // ---- Current academic year (April–March) ----
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var startYear = today.Month >= 4 ? today.Year : today.Year - 1;
        var (from, to) = (new DateOnly(startYear, 4, 1), new DateOnly(startYear + 1, 3, 31));
        var allYears = await db.AcademicYears.ToListAsync(ct);
        if (!allYears.Any(y => y.StartDate <= today && y.EndDate >= today))
        {
            db.AcademicYears.Add(new AcademicYear { Name = $"{startYear}–{(startYear + 1) % 100:00}", StartDate = from, EndDate = to, IsCurrent = !allYears.Any(y => y.IsCurrent) });
            years++;
        }

        // ---- One course per syllabus and class, with its subjects ----
        var courseCodes = (await db.Courses.Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subjectCodes = (await db.Subjects.Select(s => s.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingCourses = await db.Courses.ToListAsync(ct);
        foreach (var s in Syllabi)
            foreach (var (bandFrom, bandTo, names) in s.Bands)
                for (var n = bandFrom; n <= bandTo; n++)
                {
                    var code = $"{s.Code}-C{n}";
                    var course = existingCourses.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (course is null)
                    {
                        course = new Course
                        {
                            Code = code,
                            Name = $"Class {n} · {ShortName(s)}",
                            GradeLevelId = gradeFor[n].Id,
                            CurriculumId = curriculumFor[s.Code].Id,
                            Description = $"{s.Name} subjects for Class {n}."
                        };
                        db.Courses.Add(course);
                        existingCourses.Add(course);
                        courseCodes.Add(code);
                        courses++;
                    }
                    foreach (var name in names)
                    {
                        var subjectCode = $"{code}-{Abbreviation(name)}";
                        if (!subjectCodes.Add(subjectCode)) continue;
                        var subject = new Subject { CourseId = course.Id, Code = subjectCode, Name = name };
                        subject.SetKind(SubjectKind.Additional, null);
                        db.Subjects.Add(subject);
                        subjects++;
                    }
                }

        audit.Record("academics.default_catalogue", "Catalogue", null,
            $"Loaded defaults: {syllabi} syllabi, {classes} classes, {courses} courses, {subjects} subjects, {years} academic year(s)");
        await db.SaveChangesAsync(ct);
        var tidied = await TidyForCurrentTenantAsync(ct);
        return new DefaultCatalogueResult(syllabi, classes, courses, subjects, years, tidied);
    }

    private static string ShortName(Syllabus s) => s.Code switch { "NCERT" => "NCERT", "KERALA" => "Kerala", _ => "ICSE" };

    // ---------------- "Add a class" in one step ----------------

    /// <summary>What "Add a class" offers for a class + syllabus: the usual subjects, and what the course already has.</summary>
    public async Task<QuickClassSuggestionDto> SuggestAsync(Guid gradeLevelId, Guid curriculumId, CancellationToken ct)
    {
        access.Require(Permissions.AcademicsManage);
        var (grade, curriculum) = await ClassAndSyllabusAsync(gradeLevelId, curriculumId, ct);
        var course = await db.Courses.Where(c => c.GradeLevelId == gradeLevelId && c.CurriculumId == curriculumId && c.Status != CatalogStatus.Archived)
            .Select(c => new { c.Id, c.Name, Subjects = c.Subjects.Where(x => x.Status != CatalogStatus.Archived).Select(x => x.Name).ToList() }).FirstOrDefaultAsync(ct);
        return new QuickClassSuggestionDto(course?.Name ?? CourseName(grade, curriculum), course?.Id,
            SuggestedSubjects(curriculum, ClassNumber(grade.Name)), course?.Subjects.OrderBy(x => x).ToList() ?? []);
    }

    /// <summary>Creates "Class 10 · NCERT" (or reuses it) and adds the chosen subjects it does not have yet, in one save.</summary>
    public async Task<Guid> CreateClassAsync(QuickClassRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AcademicsManage);
        var (grade, curriculum) = await ClassAndSyllabusAsync(r.GradeLevelId, r.CurriculumId, ct);
        var names = (r.Subjects ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) throw RequestValidationException.For("subjects", "Choose at least one subject.");
        if (names.Count > 40) throw RequestValidationException.For("subjects", "Choose at most 40 subjects.");
        if (names.Any(n => n.Length > 100)) throw RequestValidationException.For("subjects", "A subject name can be at most 100 characters.");

        var course = await db.Courses.Include(c => c.Subjects)
            .FirstOrDefaultAsync(c => c.GradeLevelId == r.GradeLevelId && c.CurriculumId == r.CurriculumId && c.Status != CatalogStatus.Archived, ct);
        var codes = (await db.Courses.Select(c => c.Code).ToListAsync(ct)).Concat(await db.Subjects.Select(x => x.Code).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Unique(string stem) { var code = stem; for (var i = 2; !codes.Add(code); i++) code = $"{stem}{i}"; return code; }
        if (course is null)
        {
            var n = ClassNumber(grade.Name);
            var stem = new string(curriculum.Code.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(8).ToArray()) + "-C" + (n?.ToString(CultureInfo.InvariantCulture) ?? new string(grade.Name.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant());
            course = new Course { Code = Unique(stem), Name = CourseName(grade, curriculum), GradeLevelId = grade.Id, CurriculumId = curriculum.Id, Description = $"{curriculum.Name} subjects for {grade.Name}." };
            db.Courses.Add(course);
        }
        var added = 0;
        foreach (var name in names.Where(n => !course.Subjects.Any(x => x.Status != CatalogStatus.Archived && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase))))
        {
            var subject = new Subject { CourseId = course.Id, Code = Unique($"{course.Code}-{Abbreviation(name)}"), Name = name };
            subject.SetKind(SubjectKind.Additional, null);
            db.Subjects.Add(subject);
            added++;
        }
        audit.Record("course.quick_add", "Course", course.Id, $"{course.Name}: {added} subject(s) added");
        await db.SaveChangesAsync(ct);
        return course.Id;
    }

    private async Task<(GradeLevel Grade, Curriculum Curriculum)> ClassAndSyllabusAsync(Guid gradeLevelId, Guid curriculumId, CancellationToken ct)
    {
        var grade = await db.GradeLevels.FirstOrDefaultAsync(g => g.Id == gradeLevelId, ct) ?? throw RequestValidationException.For("gradeLevelId", "Choose the class.");
        var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == curriculumId, ct) ?? throw RequestValidationException.For("curriculumId", "Choose the syllabus.");
        return (grade, curriculum);
    }

    private static string CourseName(GradeLevel grade, Curriculum curriculum)
    {
        var known = Syllabi.FirstOrDefault(s => s.Aliases.Any(a => Matches(curriculum.Code, a) || Contains(curriculum.Name, a)));
        return $"{grade.Name} · {(known is null ? curriculum.Name : ShortName(known))}";
    }

    /// <summary>The usual subjects of a class in a syllabus (empty for an unknown syllabus or class).</summary>
    public static IReadOnlyList<string> SuggestedSubjects(Curriculum curriculum, int? classNumber)
    {
        var known = Syllabi.FirstOrDefault(s => s.Aliases.Any(a => Matches(curriculum.Code, a) || Contains(curriculum.Name, a)));
        if (known is null || classNumber is not { } n) return [];
        return known.Bands.Where(b => n >= b.From && n <= b.To).SelectMany(b => b.Subjects).ToList();
    }

    /// <summary>"Class 11", "11", "Grade 11", "XI", "+1", "Plus One", "11th" → 11. Anything else → null.</summary>
    public static int? ClassNumber(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim().ToLowerInvariant().Replace("class", "").Replace("grade", "").Replace("std", "").Replace("standard", "").Trim(' ', '.', '-');
        if (n is "+1" or "plus one" or "plus 1" or "xi") return 11;
        if (n is "+2" or "plus two" or "plus 2" or "xii") return 12;
        string[] roman = ["i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x"];
        var r = Array.IndexOf(roman, n);
        if (r >= 0) return r + 1;
        foreach (var suffix in new[] { "st", "nd", "rd", "th" }) if (n.EndsWith(suffix, StringComparison.Ordinal)) n = n[..^suffix.Length];
        return int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v is >= 1 and <= 12 ? v : null;
    }

    /// <summary>
    /// Makes names consistent (owner request 2026-11-17): classes recognised as 1–12 ("1", "+1", "XI", "Grade 5") become
    /// "Class N" in order N, and NCERT / Kerala / ICSE syllabi get their standard code and name — each only when no other
    /// entry already uses that name or code. Records keep their ids, so students and courses stay linked.
    /// </summary>
    public async Task<int> TidyForCurrentTenantAsync(CancellationToken ct)
    {
        var changed = 0;
        var grades = await db.GradeLevels.ToListAsync(ct);
        foreach (var g in grades)
        {
            if (ClassNumber(g.Name) is not { } n) continue;
            var name = $"Class {n}";
            if (g.Name != name && !grades.Any(o => o.Id != g.Id && Matches(o.Name, name))) { g.Name = name; changed++; }
            if (g.SortOrder != n) { g.SortOrder = n; changed++; }
        }
        var curricula = await db.Curricula.ToListAsync(ct);
        foreach (var s in Syllabi)
        {
            var c = curricula.FirstOrDefault(c => s.Aliases.Any(a => Matches(c.Code, a) || Contains(c.Name, a)));
            if (c is null) continue;
            if (c.Name != s.Name && !curricula.Any(o => o.Id != c.Id && Matches(o.Name, s.Name))) { c.Name = s.Name; changed++; }
            if (c.Code != s.Code && !curricula.Any(o => o.Id != c.Id && Matches(o.Code, s.Code))) { c.Code = s.Code; changed++; }
        }
        if (changed == 0) return 0;
        audit.Record("catalogue.tidied", "Catalogue", null, $"{changed} name/order change(s)");
        await db.SaveChangesAsync(ct);
        return changed;
    }

    // ---------------- Automatic codes (owner request 2026-11-17: codes are never typed) ----------------

    /// <summary>NCERT / KERALA / ICSE for the known syllabi, otherwise the name's initials (e.g. "Gulf State Board" → GSB).</summary>
    public static string SyllabusCode(string name, ISet<string> taken)
    {
        var known = Syllabi.FirstOrDefault(s => s.Aliases.Any(a => Contains(name, a)));
        return Unique(known?.Code ?? Initials(name), taken);
    }

    /// <summary>"NCERT-C5" for a class course with a syllabus; otherwise the name's initials.</summary>
    public static string CourseCode(string name, string? gradeName, string? curriculumCode, ISet<string> taken)
    {
        if (gradeName is not null && curriculumCode is not null)
        {
            var cur = new string(curriculumCode.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(8).ToArray());
            var n = ClassNumber(gradeName)?.ToString(CultureInfo.InvariantCulture) ?? new string(gradeName.Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant();
            if (cur.Length > 0 && n.Length > 0) return Unique($"{cur}-C{n}", taken);
        }
        return Unique(Initials(name), taken);
    }

    /// <summary>"NCERT-C5-MATH": the course's code and the subject's short name.</summary>
    public static string SubjectCode(string courseCode, string name, ISet<string> taken) => Unique($"{courseCode}-{Abbreviation(name)}", taken);

    /// <summary>The stem, or stem2, stem3… — the first one not taken (the chosen code is added to <paramref name="taken"/>).</summary>
    public static string Unique(string stem, ISet<string> taken)
    {
        stem = stem.Length > 26 ? stem[..26] : stem;
        var code = stem;
        for (var i = 2; !taken.Add(code); i++) code = $"{stem}{i}";
        return code;
    }

    private static string Initials(string name)
    {
        var words = name.Split([' ', '-', '/', '(', ')', '&', ',', '.', '·'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray())).Where(w => w.Length > 0).ToList();
        var code = words.Count >= 2 ? string.Concat(words.Take(6).Select(w => char.IsDigit(w[0]) ? w : w[..1]))
            : words.Count == 1 ? new string(words[0].Take(8).ToArray()) : "X";
        return code.ToUpperInvariant();
    }

    private static string Abbreviation(string subject) => subject switch
    {
        "Mathematics" => "MATH",
        "Environmental Studies" => "EVS",
        "Environmental Science" => "ENVS",
        "Social Science" => "SSC",
        "Basic Science" => "BSC",
        "Computer Science" => "CS",
        "Computer Applications" => "CA",
        "Computer Studies" => "COMP",
        "Information Technology" => "IT",
        "Business Studies" => "BST",
        "History & Civics" => "HIST",
        "Accountancy" => "ACC",
        _ => new string(subject.Where(char.IsLetter).Take(4).ToArray()).ToUpperInvariant(),
    };

    private static bool Matches(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string? text, string word) => text is not null && text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
