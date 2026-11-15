namespace StudentSync.Domain.Common;

/// <summary>Base for all persisted entities. IDs are generated client-side by EF Core (sequential GUIDs).</summary>
public abstract class Entity
{
    public Guid Id { get; set; }
}

/// <summary>
/// Entity with audit columns and an optimistic-concurrency token. Audit columns are populated by the
/// persistence layer from the authenticated user, never from client input.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Optimistic-concurrency token: PostgreSQL's xmin system column, which changes on every row update.</summary>
    public uint RowVersion { get; set; }
}

/// <summary>Marks an entity as owned by exactly one institution (tenant). Enforced by query filters and save checks.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
}

/// <summary>Violation of a business rule. Mapped to HTTP 422 by the API.</summary>
public class DomainException(string message, string code = "domain_rule") : Exception(message)
{
    public string Code { get; } = code;
}
