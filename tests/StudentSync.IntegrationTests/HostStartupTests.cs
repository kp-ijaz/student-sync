using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Api.Hosting;
using StudentSync.Application.Common;

namespace StudentSync.IntegrationTests;

/// <summary>Host composition checks that need no database.</summary>
public class HostStartupTests
{
    /// <summary>
    /// Development validates every service registration when the host is built. A leftover Hangfire registration
    /// made "Jobs:Mode=Inline" fail there, e.g. a one-off <c>seed-demo</c> run for an environment without Hangfire.
    /// </summary>
    [Fact]
    public void Inline_job_mode_passes_service_validation()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });
            b.UseSetting("ConnectionStrings:Default", "Server=unused.invalid;Database=StudentSync_Unused");
            b.UseSetting("Jobs:Mode", "Inline");
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Jwt:SigningKey", "host-startup-tests-signing-key-0123456789-abcdefgh");
        });

        using var scope = factory.Services.CreateScope();
        Assert.IsType<InlineBackgroundJobs>(scope.ServiceProvider.GetRequiredService<IBackgroundJobs>());
    }
}
