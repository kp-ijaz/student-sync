using StudentSync.Domain.Common;

namespace StudentSync.Domain.Communication;

public enum ConversationType { Direct, Group }

public class Conversation : TenantEntity
{
    public ConversationType Type { get; set; }
    public string? Subject { get; set; }
    /// <summary>For direct conversations: "{lowerUserId}:{higherUserId}", unique per tenant to prevent duplicate threads.</summary>
    public string? DirectKey { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public List<ConversationParticipant> Participants { get; set; } = [];

    public static string BuildDirectKey(Guid a, Guid b) => a.CompareTo(b) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}

public class ConversationParticipant : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime? LastReadAtUtc { get; set; }
}

public class Message : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = "";
    public Guid? AttachmentFileId { get; set; }
}

public enum AnnouncementAudience { Everyone, Teachers, Students, Parents, Batch }

public class Announcement : TenantEntity
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public AnnouncementAudience Audience { get; set; }
    public Guid? BatchId { get; set; }
    public Guid AuthorUserId { get; set; }
    public DateTime PublishedAtUtc { get; set; }
}

public enum NotificationType
{
    SessionScheduled, SessionRescheduled, SessionCancelled, SessionReminder,
    AssignmentPublished, AssignmentDueSoon, AssignmentGraded, SubmissionReceived,
    ExamPublished, ExamResultsReleased,
    DoubtAssigned, DoubtReplied, DoubtFollowUp, DoubtConfirmationRequested,
    NotesPublished, RecordingAvailable, AiDraftReady,
    MessageReceived, Announcement, WeeklyDigest, AttendanceMarked
}

/// <summary>An in-app notification. (UserId, DedupeKey) is unique, so retried events never notify twice.</summary>
public class Notification : Entity
{
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public string DedupeKey { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public enum DeliveryChannel { Email, Push }
public enum DeliveryStatus { Pending, Accepted, Failed, Skipped }

/// <summary>External delivery attempt. Status is Accepted only when the provider confirms acceptance.</summary>
public class NotificationDelivery : Entity
{
    public Guid NotificationId { get; set; }
    public DeliveryChannel Channel { get; set; }
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class NotificationPreference : Entity
{
    public Guid UserId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
    /// <summary>Comma-separated <see cref="NotificationType"/> names the user muted for external channels.</summary>
    public string MutedTypes { get; set; } = "";
}

public class DeviceRegistration : Entity
{
    public Guid UserId { get; set; }
    public string Platform { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime RegisteredAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
