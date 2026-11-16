using StudentSync.Domain.Common;

namespace StudentSync.Domain.People;

public enum ProgressReportStatus { Draft, Sent, Skipped }

/// <summary>
/// A student's month (owner request 2026-11-17): prepared automatically on the 1st from real records (attendance, hours,
/// marks), checked by the teacher who taught them most that month, who adds a remark and sends it. Only sent reports reach
/// the student (and their guardian by SMS, if switched on). One per student per month.
/// </summary>
public class ProgressReport : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid TeacherId { get; set; }
    /// <summary>First day of the month it covers.</summary>
    public DateOnly Month { get; set; }
    public int ClassesAttended { get; set; }
    public int ClassesMissed { get; set; }
    public decimal HoursAttended { get; set; }
    public decimal? HoursLeft { get; set; }
    public int AssignmentsGraded { get; set; }
    public decimal? AverageMarkPercent { get; set; }
    public string? Remark { get; set; }
    public ProgressReportStatus Status { get; set; }
    public Guid? SentByUserId { get; set; }
    public DateTime? SentAtUtc { get; set; }
}
