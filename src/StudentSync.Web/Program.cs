using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using StudentSync.Contracts;
using StudentSync.Web.Components;
using StudentSync.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Cookie and antiforgery keys; persisted when a path is configured so restarts don't sign everyone out.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("StudentSync.Web");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath) dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "ss_session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.LoginPath = "/login";
    o.LogoutPath = "/account/logout";
    o.AccessDeniedPath = "/forbidden";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
    // The cookie is only valid while its server-side token session exists.
    o.Events.OnValidatePrincipal = async ctx =>
    {
        var sid = ctx.Principal?.FindFirstValue("sid");
        var store = ctx.HttpContext.RequestServices.GetRequiredService<SessionStore>();
        if (sid is null || await store.GetAsync(sid) is null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});
builder.Services.AddAuthorization();

// Development uses an in-memory session store; configure a distributed cache (Redis/SQL Server) for multiple web nodes.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddHttpClient<ApiClient>(c => c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/"));
builder.Services.AddHttpClient("api-anon", c => c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080/"));
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ShellState>();
builder.Services.AddScoped<RealtimeService>();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health/live");

// ---------- Sign-in / sign-out (plain form posts so the session cookie can be set) ----------
app.MapPost("/account/login", async ([FromForm] string email, [FromForm] string password, [FromForm] string? institutionCode, [FromForm] string? returnUrl,
    HttpContext http, IHttpClientFactory factory, SessionStore store) =>
{
    var client = factory.CreateClient("api-anon");
    var safeReturn = !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/";
    HttpResponseMessage response;
    try
    {
        response = await client.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(email, password, institutionCode, "web"), ApiClient.Json);
    }
    catch (HttpRequestException)
    {
        return Results.Redirect($"/login?error=unavailable&email={Uri.EscapeDataString(email)}&returnUrl={Uri.EscapeDataString(safeReturn)}");
    }
    if (!response.IsSuccessStatusCode)
    {
        var error = await ApiClient.ToExceptionAsync(response);
        return Results.Redirect($"/login?error={Uri.EscapeDataString(error.Code)}&email={Uri.EscapeDataString(email)}&institution={Uri.EscapeDataString(institutionCode ?? "")}&returnUrl={Uri.EscapeDataString(safeReturn)}");
    }
    var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiClient.Json))!;
    var sid = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    await store.SaveAsync(sid, new SessionTokens(auth.AccessToken, auth.AccessTokenExpiresAtUtc, auth.RefreshToken, auth.RefreshTokenExpiresAtUtc, auth.User));
    var identity = new ClaimsIdentity([new Claim("sid", sid), new Claim(ClaimTypes.Name, auth.User.FullName), new Claim(ClaimTypes.Role, auth.User.Role.ToString()),
        new Claim(ClaimTypes.NameIdentifier, auth.User.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    if (auth.User.MustChangePassword) return Results.Redirect("/change-password");
    return Results.Redirect(safeReturn == "/" ? Navigation.Home(auth.User.Role) : safeReturn);
}).AllowAnonymous();

app.MapPost("/account/logout", async (HttpContext http, SessionStore store, IHttpClientFactory factory) =>
{
    if (http.User.FindFirstValue("sid") is { } sid)
    {
        if (await store.GetAsync(sid) is { } tokens)
        {
            try { await factory.CreateClient("api-anon").PostAsJsonAsync("api/v1/auth/logout", new LogoutRequest(tokens.RefreshToken)); }
            catch (HttpRequestException) { /* token expires on its own */ }
        }
        await store.RemoveAsync(sid);
    }
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login?signedOut=1");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
