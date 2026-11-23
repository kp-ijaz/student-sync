using StudentSync.Domain.Fees;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Contracts;

// ---------- Fees: prepaid hour packages and payments ----------

/// <summary>Add a package of hours to a student. <paramref name="PaidNow"/> records the first payment in the same step.</summary>
public sealed record SavePackageRequest(
    decimal Hours, decimal Price, DateOnly? StartsOn = null, DateOnly? PaymentDueOn = null, string? Name = null, string? Notes = null,
    decimal? PaidNow = null, PaymentMethod Method = PaymentMethod.Cash, string? Reference = null);

/// <summary>Corrects a package added by mistake (name, hours, price, start or pay-by day). The price cannot go below what was already paid.</summary>
public sealed record UpdatePackageRequest(decimal Hours, decimal Price, DateOnly StartsOn, DateOnly? PaymentDueOn = null, string? Name = null, string? Notes = null);

public sealed record RecordPaymentRequest(decimal Amount, DateOnly? PaidOn = null, PaymentMethod Method = PaymentMethod.Cash, string? Reference = null);
public sealed record VoidPaymentRequest(string Reason);

public sealed record FeePaymentDto(Guid Id, decimal Amount, DateOnly PaidOn, PaymentMethod Method, string? Reference, string? RecordedBy, bool Voided, string? VoidReason);

public sealed record StudentPackageDto(
    Guid Id, string Name, decimal Hours, decimal Price, string CurrencyCode, DateOnly StartsOn, DateOnly DueOn, PackageStatus Status,
    decimal Paid, decimal Balance, string? Notes, IReadOnlyList<FeePaymentDto> Payments);

/// <summary>An attended class that used hours: present or late, not cancelled, on or after the first active package's start.</summary>
public sealed record HoursUsedDto(Guid SessionId, string Title, DateTime StartUtc, decimal Hours, AttendanceStatus Status);

/// <summary>
/// A student's hours and fees. Hours left = hours bought (active packages) − hours used; it can go below zero when classes
/// continue after the hours run out (the centre is warned, nothing is blocked). Due = price of active packages − valid payments.
/// </summary>
public sealed record StudentFeesDto(
    Guid StudentId, string CurrencyCode, decimal HoursBought, decimal HoursUsed, decimal HoursLeft, decimal Billed, decimal Paid, decimal Due,
    DateOnly? OldestDueOn, IReadOnlyList<StudentPackageDto> Packages, IReadOnlyList<HoursUsedDto> RecentUsage, decimal? HourlyRate = null);

/// <summary>One student who owes money or is running out of hours.</summary>
public sealed record FeeDueItemDto(
    Guid StudentId, string StudentName, string? StudentCode, string? PhotoUrl, decimal Due, DateOnly? DueOn, int DaysOverdue, decimal HoursLeft);

/// <summary>
/// The front desk's money list. Items are ordered most overdue first, then by amount, then by fewest hours left.
/// <see cref="LowHoursAt"/> is the threshold used for "running out of hours".
/// </summary>
public sealed record FeesDueDto(
    string CurrencyCode, decimal TotalDue, int StudentsOwing, int OverdueCount, int LowHoursCount, decimal LowHoursAt, decimal CollectedThisMonth,
    int TotalItems, IReadOnlyList<FeeDueItemDto> Items);

// ---------- Fee plans: the centre's packages / fee structure ----------

/// <summary>A package the centre offers. <paramref name="PricePerHour"/> = price ÷ hours. Class/syllabus empty = offered to every student.</summary>
public sealed record FeePlanDto(
    Guid Id, string Name, decimal Hours, decimal Price, decimal PricePerHour, string CurrencyCode,
    Guid? GradeLevelId, string? GradeLevelName, Guid? CurriculumId, string? CurriculumName, string? Notes, bool IsActive);

public sealed record SaveFeePlanRequest(
    decimal Hours, decimal Price, string? Name = null, Guid? GradeLevelId = null, Guid? CurriculumId = null, string? Notes = null, bool IsActive = true);
