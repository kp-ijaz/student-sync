using Microsoft.AspNetCore.Identity;

namespace StudentSync.Domain.Identity;

public enum UserRole
{
    SuperAdmin,
    InstitutionAdmin,
    Teacher,
    Student,
    Parent
}

/// <summary>
/// A login account. Institution users always have a TenantId; platform super admins have none.
/// Email uniqueness is tenant-scoped: (TenantId, NormalizedEmail).
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }
    /// <summary>IANA time-zone id used to display times to this user, e.g. "Asia/Dubai".</summary>
    public string TimeZoneId { get; set; } = "UTC";
    public string PreferredLanguage { get; set; } = "en";
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    /// <summary>True for fictional development/demo accounts created by the demo seeder.</summary>
    public bool IsDemo { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    /// <summary>Optional profile photo (a private StoredFile with purpose Avatar). Who may see it is decided server-side.</summary>
    public Guid? PhotoFileId { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>SHA-256 of the opaque token; the raw token is never stored.</summary>
    public string TokenHash { get; set; } = "";
    /// <summary>All tokens issued by rotation from one login share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }
    public string? ClientName { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}

/// <summary>Fine-grained permissions. Role names alone are never used for authorization decisions.</summary>
public static class Permissions
{
    public const string PlatformTenantsManage = "platform.tenants.manage";
    public const string PlatformPlansManage = "platform.plans.manage";
    public const string PlatformAuditView = "platform.audit.view";
    public const string PlatformMonitor = "platform.monitor";

    public const string InstitutionSettingsManage = "institution.settings.manage";
    public const string InstitutionAuditView = "institution.audit.view";
    public const string ReportsView = "reports.view";

    public const string UsersManage = "users.manage";
    public const string UsersView = "users.view";
    public const string AcademicsManage = "academics.manage";
    public const string AcademicsView = "academics.view";

    public const string SessionsManageAll = "sessions.manage_all";
    public const string SessionsManageOwn = "sessions.manage_own";
    public const string SessionsView = "sessions.view";
    public const string AttendanceMark = "attendance.mark";

    public const string ContentManage = "content.manage";
    public const string ContentView = "content.view";
    public const string RecordingsManage = "recordings.manage";
    public const string ConsentGive = "consent.give";

    public const string AssignmentsManage = "assignments.manage";
    public const string AssignmentsSubmit = "assignments.submit";
    public const string ExamsManage = "exams.manage";
    public const string ExamsTake = "exams.take";

    public const string DoubtsAsk = "doubts.ask";
    public const string DoubtsAnswer = "doubts.answer";

    public const string MessagesSend = "messages.send";
    public const string AnnouncementsPublish = "announcements.publish";

    public const string AiReview = "ai.review";
    public const string AiConfigure = "ai.configure";
    public const string AiAssistantUse = "ai.assistant.use";

    public const string ProgressViewOwn = "progress.view_own";
    /// <summary>Record, review and resolve a student's weak subjects, goals and observations (admins; teachers for students they teach).</summary>
    public const string LearningNeedsManage = "learning_needs.manage";
    /// <summary>A student reporting or withdrawing their own weak subjects.</summary>
    public const string LearningNeedsSelf = "learning_needs.self";
    public const string ChildrenView = "children.view";
}

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<string>> Matrix = new()
    {
        [UserRole.SuperAdmin] =
        [
            Permissions.PlatformTenantsManage, Permissions.PlatformPlansManage,
            Permissions.PlatformAuditView, Permissions.PlatformMonitor,
        ],
        [UserRole.InstitutionAdmin] =
        [
            Permissions.InstitutionSettingsManage, Permissions.InstitutionAuditView, Permissions.ReportsView,
            Permissions.UsersManage, Permissions.UsersView, Permissions.AcademicsManage, Permissions.AcademicsView,
            Permissions.SessionsManageAll, Permissions.SessionsManageOwn, Permissions.SessionsView, Permissions.AttendanceMark,
            Permissions.ContentManage, Permissions.ContentView, Permissions.RecordingsManage, Permissions.ConsentGive,
            Permissions.AssignmentsManage, Permissions.ExamsManage, Permissions.DoubtsAnswer,
            Permissions.MessagesSend, Permissions.AnnouncementsPublish, Permissions.AiReview, Permissions.AiConfigure,
            Permissions.LearningNeedsManage,
        ],
        [UserRole.Teacher] =
        [
            Permissions.UsersView, Permissions.AcademicsView,
            Permissions.SessionsManageOwn, Permissions.SessionsView, Permissions.AttendanceMark,
            Permissions.ContentManage, Permissions.ContentView, Permissions.RecordingsManage,
            Permissions.AssignmentsManage, Permissions.ExamsManage, Permissions.DoubtsAnswer,
            Permissions.MessagesSend, Permissions.AnnouncementsPublish, Permissions.AiReview, Permissions.LearningNeedsManage,
        ],
        [UserRole.Student] =
        [
            Permissions.AcademicsView, Permissions.SessionsView, Permissions.ContentView, Permissions.ConsentGive,
            Permissions.AssignmentsSubmit, Permissions.ExamsTake, Permissions.DoubtsAsk,
            Permissions.MessagesSend, Permissions.AiAssistantUse, Permissions.ProgressViewOwn, Permissions.LearningNeedsSelf,
        ],
        [UserRole.Parent] =
        [
            Permissions.SessionsView, Permissions.ConsentGive, Permissions.MessagesSend, Permissions.ChildrenView,
        ],
    };

    public static IReadOnlySet<string> For(UserRole role) => Matrix[role];

    public static bool Has(UserRole role, string permission) => Matrix[role].Contains(permission);

    public static IEnumerable<string> All => Matrix.Values.SelectMany(p => p).Distinct().Order();
}
