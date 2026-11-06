using StudentSync.Domain.Common;

namespace StudentSync.Domain.Platform;

/// <summary>Append-only audit trail for sensitive and data-changing operations. Never stores secrets or private content.</summary>
public class AuditLog : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Summary { get; set; }
    public string? ChangedFields { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Stored result of a retried command, keyed by (UserId, Key).</summary>
public class IdempotencyRecord : Entity
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public enum IntegrationProvider { GoogleWorkspace }
public enum IntegrationStatus { Connected, Revoked, Error }

/// <summary>OAuth connection for an external provider. Tokens are encrypted with ASP.NET Core Data Protection.</summary>
public class IntegrationConnection : TenantEntity
{
    public Guid UserId { get; set; }
    public IntegrationProvider Provider { get; set; }
    public IntegrationStatus Status { get; set; }
    public string? AccountEmail { get; set; }
    public string Scopes { get; set; } = "";
    public string EncryptedTokens { get; set; } = "";
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? LastError { get; set; }
}

public class FeatureFlag : AuditableEntity
{
    public string Key { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; }
}
