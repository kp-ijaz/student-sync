namespace StudentSync.Application.Common;

/// <summary>404. Also used when a record exists but the caller may not see it, so IDs cannot be probed.</summary>
public sealed class NotFoundException(string entity) : Exception($"{entity} was not found.")
{
    public string Entity { get; } = entity;
}

/// <summary>403 for actions on records the caller can see but may not change.</summary>
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.") : Exception(message);

/// <summary>400 with field-level errors.</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException For(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>409 with a machine-readable code and optional detail payload (e.g. schedule conflicts).</summary>
public sealed class ConflictException(string message, string code, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

/// <summary>A write targeted a different tenant than the caller's. Indicates a bug or an attack; mapped to 403.</summary>
public sealed class TenantIsolationException(string message) : Exception(message);
