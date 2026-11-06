using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using StudentSync.Contracts;

namespace StudentSync.Web.Services;

/// <summary>Error returned by the API (RFC 7807 problem details).</summary>
public sealed class ApiException(int status, string code, string message, IReadOnlyDictionary<string, string[]>? errors, JsonElement? details) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
    public JsonElement? Details { get; } = details;

    public string Describe() => Errors is { Count: > 0 } e ? string.Join(" ", e.Values.SelectMany(v => v)) : Message;
}

public sealed record SessionTokens(string AccessToken, DateTime AccessExpiresUtc, string RefreshToken, DateTime RefreshExpiresUtc, CurrentUserDto User);

/// <summary>
/// Server-side token store (backend-for-frontend). The browser only holds an HttpOnly cookie with an opaque session id;
/// API access and refresh tokens never reach the browser.
/// </summary>
public sealed class SessionStore(IDistributedCache cache)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public async Task<SessionTokens?> GetAsync(string sid)
    {
        var bytes = await cache.GetAsync(Key(sid));
        return bytes is null ? null : JsonSerializer.Deserialize<SessionTokens>(bytes, Json);
    }

    public Task SaveAsync(string sid, SessionTokens tokens) =>
        cache.SetAsync(Key(sid), JsonSerializer.SerializeToUtf8Bytes(tokens, Json), new DistributedCacheEntryOptions { AbsoluteExpiration = tokens.RefreshExpiresUtc });

    public Task RemoveAsync(string sid) => cache.RemoveAsync(Key(sid));

    public static SemaphoreSlim LockFor(string sid) => Locks.GetOrAdd(sid, _ => new SemaphoreSlim(1, 1));

    private static string Key(string sid) => "ss-session:" + sid;
}

/// <summary>Typed client for the Student Sync API. Refreshes the access token transparently (one refresh at a time per session).</summary>
public sealed class ApiClient(HttpClient http, SessionStore store, AuthenticationStateProvider auth)
{
    public static readonly JsonSerializerOptions Json = SessionStore.Json;
    private string? _sid;

    private async Task<string> SidAsync()
    {
        if (_sid is not null) return _sid;
        var state = await auth.GetAuthenticationStateAsync();
        return _sid = state.User.FindFirstValue("sid") ?? throw new ApiException(401, "unauthenticated", "Your session has ended. Please sign in again.", null, null);
    }

    public async Task<SessionTokens> TokensAsync()
    {
        var sid = await SidAsync();
        var tokens = await store.GetAsync(sid) ?? throw new ApiException(401, "unauthenticated", "Your session has ended. Please sign in again.", null, null);
        if (tokens.AccessExpiresUtc > DateTime.UtcNow.AddSeconds(60)) return tokens;

        var gate = SessionStore.LockFor(sid);
        await gate.WaitAsync();
        try
        {
            tokens = await store.GetAsync(sid) ?? throw new ApiException(401, "unauthenticated", "Your session has ended. Please sign in again.", null, null);
            if (tokens.AccessExpiresUtc > DateTime.UtcNow.AddSeconds(60)) return tokens;
            var response = await http.PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), Json);
            if (!response.IsSuccessStatusCode)
            {
                await store.RemoveAsync(sid);
                throw new ApiException(401, "unauthenticated", "Your session has expired. Please sign in again.", null, null);
            }
            var fresh = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
            tokens = new SessionTokens(fresh.AccessToken, fresh.AccessTokenExpiresAtUtc, fresh.RefreshToken, fresh.RefreshTokenExpiresAtUtc, fresh.User);
            await store.SaveAsync(sid, tokens);
            return tokens;
        }
        finally { gate.Release(); }
    }

    public async Task<CurrentUserDto> MeAsync() => (await TokensAsync()).User;

    /// <summary>Replaces the session's tokens (after a password change revokes the previous refresh token).</summary>
    public async Task ReplaceTokensAsync(AuthResponse auth)
    {
        var sid = await SidAsync();
        await store.SaveAsync(sid, new SessionTokens(auth.AccessToken, auth.AccessTokenExpiresAtUtc, auth.RefreshToken, auth.RefreshTokenExpiresAtUtc, auth.User));
    }

    /// <summary>Unauthenticated call used to re-establish tokens with new credentials.</summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var response = await http.PostAsJsonAsync("api/v1/auth/login", request, Json);
        if (!response.IsSuccessStatusCode) throw await ToExceptionAsync(response);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    public async Task UpdateCachedUserAsync(CurrentUserDto user)
    {
        var sid = await SidAsync();
        if (await store.GetAsync(sid) is { } t) await store.SaveAsync(sid, t with { User = user });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var tokens = await TokensAsync();
        using var request = new HttpRequestMessage(method, "api/v1/" + path.TrimStart('/')) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException) { throw new ApiException(503, "unavailable", "The Student Sync service is unreachable. Check your connection and try again.", null, null); }
        if (response.IsSuccessStatusCode) return response;
        throw await ToExceptionAsync(response);
    }

    public static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            var code = problem.TryGetProperty("code", out var c) ? c.GetString() ?? "error" : "error";
            var title = problem.TryGetProperty("title", out var t) ? t.GetString() ?? "Request failed." : "Request failed.";
            Dictionary<string, string[]>? errors = null;
            if (problem.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                errors = e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray());
            JsonElement? details = problem.TryGetProperty("details", out var d) ? d.Clone() : null;
            return new ApiException(status, code, title, errors, details);
        }
        catch (JsonException)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "Too many requests. Please wait a moment.",
                HttpStatusCode.Forbidden => "You do not have permission to do that.",
                HttpStatusCode.NotFound => "That item was not found.",
                _ => "The request failed. Please try again.",
            };
            return new ApiException(status, "error", message, null, null);
        }
    }

    private static JsonContent Body(object? body) => JsonContent.Create(body, options: Json);

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Get, path, null, ct);
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        using var r = await SendAsync(HttpMethod.Get, path, null, ct);
        return r.StatusCode == HttpStatusCode.NoContent ? null : await r.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    public async Task<string> GetStringAsync(string path, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Get, path, null, ct);
        return await r.Content.ReadAsStringAsync(ct);
    }

    public async Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Post, path, Body(body), ct);
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task PostAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, path, Body(body ?? new { }), ct);
    }

    public async Task<T> PutAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Put, path, Body(body), ct);
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task PutAsync(string path, object? body, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Put, path, Body(body), ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, path, null, ct);
    }

    public async Task<T> PostFormAsync<T>(string path, MultipartFormDataContent form, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Post, path, form, ct);
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task PostFormAsync(string path, MultipartFormDataContent form, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, path, form, ct);
    }

    public async Task<T> PutFormAsync<T>(string path, MultipartFormDataContent form, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Put, path, form, ct);
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task PutFormAsync(string path, MultipartFormDataContent form, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Put, path, form, ct);
    }

    public async Task<T> DeleteAsync<T>(string path, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Delete, path, null, ct);
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    /// <summary>Turns an API-relative file URL (e.g. a profile photo URL) into one the browser can load from the API.</summary>
    public static string? PublicUrl(string? apiRelativeUrl, string? publicApiBase)
        => apiRelativeUrl is null ? null : (publicApiBase ?? "").TrimEnd('/') + apiRelativeUrl;

    /// <summary>Returns a short-lived, user-bound download URL served directly by the API.</summary>
    public async Task<string> FileUrlAsync(Guid fileId, string publicApiBase)
    {
        var link = await GetAsync<FileLinkDto>($"files/{fileId}/link");
        return publicApiBase.TrimEnd('/') + link.Url;
    }
}
