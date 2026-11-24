using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Fees;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Marketing;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Marketing;

/// <summary>
/// Marketing (owner request 2026-11-24): enquiries (leads) with their calls and follow-ups, campaigns with their spend,
/// and the figures that tie them together. Used by admins and the marketing team (<see cref="Permissions.MarketingManage"/>).
/// The marketing team never reads student, fee or class data through this service: a lead is its own small record, and
/// linking it to an admitted student is an admin action.
/// </summary>
public sealed class MarketingService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, CentreService centre)
{
    public const int MaxCampaigns = 500;

    private void Require() => access.Require(Permissions.MarketingManage);

    /// <summary>A lead still being worked on (not admitted, not lost) — the same test as <see cref="MarketingRules.IsOpen"/>, for queries.</summary>
    public static readonly System.Linq.Expressions.Expression<Func<Lead, bool>> OpenLead = l => l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested;

    /// <summary>
    /// Whether this family already has an open lead for this child: same name (any letter case) and the same phone number
    /// compared by its last ten digits. Only leads with that name are read, not every open lead.
    /// </summary>
    public static async Task<bool> OpenLeadExistsAsync(IAppDbContext db, string studentName, string phone, CancellationToken ct)
    {
        var name = studentName.Trim().ToLower();
        var key = MarketingRules.PhoneKey(phone);
        var phones = await db.Leads.Where(OpenLead).Where(l => l.StudentName.ToLower() == name).Select(l => l.Phone).Take(50).ToListAsync(ct);
        return phones.Any(p => MarketingRules.PhoneKey(p) == key);
    }

    // ---------------- Options ----------------

    public async Task<MarketingOptionsDto> OptionsAsync(CancellationToken ct)
    {
        Require();
        var (currency, _) = await centre.GetAsync(ct);
        var grades = await db.GradeLevels.OrderBy(g => g.SortOrder).Select(g => new LookupItem(g.Id, g.Name, null)).ToListAsync(ct);
        var curricula = await db.Curricula.Where(c => c.Status == Domain.Academics.CatalogStatus.Active).OrderBy(c => c.Name).Select(c => new LookupItem(c.Id, c.Name, null)).ToListAsync(ct);
        var campaigns = await db.Campaigns.OrderByDescending(c => c.StartsOn).Take(MaxCampaigns).Select(c => new LookupItem(c.Id, c.Name, c.Status.ToString())).ToListAsync(ct);
        return new MarketingOptionsDto(grades, curricula, campaigns, await TeamLookupAsync(ct), currency);
    }

    /// <summary>People a lead can be given to: active marketing users and admins of this institution.</summary>
    private async Task<List<LookupItem>> TeamLookupAsync(CancellationToken ct) =>
        await db.Users.Where(u => u.TenantId == user.TenantId && u.IsActive && (u.Role == UserRole.Marketing || u.Role == UserRole.InstitutionAdmin))
            .OrderBy(u => u.FullName).Select(u => new LookupItem(u.Id, u.FullName, u.Role == UserRole.Marketing ? "Marketing" : "Admin")).ToListAsync(ct);

    // ---------------- Leads ----------------

    public async Task<PagedResult<LeadDto>> LeadsAsync(LeadQuery q, CancellationToken ct)
    {
        Require();
        var today = await centre.TodayAsync(ct);
        var leads = Filter(db.Leads.AsQueryable(), q, today);
        var total = await leads.CountAsync(ct);
        leads = (q.Sort ?? LeadSort.Newest) switch
        {
            LeadSort.Oldest => leads.OrderBy(l => l.EnquiredOn).ThenBy(l => l.CreatedAtUtc),
            LeadSort.Name => leads.OrderBy(l => l.StudentName).ThenByDescending(l => l.EnquiredOn),
            // Leads with a follow-up day first (earliest first), then the rest, newest first.
            LeadSort.FollowUp => string.Equals(q.Trial, "upcoming", StringComparison.OrdinalIgnoreCase)
                ? leads.OrderBy(l => l.TrialAtUtc)
                : leads.OrderBy(l => l.NextFollowUpOn == null).ThenBy(l => l.NextFollowUpOn).ThenByDescending(l => l.EnquiredOn),
            _ => leads.OrderByDescending(l => l.EnquiredOn).ThenByDescending(l => l.CreatedAtUtc),
        };
        var page = await Project(leads.Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize), today).ToListAsync(ct);
        return new PagedResult<LeadDto>(page, q.SafePage, q.SafePageSize, total);
    }

    private IQueryable<Lead> Filter(IQueryable<Lead> leads, LeadQuery q, DateOnly today)
    {
        if (q.Status is { } st) leads = leads.Where(l => l.Status == st);
        if (q.Open is { } open)
            leads = open ? leads.Where(OpenLead)
                : leads.Where(l => l.Status == LeadStatus.Admitted || l.Status == LeadStatus.NotInterested);
        if (q.Source is { } src) leads = leads.Where(l => l.Source == src);
        if (q.CampaignId is { } cid) leads = leads.Where(l => l.CampaignId == cid);
        if (q.AssignedToUserId is { } uid) leads = uid == Guid.Empty ? leads.Where(l => l.AssignedToUserId == null) : leads.Where(l => l.AssignedToUserId == uid);
        if (q.From is { } from) leads = leads.Where(l => l.EnquiredOn >= from);
        if (q.To is { } to) leads = leads.Where(l => l.EnquiredOn <= to);
        var week = today.AddDays(7);
        leads = q.FollowUp?.ToLowerInvariant() switch
        {
            "today" => leads.Where(l => l.NextFollowUpOn == today && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested),
            "overdue" => leads.Where(l => l.NextFollowUpOn < today && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested),
            "week" => leads.Where(l => l.NextFollowUpOn >= today && l.NextFollowUpOn <= week && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested),
            "due" => leads.Where(l => l.NextFollowUpOn <= today && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested),
            _ => leads,
        };
        if (string.Equals(q.Trial, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            var now = clock.UtcNow.AddHours(-1); // a trial that started within the last hour still counts as "now"
            leads = leads.Where(l => l.TrialAtUtc >= now && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested);
        }
        if (q.WebsiteNew == true) leads = leads.Where(l => l.FromWebsiteForm && l.Status == LeadStatus.New);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLowerInvariant();
            if (term.Length > 100) throw RequestValidationException.For("search", "Search for at most 100 characters.");
            var digits = new string(term.Where(char.IsDigit).ToArray());
            leads = leads.Where(l => l.StudentName.ToLower().Contains(term) || (l.GuardianName != null && l.GuardianName.ToLower().Contains(term))
                || l.Phone.Contains(term) || (digits.Length >= 4 && l.Phone.Replace(" ", "").Replace("-", "").Contains(digits))
                || (l.SchoolName != null && l.SchoolName.ToLower().Contains(term)) || (l.Interest != null && l.Interest.ToLower().Contains(term))
                || (l.ReferredBy != null && l.ReferredBy.ToLower().Contains(term)));
        }
        return leads;
    }

    private IQueryable<LeadDto> Project(IQueryable<Lead> leads, DateOnly today) => leads.Select(l => new LeadDto(
        l.Id, l.StudentName, l.GuardianName, l.Phone, l.Email, l.GradeLevelId, db.GradeLevels.Where(g => g.Id == l.GradeLevelId).Select(g => g.Name).FirstOrDefault(),
        l.CurriculumId, db.Curricula.Where(c => c.Id == l.CurriculumId).Select(c => c.Name).FirstOrDefault(), l.SchoolName, l.Interest, l.Source,
        l.CampaignId, l.Campaign!.Name, l.Status, l.LostReason, l.AssignedToUserId,
        db.Users.Where(u => u.Id == l.AssignedToUserId).Select(u => u.FullName).FirstOrDefault(), l.NextFollowUpOn, l.Notes, l.EnquiredOn, l.ConvertedStudentId,
        l.Activities.Count(a => a.Kind == LeadActivityKind.Call), l.Activities.Max(a => (DateTime?)a.OccurredAtUtc),
        l.NextFollowUpOn < today && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested,
        l.ReferredBy, l.TrialAtUtc, l.TrialNotes, l.FromWebsiteForm));

    public async Task<LeadDetailDto> LeadAsync(Guid id, CancellationToken ct)
    {
        Require();
        var (_, tz) = await centre.GetAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var lead = await Project(db.Leads.Where(l => l.Id == id), today).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Lead");
        var me = user.UserId;
        var admin = access.IsAdmin;
        var activities = await db.LeadActivities.Where(a => a.LeadId == id).OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.CreatedAtUtc).Take(200)
            .Select(a => new LeadActivityDto(a.Id, a.Kind, a.Outcome, a.OccurredAtUtc, a.DurationMinutes, a.Notes, a.ByUserId,
                db.Users.Where(u => u.Id == a.ByUserId).Select(u => u.FullName).FirstOrDefault() ?? "", admin || a.ByUserId == me)).ToListAsync(ct);
        return new LeadDetailDto(lead, activities, tz);
    }

    public async Task<Guid> SaveLeadAsync(Guid? id, SaveLeadRequest r, CancellationToken ct)
    {
        Require();
        var today = await centre.TodayAsync(ct);
        var phone = Text.Required(r.Phone, "phone", 30);
        if (MarketingRules.PhoneKey(phone).Length < 6) throw RequestValidationException.For("phone", "Enter a phone number with at least 6 digits.");
        if (!Enum.IsDefined(r.Source)) throw RequestValidationException.For("source", "Choose where the enquiry came from.");
        if (!Enum.IsDefined(r.Status)) throw RequestValidationException.For("status", "Choose a status.");
        if (r.Email is { Length: > 0 } email && (!System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _) || email.Length > 256))
            throw RequestValidationException.For("email", "Enter a valid email address.");
        if (r.GradeLevelId is { } g && !await db.GradeLevels.AnyAsync(x => x.Id == g, ct)) throw RequestValidationException.For("gradeLevelId", "Class not found.");
        if (r.CurriculumId is { } c && !await db.Curricula.AnyAsync(x => x.Id == c, ct)) throw RequestValidationException.For("curriculumId", "Syllabus not found.");
        if (r.CampaignId is { } camp && !await db.Campaigns.AnyAsync(x => x.Id == camp, ct)) throw RequestValidationException.For("campaignId", "Campaign not found.");
        if (r.AssignedToUserId is { } assignee && !(await TeamLookupAsync(ct)).Any(t => t.Id == assignee))
            throw RequestValidationException.For("assignedToUserId", "Choose someone from the marketing team.");
        var enquired = r.EnquiredOn ?? today;
        if (enquired > today) throw RequestValidationException.For("enquiredOn", "The enquiry day cannot be in the future.");
        if (enquired < today.AddYears(-5)) throw RequestValidationException.For("enquiredOn", "The enquiry day must be within the last five years.");
        if (r.NextFollowUpOn is { } follow && follow > today.AddYears(2)) throw RequestValidationException.For("nextFollowUpOn", "Choose a follow-up day within two years.");
        var trialAt = r.TrialAtUtc is { } tr ? DateTime.SpecifyKind(tr, DateTimeKind.Utc) : (DateTime?)null;
        if (trialAt is { } ta && Math.Abs((ta - clock.UtcNow).TotalDays) > 366) throw RequestValidationException.For("trialAtUtc", "Choose a trial class time within a year of today.");

        Lead lead;
        if (id is { } lid) lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == lid, ct) ?? throw new NotFoundException("Lead");
        else
        {
            db.Leads.Add(lead = new Lead());
            // The same phone number already on an open lead is almost always the same family calling again.
            if (await OpenLeadExistsAsync(db, r.StudentName ?? "", phone, ct))
                throw RequestValidationException.For("phone", "An open lead with this name and phone number already exists.");
        }
        var before = lead.Status;
        lead.StudentName = Text.Required(r.StudentName, "studentName", 150);
        lead.GuardianName = Text.Optional(r.GuardianName, "guardianName", 150);
        lead.Phone = phone;
        lead.Email = Text.Optional(r.Email, "email", 256);
        lead.GradeLevelId = r.GradeLevelId;
        lead.CurriculumId = r.CurriculumId;
        lead.SchoolName = Text.Optional(r.SchoolName, "schoolName", 200);
        lead.Interest = Text.Optional(r.Interest, "interest", 300);
        lead.Source = r.Source;
        lead.CampaignId = r.CampaignId;
        lead.AssignedToUserId = r.AssignedToUserId;
        lead.Notes = Text.Optional(r.Notes, "notes", 2000);
        lead.EnquiredOn = enquired;
        lead.ReferredBy = Text.Optional(r.ReferredBy, "referredBy", 150);
        lead.TrialAtUtc = trialAt;
        lead.TrialNotes = trialAt is null ? null : Text.Optional(r.TrialNotes, "trialNotes", 300);
        lead.LostReason = r.Status == LeadStatus.NotInterested ? Text.Optional(r.LostReason, "lostReason", 300) : null;
        lead.NextFollowUpOn = r.NextFollowUpOn;
        ApplyStatus(lead, r.Status);
        audit.Record(id is null ? "marketing.lead_added" : "marketing.lead_updated", "Lead", lead.Id,
            id is null || before == lead.Status ? $"{lead.Source} · {lead.Status}" : $"{before} -> {lead.Status}");
        await db.SaveChangesAsync(ct);
        return lead.Id;
    }

    /// <summary>A closed lead has no follow-up left; reopening one clears the link made when it was admitted.</summary>
    private void ApplyStatus(Lead lead, LeadStatus status)
    {
        if (lead.Status == LeadStatus.Admitted && status != LeadStatus.Admitted) lead.ConvertedStudentId = null;
        lead.Status = status;
        if (MarketingRules.IsOpen(status)) lead.ClosedAtUtc = null;
        else
        {
            lead.NextFollowUpOn = null;
            lead.ClosedAtUtc ??= clock.UtcNow;
        }
    }

    public async Task DeleteLeadAsync(Guid id, CancellationToken ct)
    {
        Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lead");
        db.Leads.Remove(lead); // its contact log goes with it (cascade)
        audit.Record("marketing.lead_deleted", "Lead", id, $"{lead.Source} · {lead.Status}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Admin only (students are the admin's records): links the lead to the admitted student and marks it Admitted.</summary>
    public async Task LinkStudentAsync(Guid id, LinkLeadStudentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lead");
        if (!await db.Students.AnyAsync(s => s.Id == r.StudentId, ct)) throw RequestValidationException.For("studentId", "Student not found.");
        if (await db.Leads.AnyAsync(l => l.ConvertedStudentId == r.StudentId && l.Id != id, ct))
            throw RequestValidationException.For("studentId", "This student is already linked to another lead.");
        ApplyStatus(lead, LeadStatus.Admitted);
        lead.ConvertedStudentId = r.StudentId;
        audit.Record("marketing.lead_admitted", "Lead", id, "Linked to the admitted student");
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Calls and other contacts ----------------

    public async Task<Guid> SaveActivityAsync(Guid leadId, Guid? activityId, SaveLeadActivityRequest r, CancellationToken ct)
    {
        Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct) ?? throw new NotFoundException("Lead");
        if (!Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Outcome)) throw RequestValidationException.For("kind", "Choose what happened.");
        if (r.DurationMinutes is < 0 or > 600) throw RequestValidationException.For("durationMinutes", "Enter the length in minutes (up to 600).");
        var now = clock.UtcNow;
        var at = r.OccurredAtUtc is { } o ? DateTime.SpecifyKind(o, DateTimeKind.Utc) : now;
        if (at > now.AddMinutes(10)) throw RequestValidationException.For("occurredAtUtc", "The time cannot be in the future.");
        if (at < now.AddYears(-5)) throw RequestValidationException.For("occurredAtUtc", "The time must be within the last five years.");
        var today = await centre.TodayAsync(ct);
        if (r.NextFollowUpOn is { } follow && (follow < today || follow > today.AddYears(2))) throw RequestValidationException.For("nextFollowUpOn", "Choose a follow-up day from today on.");

        LeadActivity activity;
        if (activityId is { } aid)
        {
            activity = await db.LeadActivities.FirstOrDefaultAsync(a => a.Id == aid && a.LeadId == leadId, ct) ?? throw new NotFoundException("Entry");
            if (!access.IsAdmin && activity.ByUserId != user.UserId) throw new ForbiddenException("You can change only what you logged yourself.");
        }
        else db.LeadActivities.Add(activity = new LeadActivity { LeadId = leadId, ByUserId = user.UserId });
        activity.Kind = r.Kind;
        activity.Outcome = r.Outcome;
        activity.OccurredAtUtc = at;
        activity.DurationMinutes = r.DurationMinutes;
        activity.Notes = Text.Optional(r.Notes, "notes", 1000);

        // The same step moves the lead along: a first contact makes a New lead Contacted unless a status was chosen.
        var status = r.SetStatus ?? (lead.Status == LeadStatus.New && r.Kind != LeadActivityKind.Note ? LeadStatus.Contacted : lead.Status);
        if (!Enum.IsDefined(status)) throw RequestValidationException.For("setStatus", "Choose a status.");
        if (r.NextFollowUpOn is { } next) lead.NextFollowUpOn = next;
        else if (r.ClearFollowUp) lead.NextFollowUpOn = null;
        if (status != lead.Status) ApplyStatus(lead, status);
        audit.Record(activityId is null ? "marketing.contact_logged" : "marketing.contact_updated", "Lead", leadId, $"{r.Kind} · {r.Outcome}");
        await db.SaveChangesAsync(ct);
        return activity.Id;
    }

    public async Task DeleteActivityAsync(Guid leadId, Guid activityId, CancellationToken ct)
    {
        Require();
        var activity = await db.LeadActivities.FirstOrDefaultAsync(a => a.Id == activityId && a.LeadId == leadId, ct) ?? throw new NotFoundException("Entry");
        if (!access.IsAdmin && activity.ByUserId != user.UserId) throw new ForbiddenException("You can remove only what you logged yourself.");
        db.LeadActivities.Remove(activity);
        audit.Record("marketing.contact_deleted", "Lead", leadId, $"{activity.Kind} · {activity.Outcome}");
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Campaigns ----------------

    public async Task<List<CampaignDto>> CampaignsAsync(CancellationToken ct)
    {
        Require();
        return await CampaignRowsAsync(db.Campaigns.AsQueryable(), null, null, ct);
    }

    /// <summary>Campaigns with their leads; with dates, only leads enquired in that period are counted.</summary>
    private async Task<List<CampaignDto>> CampaignRowsAsync(IQueryable<Campaign> campaigns, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var rows = await campaigns.OrderByDescending(c => c.StartsOn).ThenBy(c => c.Name).Take(MaxCampaigns).ToListAsync(ct);
        var ids = rows.Select(c => c.Id).ToList();
        var leads = db.Leads.Where(l => l.CampaignId != null && ids.Contains(l.CampaignId.Value));
        if (from is { } f) leads = leads.Where(l => l.EnquiredOn >= f);
        if (to is { } t) leads = leads.Where(l => l.EnquiredOn <= t);
        var counts = (await leads.GroupBy(l => l.CampaignId!.Value).Select(g => new { g.Key, Leads = g.Count(), Admitted = g.Count(l => l.Status == LeadStatus.Admitted) }).ToListAsync(ct))
            .ToDictionary(x => x.Key);
        // Spent = the expenses recorded against the campaign (in the period, when one is given).
        var spend = db.Expenses.Where(e => e.CampaignId != null && ids.Contains(e.CampaignId.Value));
        if (from is { } sf) spend = spend.Where(e => e.SpentOn >= sf);
        if (to is { } st) spend = spend.Where(e => e.SpentOn <= st);
        var spent = (await spend.GroupBy(e => e.CampaignId!.Value).Select(g => new { g.Key, Total = g.Sum(e => e.Amount) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Total);
        return rows.Select(c =>
        {
            var n = counts.GetValueOrDefault(c.Id);
            var (leadCount, admitted, paid) = (n?.Leads ?? 0, n?.Admitted ?? 0, spent.GetValueOrDefault(c.Id));
            return new CampaignDto(c.Id, c.Name, c.Channel, c.Location, c.StartsOn, c.EndsOn, c.Budget, paid, c.CurrencyCode, c.Status, c.Notes, leadCount, admitted,
                MarketingRules.CostPer(paid, leadCount), MarketingRules.CostPer(paid, admitted), MarketingRules.Percent(admitted, leadCount));
        }).ToList();
    }

    public async Task<Guid> SaveCampaignAsync(Guid? id, SaveCampaignRequest r, CancellationToken ct)
    {
        Require();
        if (!Enum.IsDefined(r.Channel) || !Enum.IsDefined(r.Status)) throw RequestValidationException.For("channel", "Choose a channel and a status.");
        if (r.Budget is < 0 or > 100_000_000) throw RequestValidationException.For("budget", "Enter a valid budget.");
        if (r.EndsOn is { } end && end < r.StartsOn) throw RequestValidationException.For("endsOn", "The campaign ends before it starts.");
        var today = await centre.TodayAsync(ct);
        if (Math.Abs(r.StartsOn.DayNumber - today.DayNumber) > 366 * 5) throw RequestValidationException.For("startsOn", "Choose a start day within five years of today.");
        var name = Text.Required(r.Name, "name", 150);
        if (await db.Campaigns.AnyAsync(c => c.Name.ToLower() == name.ToLowerInvariant() && c.Id != id, ct)) throw RequestValidationException.For("name", "A campaign with this name already exists.");

        Campaign c;
        if (id is { } cid) c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException("Campaign");
        else
        {
            if (await db.Campaigns.CountAsync(ct) >= MaxCampaigns) throw new DomainException($"Up to {MaxCampaigns} campaigns can be kept. Delete old ones first.", "campaign_limit");
            var (currency, _) = await centre.GetAsync(ct);
            db.Campaigns.Add(c = new Campaign { CurrencyCode = currency });
        }
        (c.Name, c.Channel, c.Location, c.StartsOn, c.EndsOn) = (name, r.Channel, Text.Optional(r.Location, "location", 200), r.StartsOn, r.EndsOn);
        (c.Budget, c.Status, c.Notes) = (decimal.Round(r.Budget, 2), r.Status, Text.Optional(r.Notes, "notes", 1000));
        audit.Record(id is null ? "marketing.campaign_added" : "marketing.campaign_updated", "Campaign", c.Id, $"{c.Name} · {c.Channel} · budget {c.Budget:0.##}");
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    // ---- Campaign spend: each amount is an expense (category Marketing), so Finance counts the same money once ----

    public async Task<List<CampaignSpendDto>> SpendAsync(Guid campaignId, CancellationToken ct)
    {
        Require();
        if (!await db.Campaigns.AnyAsync(c => c.Id == campaignId, ct)) throw new NotFoundException("Campaign");
        return await db.Expenses.Where(e => e.CampaignId == campaignId).OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.CreatedAtUtc).Take(200)
            .Select(e => new CampaignSpendDto(e.Id, e.SpentOn, e.Amount, e.Description, e.Method, e.Reference,
                db.Users.Where(u => u.Id == e.RecordedByUserId).Select(u => u.FullName).FirstOrDefault())).ToListAsync(ct);
    }

    public async Task<Guid> AddSpendAsync(Guid campaignId, AddCampaignSpendRequest r, CancellationToken ct)
    {
        Require();
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct) ?? throw new NotFoundException("Campaign");
        try { Domain.Fees.Expense.Validate(r.Amount); }
        catch (DomainException ex) { throw RequestValidationException.For("amount", ex.Message); }
        if (!Enum.IsDefined(r.Method) || r.Method == Domain.Fees.PaymentMethod.Online) throw RequestValidationException.For("method", "Choose how it was paid.");
        var today = await centre.TodayAsync(ct);
        var day = r.SpentOn ?? today;
        if (day > today) throw RequestValidationException.For("spentOn", "The day cannot be in the future.");
        if (day < today.AddYears(-5)) throw RequestValidationException.For("spentOn", "The day must be within the last five years.");
        var expense = new Domain.Fees.Expense
        {
            SpentOn = day,
            Category = Domain.Fees.ExpenseCategory.Marketing,
            Amount = decimal.Round(r.Amount, 2),
            CurrencyCode = c.CurrencyCode,
            Description = Text.Optional(r.Description, "description", 300) ?? c.Name,
            PaidTo = MarketingLabels.Source(c.Channel),
            Method = r.Method,
            Reference = Text.Optional(r.Reference, "reference", 100),
            RecordedByUserId = user.UserId,
            CampaignId = campaignId,
        };
        db.Expenses.Add(expense);
        audit.Record("marketing.campaign_spend_added", "Campaign", campaignId, $"{c.Name} · {expense.Amount:0.##} {c.CurrencyCode} on {day:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
        return expense.Id;
    }

    /// <summary>Removes a spend entry: the admin any, the team only what they recorded themselves.</summary>
    public async Task DeleteSpendAsync(Guid campaignId, Guid expenseId, CancellationToken ct)
    {
        Require();
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == expenseId && x.CampaignId == campaignId, ct) ?? throw new NotFoundException("Spend entry");
        if (!access.IsAdmin && e.RecordedByUserId != user.UserId) throw new ForbiddenException("You can remove only what you recorded yourself.");
        db.Expenses.Remove(e);
        audit.Record("marketing.campaign_spend_deleted", "Campaign", campaignId, $"{e.Amount:0.##} {e.CurrencyCode} on {e.SpentOn:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a campaign; its leads stay and simply lose the campaign (their source is kept). Money already recorded stays in Finance.</summary>
    public async Task DeleteCampaignAsync(Guid id, CancellationToken ct)
    {
        Require();
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Campaign");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Leads.Where(l => l.CampaignId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.CampaignId, (Guid?)null), ct);
        db.Campaigns.Remove(c);
        audit.Record("marketing.campaign_deleted", "Campaign", id, c.Name);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------------- Overview ----------------

    public async Task<MarketingOverviewDto> OverviewAsync(DateOnly? fromDay, DateOnly? toDay, CancellationToken ct)
    {
        Require();
        var (currency, tz) = await centre.GetAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var (from, to) = FinanceService.Range(fromDay, toDay, today);
        var (prevFrom, prevTo) = FinanceService.PreviousPeriod(from, to);

        var leads = await db.Leads.Where(l => l.EnquiredOn >= prevFrom && l.EnquiredOn <= to)
            .Select(l => new { l.EnquiredOn, l.Status, l.Source, l.AssignedToUserId, l.CreatedByUserId, l.ReferredBy }).ToListAsync(ct);
        var inPeriod = leads.Where(l => l.EnquiredOn >= from).ToList();
        var previous = leads.Where(l => l.EnquiredOn < from).ToList();

        // Contacts are dated by the centre's local day.
        var fromUtc = ZonedTime.ToUtc(prevFrom, TimeOnly.MinValue, tz).Utc;
        var toUtc = ZonedTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, tz).Utc;
        var contacts = (await db.LeadActivities.Where(a => a.OccurredAtUtc >= fromUtc && a.OccurredAtUtc < toUtc && a.Kind != LeadActivityKind.Note)
                .Select(a => new { a.Kind, a.Outcome, a.OccurredAtUtc, a.ByUserId }).ToListAsync(ct))
            .Select(a => new { a.Kind, a.Outcome, a.ByUserId, Day = DateOnly.FromDateTime(ZonedTime.ToLocal(a.OccurredAtUtc, tz)) }).ToList();
        var now = contacts.Where(a => a.Day >= from && a.Day <= to).ToList();
        var calls = now.Where(a => a.Kind == LeadActivityKind.Call).ToList();
        var reached = calls.Count(a => MarketingRules.Reached(a.Outcome));

        var admitted = inPeriod.Count(l => l.Status == LeadStatus.Admitted);
        var campaigns = await CampaignRowsAsync(db.Campaigns.Where(c => (c.StartsOn <= to && (c.EndsOn == null || c.EndsOn >= from))
            || db.Expenses.Any(e => e.CampaignId == c.Id && e.SpentOn >= from && e.SpentOn <= to)), from, to, ct);
        // Everything spent on marketing in the period: campaign spend plus any other expense filed under Marketing.
        var spend = await db.Expenses.Where(e => e.Category == Domain.Fees.ExpenseCategory.Marketing && e.SpentOn >= from && e.SpentOn <= to).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

        var open = db.Leads.Where(OpenLead);
        var openCount = await open.CountAsync(ct);
        var dueToday = await open.CountAsync(l => l.NextFollowUpOn == today, ct);
        var overdue = await open.CountAsync(l => l.NextFollowUpOn < today, ct);
        var unassigned = await open.CountAsync(l => l.AssignedToUserId == null, ct);
        var due = await Project(open.Where(l => l.NextFollowUpOn <= today).OrderBy(l => l.NextFollowUpOn).Take(8), today).ToListAsync(ct);

        var pipeline = Enum.GetValues<LeadStatus>().Select(s => new CountDto<LeadStatus>(s, inPeriod.Count(l => l.Status == s))).ToList();
        var sources = inPeriod.GroupBy(l => l.Source).Select(g =>
        {
            var won = g.Count(l => l.Status == LeadStatus.Admitted);
            return new SourceRowDto(g.Key, g.Count(), won, MarketingRules.Percent(won, g.Count()));
        }).OrderByDescending(s => s.Leads).ToList();

        var openByUser = (await open.Where(l => l.AssignedToUserId != null).GroupBy(l => l.AssignedToUserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);
        var targets = (await db.MarketingTargets.ToListAsync(ct)).ToDictionary(t => t.UserId);
        var team = (await TeamLookupAsync(ct)).Select(t =>
            {
                var target = targets.GetValueOrDefault(t.Id);
                return new TeamRowDto(t.Id, t.Name,
                    calls.Count(c => c.ByUserId == t.Id), calls.Count(c => c.ByUserId == t.Id && MarketingRules.Reached(c.Outcome)),
                    now.Count(c => c.ByUserId == t.Id && c.Kind != LeadActivityKind.Call), openByUser.GetValueOrDefault(t.Id),
                    inPeriod.Count(l => l.AssignedToUserId == t.Id && l.Status == LeadStatus.Admitted),
                    inPeriod.Count(l => l.CreatedByUserId == t.Id),
                    MarketingRules.DailyTarget(target?.CallsPerDay, from, to < today ? to : today < from ? from.AddDays(-1) : today),
                    MarketingRules.MonthlyTarget(target?.LeadsPerMonth, from, to), MarketingRules.MonthlyTarget(target?.AdmissionsPerMonth, from, to));
            })
            .Where(t => t.Calls + t.OtherContacts + t.LeadsAssignedOpen + t.Admitted + t.LeadsAdded > 0 || t.CallsTarget is not null || t.UserId == user.UserId)
            .OrderByDescending(t => t.Calls).ThenBy(t => t.Name).ToList();

        // A day-by-day line only for periods short enough to read (up to 62 days).
        var days = to.DayNumber - from.DayNumber < 62
            ? Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i))
                .Select(d => new DayCountDto(d, inPeriod.Count(l => l.EnquiredOn == d), calls.Count(c => c.Day == d))).ToList()
            : [];

        return new MarketingOverviewDto(from, to, currency,
            inPeriod.Count, admitted, inPeriod.Count(l => l.Status == LeadStatus.NotInterested), MarketingRules.Percent(admitted, inPeriod.Count),
            calls.Count, reached, MarketingRules.Percent(reached, calls.Count), now.Count - calls.Count,
            spend, MarketingRules.CostPer(spend, inPeriod.Count), MarketingRules.CostPer(spend, admitted),
            openCount, dueToday, overdue, unassigned,
            previous.Count, contacts.Count(a => a.Day < from && a.Kind == LeadActivityKind.Call), previous.Count(l => l.Status == LeadStatus.Admitted),
            pipeline, sources, campaigns, team, days, due,
            await Project(open.Where(l => l.TrialAtUtc >= clock.UtcNow.AddHours(-1) && l.TrialAtUtc < clock.UtcNow.AddDays(14)).OrderBy(l => l.TrialAtUtc).Take(8), today).ToListAsync(ct),
            inPeriod.Where(l => !string.IsNullOrWhiteSpace(l.ReferredBy)).GroupBy(l => l.ReferredBy!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new CountDto<string>(g.Key, g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Key).Take(8).ToList(),
            await open.CountAsync(l => l.FromWebsiteForm && l.Status == LeadStatus.New, ct));
    }

    /// <summary>Leads matching the filters as a CSV file (the same filters as the list; up to 5,000 rows).</summary>
    public async Task<string> LeadsCsvAsync(LeadQuery q, CancellationToken ct)
    {
        Require();
        var today = await centre.TodayAsync(ct);
        var rows = await Project(Filter(db.Leads.AsQueryable(), q, today).OrderByDescending(l => l.EnquiredOn).Take(5000), today).ToListAsync(ct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CsvText.Row("Enquired", "Student", "Parent / guardian", "Phone", "Class", "Syllabus", "School", "Asked about", "Source", "Campaign", "Status", "Assigned to", "Next follow-up", "Calls", "Lost reason"));
        foreach (var l in rows)
            sb.AppendLine(CsvText.Row(l.EnquiredOn.ToString("yyyy-MM-dd"), l.StudentName, l.GuardianName, l.Phone, l.GradeLevelName, l.CurriculumName, l.SchoolName, l.Interest,
                MarketingLabels.Source(l.Source), l.CampaignName, MarketingLabels.Status(l.Status), l.AssignedToName, l.NextFollowUpOn?.ToString("yyyy-MM-dd"), l.Calls.ToString(), l.LostReason));
        return sb.ToString();
    }

    // ---------------- Marketing team accounts (admin only) ----------------

    public async Task<List<MarketingUserDto>> TeamAsync(CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        return await db.Users.Where(u => u.TenantId == user.TenantId && u.Role == UserRole.Marketing).OrderBy(u => u.FullName)
            .Select(u => new MarketingUserDto(u.Id, u.FullName, u.Email!, u.IsActive, u.LastLoginAtUtc,
                db.Leads.Count(l => l.AssignedToUserId == u.Id && l.Status != LeadStatus.Admitted && l.Status != LeadStatus.NotInterested),
                db.MarketingTargets.Where(t => t.UserId == u.Id).Select(t => t.CallsPerDay).FirstOrDefault(),
                db.MarketingTargets.Where(t => t.UserId == u.Id).Select(t => t.LeadsPerMonth).FirstOrDefault(),
                db.MarketingTargets.Where(t => t.UserId == u.Id).Select(t => t.AdmissionsPerMonth).FirstOrDefault())).ToListAsync(ct);
    }

    private async Task<AppUser> TeamMemberAsync(Guid id, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == user.TenantId && u.Role == UserRole.Marketing, ct) ?? throw new NotFoundException("Team member");

    public async Task<CreatedPersonResponse> CreateTeamMemberAsync(CreateMarketingUserRequest r, IUserAccountService accounts, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (_, tz) = await centre.GetAsync(ct);
        var account = await accounts.CreateAsync(user.TenantId, r.Email, Text.Required(r.FullName, "fullName", 150), UserRole.Marketing, tz, null, false, ct);
        audit.Record("marketing.team_added", "User", account.UserId, r.FullName);
        await db.SaveChangesAsync(ct);
        return new CreatedPersonResponse(account.UserId, account.TemporaryPassword!);
    }

    public async Task UpdateTeamMemberAsync(Guid id, UpdateMarketingUserRequest r, IUserAccountService accounts, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var member = await TeamMemberAsync(id, ct);
        member.FullName = Text.Required(r.FullName, "fullName", 150);
        if (r.CallsPerDay is < 0 or > 500) throw RequestValidationException.For("callsPerDay", "Enter a daily call target between 0 and 500.");
        if (r.LeadsPerMonth is < 0 or > 5000) throw RequestValidationException.For("leadsPerMonth", "Enter a monthly lead target between 0 and 5,000.");
        if (r.AdmissionsPerMonth is < 0 or > 5000) throw RequestValidationException.For("admissionsPerMonth", "Enter a monthly admission target between 0 and 5,000.");
        var target = await db.MarketingTargets.FirstOrDefaultAsync(t => t.UserId == id, ct);
        if (target is null) db.MarketingTargets.Add(target = new MarketingTarget { UserId = id });
        if ((target.CallsPerDay, target.LeadsPerMonth, target.AdmissionsPerMonth) != (r.CallsPerDay, r.LeadsPerMonth, r.AdmissionsPerMonth))
        {
            (target.CallsPerDay, target.LeadsPerMonth, target.AdmissionsPerMonth) = (r.CallsPerDay, r.LeadsPerMonth, r.AdmissionsPerMonth);
            audit.Record("marketing.team_targets", "User", id, $"calls/day {r.CallsPerDay?.ToString() ?? "—"}, leads/month {r.LeadsPerMonth?.ToString() ?? "—"}, admissions/month {r.AdmissionsPerMonth?.ToString() ?? "—"}");
        }
        if (member.IsActive != r.IsActive)
        {
            await accounts.SetActiveAsync(id, r.IsActive, ct);
            audit.Record("marketing.team_status", "User", id, r.IsActive ? "Active" : "Inactive");
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<TemporaryPasswordResponse> ResetTeamPasswordAsync(Guid id, IUserAccountService accounts, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        await TeamMemberAsync(id, ct);
        var password = await accounts.ResetPasswordAsync(id, ct);
        audit.Record("marketing.team_password_reset", "User", id, null);
        await db.SaveChangesAsync(ct);
        return new TemporaryPasswordResponse(password);
    }

    /// <summary>
    /// Removes a team member. Someone who logged calls is kept as a deactivated account (their name stays on the log);
    /// someone with no activity is deleted for good. Their open leads become unassigned either way.
    /// </summary>
    public async Task<RemovePersonResponse> DeleteTeamMemberAsync(Guid id, IUserAccountService accounts, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var member = await TeamMemberAsync(id, ct);
        var name = member.FullName;
        var hasHistory = await db.LeadActivities.AnyAsync(a => a.ByUserId == id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Leads.Where(l => l.AssignedToUserId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.AssignedToUserId, (Guid?)null), ct);
        if (hasHistory)
        {
            if (member.IsActive) await accounts.SetActiveAsync(id, false, ct);
            audit.Record("marketing.team_deactivated", "User", id, "Deleted by admin; kept because calls are logged");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new RemovePersonResponse(false, $"{name} logged calls, so the account was switched off instead: they can no longer sign in and their name stays on the call log. Their open leads are now unassigned.");
        }
        await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
        audit.Record("marketing.team_deleted", "User", id, name);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new RemovePersonResponse(true, $"{name} was deleted.");
    }

    // ---------------- Website enquiry form (admin switch) and spreadsheet import ----------------

    public async Task<EnquiryFormDto> EnquiryFormAsync(CancellationToken ct)
    {
        Require();
        var t = await db.Tenants.Where(x => x.Id == user.TenantId).Select(x => new { x.PublicEnquiryEnabled, x.Slug }).FirstAsync(ct);
        return new EnquiryFormDto(t.PublicEnquiryEnabled, t.Slug);
    }

    public async Task SetEnquiryFormAsync(SetEnquiryFormRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var t = await db.Tenants.FirstAsync(x => x.Id == user.TenantId, ct);
        t.PublicEnquiryEnabled = r.Enabled;
        audit.Record("marketing.enquiry_form", "Tenant", t.Id, r.Enabled ? "Switched on" : "Switched off");
        await db.SaveChangesAsync(ct);
    }

    public const int MaxImportRows = 500;

    /// <summary>
    /// Adds leads pasted from a spreadsheet (name, phone, parent, class, asked about). Lines without a usable name and
    /// phone, and people who already have an open lead, are skipped and reported; nothing else is changed.
    /// </summary>
    public async Task<ImportLeadsResult> ImportAsync(ImportLeadsRequest r, CancellationToken ct)
    {
        Require();
        if (!Enum.IsDefined(r.Source)) throw RequestValidationException.For("source", "Choose where these enquiries came from.");
        if (r.CampaignId is { } camp && !await db.Campaigns.AnyAsync(x => x.Id == camp, ct)) throw RequestValidationException.For("campaignId", "Campaign not found.");
        var lines = (r.Rows ?? "").Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) throw RequestValidationException.For("rows", "Paste at least one line: name, phone.");
        if (lines.Count > MaxImportRows + 1) throw RequestValidationException.For("rows", $"Paste at most {MaxImportRows} lines at a time.");
        var today = await centre.TodayAsync(ct);
        var grades = await db.GradeLevels.Select(g => new { g.Id, g.Name }).ToListAsync(ct);
        var known = (await db.Leads.Where(OpenLead).Select(l => new { l.Phone, l.StudentName }).ToListAsync(ct))
            .Select(l => MarketingRules.PhoneKey(l.Phone) + "|" + l.StudentName.Trim().ToLowerInvariant()).ToHashSet();
        var problems = new List<string>();
        var added = 0;
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            var cells = line.Split(line.Contains('\t') ? '\t' : ',').Select(c => c.Trim().Trim('"').Trim()).ToArray();
            if (index == 0 && cells[0].Equals("name", StringComparison.OrdinalIgnoreCase)) continue; // header line
            string? Cell(int i, int max) => cells.Length > i && cells[i].Length > 0 ? (cells[i].Length > max ? cells[i][..max] : cells[i]) : null;
            var (name, phone) = (Cell(0, 150), Cell(1, 30));
            if (name is null || phone is null || MarketingRules.PhoneKey(phone).Length < 6) { problems.Add($"Line {index + 1}: needs a name and a phone number."); continue; }
            if (!known.Add(MarketingRules.PhoneKey(phone) + "|" + name.ToLowerInvariant())) { problems.Add($"Line {index + 1}: {name} already has an open lead."); continue; }
            var grade = Cell(3, 100) is { } g ? grades.FirstOrDefault(x => x.Name.Equals(g, StringComparison.OrdinalIgnoreCase))?.Id : null;
            db.Leads.Add(new Lead { StudentName = name, Phone = phone, GuardianName = Cell(2, 150), GradeLevelId = grade, Interest = Cell(4, 300), Source = r.Source, CampaignId = r.CampaignId, EnquiredOn = today });
            added++;
        }
        audit.Record("marketing.leads_imported", "Lead", Guid.Empty, $"{added} added, {problems.Count} skipped · {r.Source}");
        await db.SaveChangesAsync(ct);
        return new ImportLeadsResult(added, problems.Count, problems.Take(50).ToList());
    }
}
