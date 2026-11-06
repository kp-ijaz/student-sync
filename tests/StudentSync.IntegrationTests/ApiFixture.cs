using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Contracts;
using StudentSync.Infrastructure.Seeding;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(StudentSync.IntegrationTests.ApiFixture))]

namespace StudentSync.IntegrationTests;

/// <summary>
/// Runs the real API against SQL Server 2022 with the demo dataset. Uses Testcontainers by default; set
/// STUDENTSYNC_TEST_SQL to an existing server's connection string (without a database name) to reuse a running server.
/// Each run gets its own uniquely named database, which is dropped afterwards.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Password = "Integration-Test-Pass1";
    public static readonly JsonSerializerOptions Json = CreateJson();
    private MsSqlContainer? _container;
    private string _connection = "";
    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    private static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public async ValueTask InitializeAsync()
    {
        var database = $"StudentSync_Test_{Guid.NewGuid():N}";
        var external = Environment.GetEnvironmentVariable("STUDENTSYNC_TEST_SQL");
        if (!string.IsNullOrWhiteSpace(external))
            _connection = $"{external.TrimEnd(';')};Database={database};TrustServerCertificate=True";
        else
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU16-ubuntu-22.04").Build();
            await _container.StartAsync();
            _connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", _connection);
            b.UseSetting("Jobs:Mode", "Inline");
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789-abcdefghijklmnop");
            b.UseSetting("RateLimits:AuthPerMinute", "10000");
            b.UseSetting("Storage:Local:LocalRootPath", Path.Combine(Path.GetTempPath(), "ss-test-storage-" + database));
            b.UseSetting("Email:Smtp:Host", "");
        });
        _ = Factory.CreateClient(); // starts the host, migrates and seeds reference data

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(new DemoOptions { Password = Password, ForcePasswordChange = false }, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var scope = Factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<StudentSync.Infrastructure.Persistence.AppDbContext>().Database.EnsureDeletedAsync();
        }
        catch { /* best effort */ }
        await Factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    public async Task<Client> LoginAsync(string email, string? password = null, string? institution = null)
    {
        var http = Factory.CreateClient();
        var r = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password ?? Password, institution, "tests"), Json);
        Assert.True(r.IsSuccessStatusCode, $"Login failed for {email}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        var auth = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new Client(http, auth);
    }

    public HttpClient Anonymous() => Factory.CreateClient();
}

public sealed record Client(HttpClient Http, AuthResponse Auth)
{
    public Guid UserId => Auth.User.Id;

    public async Task<T> Get<T>(string path)
    {
        var r = await Http.GetAsync("/api/v1/" + path);
        await Ensure(r, path);
        return (await r.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public Task<HttpResponseMessage> GetRaw(string path) => Http.GetAsync("/api/v1/" + path);
    public Task<HttpResponseMessage> PostRaw(string path, object? body) => Http.PostAsJsonAsync("/api/v1/" + path, body ?? new { }, ApiFixture.Json);
    public Task<HttpResponseMessage> PutRaw(string path, object? body) => Http.PutAsJsonAsync("/api/v1/" + path, body, ApiFixture.Json);

    public async Task<T> Post<T>(string path, object? body)
    {
        var r = await PostRaw(path, body);
        await Ensure(r, path);
        return (await r.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public async Task Post(string path, object? body = null) => await Ensure(await PostRaw(path, body), path);
    public async Task Put(string path, object? body) => await Ensure(await PutRaw(path, body), path);

    private static async Task Ensure(HttpResponseMessage r, string path)
    {
        if (!r.IsSuccessStatusCode) Assert.Fail($"{r.RequestMessage?.Method} {path} → {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    public static async Task<string> Code(HttpResponseMessage r)
    {
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
    }
}

public static class Accounts
{
    public const string Admin = "admin@horizon.example.test";
    public const string Amira = "amira@horizon.example.test";       // batch maths teacher
    public const string Priya = "priya@horizon.example.test";       // one-to-one chemistry
    public const string Daniel = "daniel@horizon.example.test";
    public const string Aisha = "aisha@horizon.example.test";       // batch student, parent.khan's child
    public const string Ben = "ben@horizon.example.test";
    public const string Fatima = "fatima@horizon.example.test";     // one-to-one chemistry student
    public const string Hiro = "hiro@horizon.example.test";         // in no batch
    public const string ParentKhan = "parent.khan@horizon.example.test";
    public const string ParentAlMansoori = "parent.almansoori@horizon.example.test";
    public const string OtherAdmin = "admin@northbridge.example.test";
    public const string OtherTeacher = "teacher@northbridge.example.test";
    public const string SuperAdmin = "superadmin@studentsync.example.test";
}

public static class Http
{
    public static void Status(HttpStatusCode expected, HttpResponseMessage r) => Assert.Equal(expected, r.StatusCode);
}
