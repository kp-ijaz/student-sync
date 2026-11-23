using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;

namespace StudentSync.Application.Fees;

/// <summary>
/// The centre's fee plans (packages it offers, optionally per class and syllabus). Admin only. A plan only pre-fills
/// "Add hours": each sale copies the hours and price, so changing or deleting a plan never rewrites what a student bought.
/// </summary>
public sealed class FeePlanService(IAppDbContext db, ICurrentUser user, AccessPolicy access, AuditService audit)
{
    public const int MaxPlans = 200;

    /// <summary>
    /// All plans, offered ones first, then by class, syllabus and hours. With <paramref name="studentId"/>: only the offered
    /// plans that fit that student (plans for any class/syllabus, or for the student's own class and syllabus).
    /// </summary>
    public async Task<List<FeePlanDto>> ListAsync(Guid? studentId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var currency = await CurrencyAsync(ct);
        var plans = db.FeePlans.AsQueryable();
        if (studentId is { } sid)
        {
            var student = await db.Students.Where(s => s.Id == sid).Select(s => new { s.GradeLevelId, s.CurriculumId }).FirstOrDefaultAsync(ct)
                          ?? throw new NotFoundException("Student");
            plans = plans.Where(p => p.IsActive && (p.GradeLevelId == null || p.GradeLevelId == student.GradeLevelId)
                                     && (p.CurriculumId == null || p.CurriculumId == student.CurriculumId));
        }
        var rows = await plans
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.GradeLevelId == null).ThenBy(p => p.Hours).ThenBy(p => p.Name)
            .Take(MaxPlans)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Hours,
                p.Price,
                p.GradeLevelId,
                p.CurriculumId,
                p.Notes,
                p.IsActive,
                Grade = db.GradeLevels.Where(g => g.Id == p.GradeLevelId).Select(g => g.Name).FirstOrDefault(),
                Syllabus = db.Curricula.Where(c => c.Id == p.CurriculumId).Select(c => c.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(p => new FeePlanDto(p.Id, p.Name, p.Hours, p.Price, FeePlan.PerHour(p.Hours, p.Price), currency,
            p.GradeLevelId, p.Grade, p.CurriculumId, p.Syllabus, p.Notes, p.IsActive)).ToList();
    }

    public async Task<Guid> SaveAsync(Guid? id, SaveFeePlanRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        try { StudentPackage.Validate(r.Hours, r.Price); }
        catch (DomainException e) { throw RequestValidationException.For(e.Code == "invalid_price" ? "price" : "hours", e.Message); }
        if (r.GradeLevelId is { } g && !await db.GradeLevels.AnyAsync(x => x.Id == g, ct)) throw RequestValidationException.For("gradeLevelId", "Class not found.");
        if (r.CurriculumId is { } c && !await db.Curricula.AnyAsync(x => x.Id == c, ct)) throw RequestValidationException.For("curriculumId", "Syllabus not found.");

        FeePlan plan;
        if (id is { } existing)
            plan = await db.FeePlans.FirstOrDefaultAsync(p => p.Id == existing, ct) ?? throw new NotFoundException("Fee plan");
        else
        {
            if (await db.FeePlans.CountAsync(ct) >= MaxPlans) throw new DomainException($"A centre can keep up to {MaxPlans} fee plans. Delete unused ones first.", "plan_limit");
            db.FeePlans.Add(plan = new FeePlan());
        }
        plan.Hours = r.Hours;
        plan.Price = decimal.Round(r.Price, 2);
        plan.Name = Text.Optional(r.Name, "name", 100) ?? $"{r.Hours:0.#} hours";
        plan.GradeLevelId = r.GradeLevelId;
        plan.CurriculumId = r.CurriculumId;
        plan.Notes = Text.Optional(r.Notes, "notes", 500);
        plan.IsActive = r.IsActive;
        var currency = await CurrencyAsync(ct);
        audit.Record(id is null ? "fees.plan_added" : "fees.plan_updated", "FeePlan", plan.Id,
            $"{plan.Name} · {plan.Hours:0.##} h · {plan.Price:0.##} {currency}{(plan.IsActive ? "" : " · hidden")}");
        await db.SaveChangesAsync(ct);
        return plan.Id;
    }

    /// <summary>Deletes a plan. Packages already added from it keep their own copy of the hours and price.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var plan = await db.FeePlans.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Fee plan");
        db.FeePlans.Remove(plan);
        audit.Record("fees.plan_deleted", "FeePlan", id, plan.Name);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> CurrencyAsync(CancellationToken ct) =>
        await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.CurrencyCode).FirstOrDefaultAsync(ct) ?? "USD";
}
