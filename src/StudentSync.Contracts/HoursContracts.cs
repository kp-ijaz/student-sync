using StudentSync.Domain.Fees;

namespace StudentSync.Contracts;

// Attendance and hours for a student or a teacher, teacher profiles and teacher pay (owner request 2026-11-16).

/// <summary>
/// Classes and hours over one period. Student: Done = classes marked present or late; Missed = marked absent or cancelled
/// because the student was absent. Teacher: Done = completed classes taught; Missed = cancelled because the teacher was
/// absent. Present/Late/Absent/Excused count attendance marks; AwaitingMark = ended classes with no attendance yet;
/// Upcoming = scheduled classes still to come (only for "All time").
/// </summary>
public sealed record HoursStatsDto(int ClassesDone, decimal HoursDone, int ClassesMissed, decimal HoursMissed,
    int Present, int Late, int Absent, int Excused, int AwaitingMark, int Upcoming);
public sealed record PeriodHoursDto(string Label, HoursStatsDto Stats);

/// <summary>A subject the student takes, who teaches it and how much of it they have done.</summary>
public sealed record SubjectTeachersDto(Guid SubjectId, string SubjectName, string? CourseName, IReadOnlyList<LookupItem> Teachers, int ClassesDone, decimal HoursDone);
public sealed record StudentHoursDto(Guid StudentId, IReadOnlyList<PeriodHoursDto> Periods, IReadOnlyList<SubjectTeachersDto> Subjects);

/// <summary>A student a teacher teaches: subjects, classes and hours done with this teacher, and the next class.</summary>
public sealed record TeacherStudentDto(Guid StudentId, string Name, string? PhotoUrl, string? GradeName, IReadOnlyList<string> Subjects,
    int ClassesDone, decimal HoursDone, DateTime? NextClassUtc, Guid? NextSessionId);
public sealed record TeacherOverviewDto(TeacherDto Teacher, IReadOnlyList<PeriodHoursDto> Periods, IReadOnlyList<TeacherStudentDto> Students);

public sealed record TeacherPayRateDto(decimal HourlyRate, DateOnly EffectiveFrom);
public sealed record TeacherPaymentDto(Guid Id, decimal Amount, DateOnly PaidOn, DateOnly? PeriodFrom, DateOnly? PeriodTo, PaymentMethod Method,
    string? Reference, string? Note, string? RecordedBy, bool Voided, string? VoidReason);
/// <summary>
/// Pay = hours of completed classes × the rate in force on each class's day. Due = earned to date − paid (negative = paid
/// in advance). UnratedClasses = completed classes before the first rate, which are not counted until a rate covers them.
/// </summary>
public sealed record TeacherPayDto(Guid TeacherId, string CurrencyCode, decimal? CurrentRate, IReadOnlyList<TeacherPayRateDto> Rates,
    decimal HoursThisMonth, decimal EarnedThisMonth, decimal HoursAllTime, decimal EarnedAllTime, decimal Paid, decimal Due, int UnratedClasses,
    IReadOnlyList<TeacherPaymentDto> Payments);
public sealed record SetTeacherRateRequest(decimal HourlyRate, DateOnly? EffectiveFrom);
public sealed record AddTeacherPaymentRequest(decimal Amount, DateOnly PaidOn, PaymentMethod Method, string? Reference, string? Note, DateOnly? PeriodFrom, DateOnly? PeriodTo);
public sealed record VoidTeacherPaymentRequest(string Reason);

/// <summary>The student's fee per hour; empty clears it. Prices new packages (hours × rate).</summary>
public sealed record SetStudentRateRequest(decimal? HourlyRate);

/// <summary>
/// A subject with its class and syllabus, who teaches it, how many active students take it, completed classes and hours
/// (all time), and FeeValue = hours attended by each student in this subject × that student's rate per hour (admins only;
/// null for others or when no student in it has a rate).
/// </summary>
public sealed record SubjectOverviewDto(Guid Id, string Name, string Code, string? CourseName, Guid? CurriculumId, string? CurriculumName,
    Guid? GradeLevelId, string? GradeName, int GradeOrder, IReadOnlyList<LookupItem> Teachers, int Students, int ClassesDone, decimal HoursDone, decimal? FeeValue);

/// <summary>What "Load default catalogue" added (zeros when everything was already there).</summary>
/// <summary>Tidied: class and syllabus names made consistent ("+1" → "Class 11", "CBSE" → "CBSE / NCERT").</summary>
public sealed record DefaultCatalogueResult(int Syllabi, int Classes, int Courses, int Subjects, int AcademicYears, int Tidied = 0);

/// <summary>"Add a class" (Academics): the course name it will get, the course if it already exists, the usual subjects of
/// that class and syllabus (default catalogue) and the subjects the course already has.</summary>
public sealed record QuickClassSuggestionDto(string CourseName, Guid? CourseId, IReadOnlyList<string> Suggested, IReadOnlyList<string> Existing);
/// <summary>Creates the class course (or reuses it) and adds the listed subjects that are missing, in one step.</summary>
public sealed record QuickClassRequest(Guid GradeLevelId, Guid CurriculumId, List<string> Subjects);

/// <summary>Fees ▸ Salaries for one month. DueNow is what each teacher is owed overall today (earned to date − paid).</summary>
public sealed record SalaryMonthDto(DateOnly Month, string CurrencyCode, decimal Hours, decimal Earned, decimal PaidInMonth, decimal DueNow, IReadOnlyList<SalaryRowDto> Teachers);
public sealed record SalaryRowDto(Guid TeacherId, string TeacherName, decimal Hours, int Classes, decimal Earned, decimal PaidInMonth, decimal DueNow, decimal? Rate, int UnratedClasses);
public sealed record PaySalaryRequest(DateOnly Month, PaymentMethod Method, string? Reference);

/// <summary>An online payment link for what is owed on a package.</summary>
public sealed record PaymentLinkDto(Guid Id, string Url, decimal Amount, string CurrencyCode, DateTime ExpiresAtUtc, string Status, string Provider);
