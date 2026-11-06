using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Domain.Common;
using StudentSync.Infrastructure.Identity;

namespace StudentSync.Api.Hosting;

/// <summary>Maps application exceptions to RFC 7807 problem responses with a stable machine-readable "code".</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, code, title, extra) = ex switch
        {
            RequestValidationException v => (400, "validation_failed", v.Message, new Dictionary<string, object?> { ["errors"] = v.Errors }),
            AuthException a => (a.Status, a.Code, a.Message, null),
            NotFoundException n => (404, "not_found", n.Message, null),
            ForbiddenException f => (403, "forbidden", f.Message, null),
            TenantIsolationException => (403, "forbidden", "You do not have permission to perform this action.", null),
            ConflictException c => (409, c.Code, c.Message, c.Details is null ? null : new Dictionary<string, object?> { ["details"] = c.Details }),
            DomainException d => (422, d.Code, d.Message, null),
            DbUpdateConcurrencyException => (409, "concurrency_conflict", "This record was changed by someone else. Reload and try again.", null),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "duplicate", "A record with the same details already exists.", null),
            BadHttpRequestException b => (b.StatusCode, "bad_request", "The request could not be read.", null),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (499, "cancelled", "The request was cancelled.", null),
            _ => (500, "server_error", "Something went wrong on our side. Please try again.", null),
        };

        if (status >= 500) logger.LogError(ex, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
        else if (ex is TenantIsolationException) logger.LogWarning("Tenant isolation guard triggered for {Path}", http.Request.Path);

        http.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = title, Type = $"https://studentsync.example.test/errors/{code}" };
        details.Extensions["code"] = code;
        if (extra is not null) foreach (var (k, v) in extra) details.Extensions[k] = v;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = ex });
    }
}
