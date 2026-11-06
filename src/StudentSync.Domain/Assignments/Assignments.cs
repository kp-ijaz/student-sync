using StudentSync.Domain.Common;
using StudentSync.Domain.People;

namespace StudentSync.Domain.Assignments;

public enum AssignmentStatus { Draft, Published, Closed }
public enum SubmissionStatus { Draft, Submitted, Graded, ReturnedForCorrection }

/// <summary>Homework. Recipients are materialised explicitly at publish time, so the batch link is optional.</summary>
public class Assignment : TenantEntity
{
    public string Title { get; set; } = "";
    public string InstructionsMarkdown { get; set; } = "";
    public Guid TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? SubjectId { get; set; }
    public DateTime DueAtUtc { get; set; }
    public decimal MaxMarks { get; set; }
    public bool AllowLateSubmission { get; set; }
    public bool AllowResubmission { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public List<AssignmentRecipient> Recipients { get; set; } = [];
    public List<AssignmentAttachment> Attachments { get; set; } = [];

    public void Publish(DateTime nowUtc)
    {
        if (Status != AssignmentStatus.Draft) throw new DomainException("Only draft assignments can be published.", "invalid_assignment_state");
        if (Recipients.Count == 0) throw new DomainException("Choose at least one student before publishing.", "no_recipients");
        Status = AssignmentStatus.Published;
        PublishedAtUtc = nowUtc;
    }
}

public class AssignmentRecipient : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
}

public class AssignmentAttachment : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public Guid FileId { get; set; }
}

public class AssignmentSubmission : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public string? TextAnswer { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public bool IsLate { get; set; }
    public int AttemptNumber { get; set; }
    public decimal? Marks { get; set; }
    public string? Feedback { get; set; }
    public DateTime? GradedAtUtc { get; set; }
    public Guid? GradedByUserId { get; set; }
    public List<SubmissionFile> Files { get; set; } = [];

    public void Submit(Assignment assignment, DateTime nowUtc)
    {
        if (assignment.Status != AssignmentStatus.Published)
            throw new DomainException("This assignment is not open for submissions.", "assignment_closed");
        var canEdit = Status is SubmissionStatus.Draft or SubmissionStatus.ReturnedForCorrection
                      || (Status == SubmissionStatus.Submitted && assignment.AllowResubmission);
        if (!canEdit) throw new DomainException("This submission can no longer be changed.", "submission_locked");
        var late = nowUtc > assignment.DueAtUtc;
        if (late && !assignment.AllowLateSubmission)
            throw new DomainException("The deadline has passed and late submissions are not accepted.", "deadline_passed");
        if (string.IsNullOrWhiteSpace(TextAnswer) && Files.Count == 0)
            throw new DomainException("Add an answer or attach a file before submitting.", "empty_submission");
        Status = SubmissionStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        IsLate = late;
        AttemptNumber++;
    }

    public void Grade(decimal marks, decimal maxMarks, string? feedback, bool returnForCorrection, Guid graderId, DateTime nowUtc)
    {
        if (Status is not (SubmissionStatus.Submitted or SubmissionStatus.Graded))
            throw new DomainException("Only submitted work can be graded.", "invalid_submission_state");
        if (marks < 0 || marks > maxMarks) throw new DomainException($"Marks must be between 0 and {maxMarks}.", "invalid_marks");
        Marks = marks;
        Feedback = feedback;
        GradedAtUtc = nowUtc;
        GradedByUserId = graderId;
        Status = returnForCorrection ? SubmissionStatus.ReturnedForCorrection : SubmissionStatus.Graded;
    }
}

public class SubmissionFile : TenantEntity
{
    public Guid SubmissionId { get; set; }
    public Guid FileId { get; set; }
}
