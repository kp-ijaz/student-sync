using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.People;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Seeding;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Behaviour that SQL Server gave for free and PostgreSQL needs explicitly: optimistic concurrency through the xmin
/// row version, unique violations surfacing as PostgreSQL error 23505, and case-insensitive search and duplicate checks.
/// </summary>
public sealed class PostgreSqlBehaviourTests(ApiFixture api)
{
    private (IServiceScope Scope, AppDbContext Db) TenantDb(Guid tenantId)
    {
        var scope = api.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId);
        return (scope, scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Guid> HorizonIdAsync()
    {
        using var scope = api.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug == DemoDataSeeder.PrimarySlug).Select(t => t.Id).SingleAsync();
    }

    [Fact]
    public async Task Row_version_changes_on_every_update_and_a_stale_save_is_rejected()
    {
        var tenantId = await HorizonIdAsync();
        var (s1, first) = TenantDb(tenantId);
        var (s2, second) = TenantDb(tenantId);
        using (s1)
        using (s2)
        {
            var grade = new GradeLevel { Name = $"Concurrency {Guid.NewGuid():N}"[..30], SortOrder = 900 };
            first.GradeLevels.Add(grade);
            await first.SaveChangesAsync();
            var created = grade.RowVersion;
            Assert.NotEqual(0u, created);

            var other = await second.GradeLevels.SingleAsync(g => g.Id == grade.Id);
            Assert.Equal(created, other.RowVersion);
            other.SortOrder = 901;
            await second.SaveChangesAsync();
            Assert.NotEqual(created, other.RowVersion);

            grade.SortOrder = 902; // `first` still holds the original version
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Unique_index_violations_surface_as_postgres_unique_violation()
    {
        var tenantId = await HorizonIdAsync();
        var (scope, db) = TenantDb(tenantId);
        using (scope)
        {
            var existing = await db.GradeLevels.Select(g => g.Name).FirstAsync();
            db.GradeLevels.Add(new GradeLevel { Name = existing, SortOrder = 999 });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
    }

    [Fact]
    public async Task Only_one_open_whole_subject_learning_need_per_student_and_subject()
    {
        var tenantId = await HorizonIdAsync();
        var (scope, db) = TenantDb(tenantId);
        using (scope)
        {
            // A student/subject pair with no open whole-subject need yet (NULL chapter = the whole subject).
            var pair = await (from s in db.Students
                              from sub in db.Subjects
                              where !db.LearningNeeds.Any(n => n.StudentId == s.Id && n.SubjectId == sub.Id && n.ChapterId == null)
                              select new { StudentId = s.Id, SubjectId = sub.Id }).FirstAsync();
            var adminId = await db.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id).FirstAsync();
            StudentLearningNeed Need() => new() { StudentId = pair.StudentId, SubjectId = pair.SubjectId, Source = LearningNeedSource.Admin, RecordedByUserId = adminId, Note = "PostgreSQL NULLS NOT DISTINCT (test)" };

            var first = Need();
            db.LearningNeeds.Add(first);
            await db.SaveChangesAsync();

            var duplicate = Need();
            db.LearningNeeds.Add(duplicate);
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
            db.Entry(duplicate).State = EntityState.Detached;

            // The rule covers open needs only: once resolved, the subject can be flagged again.
            first.Status = LearningNeedStatus.Resolved;
            var again = Need();
            db.LearningNeeds.Add(again);
            await db.SaveChangesAsync();

            db.LearningNeeds.RemoveRange(first, again);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Search_and_duplicate_name_checks_ignore_letter_case()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var aisha = (await admin.Get<PagedResult<StudentDto>>("students?search=Aisha")).Items.Single();
        Assert.Equal(aisha.Id, (await admin.Get<PagedResult<StudentDto>>("students?search=AISHA")).Items.Single().Id);

        var name = $"Grade Case {Guid.NewGuid():N}"[..24];
        await admin.Post<IdResponse>("academics/grades", new SaveGradeLevelRequest(name, 950));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("academics/grades", new SaveGradeLevelRequest(name.ToUpperInvariant(), 951))).StatusCode);
    }
}
