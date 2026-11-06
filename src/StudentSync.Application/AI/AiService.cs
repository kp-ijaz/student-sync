using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Contracts;
using StudentSync.Domain.AI;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.AI;

public sealed class AiService(
    IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify,
    ContentService content, ILanguageModel llm, ITranscriptionProvider transcription, IBackgroundJobs jobs)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<LearningSession> ManageableAsync(Guid sessionId, CancellationToken ct)
    {
        access.Require(Permissions.AiReview);
        return await access.GetManageableSessionAsync(sessionId, ct);
    }

    // ---------------- Status ----------------

    public async Task<AiSessionStatusDto> StatusAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await ManageableAsync(sessionId, ct);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == s.TenantId, ct);
        var consent = await content.AllConsentedAsync(s.Id, ct);
        var hasRecording = await db.Recordings.AnyAsync(r => r.SessionId == s.Id && r.Status == RecordingStatus.Available, ct);
        var transcriptId = await db.Transcripts.Where(t => t.SessionId == s.Id).OrderByDescending(t => t.CreatedAtUtc).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var jobList = await db.AiJobs.Where(j => j.SessionId == s.Id).OrderByDescending(j => j.CreatedAtUtc).Take(20)
            .Select(j => new AiJobDto(j.Id, j.Kind, j.Status, j.Attempts, j.FailureReason, j.Provider, j.Model, j.CreatedAtUtc, j.CompletedAtUtc)).ToListAsync(ct);
        var drafts = await db.AiDrafts.Where(d => d.SessionId == s.Id).OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new AiDraftSummaryDto(d.Id, d.Status, d.CurrentVersion, d.CreatedAtUtc, d.ApprovedAtUtc, d.PublishedAtUtc, d.IsDemo)).ToListAsync(ct);

        var latestJob = jobList.FirstOrDefault();
        var latestDraft = drafts.FirstOrDefault();
        (AiPipelineStatus status, string detail) = (latestJob, latestDraft) switch
        {
            ({ Status: AiJobStatus.Queued or AiJobStatus.Running }, _) => (AiPipelineStatus.AIProcessing, "AI processing is in progress."),
            ({ Status: AiJobStatus.Failed or AiJobStatus.Blocked } j, var d) when d is null || d.CreatedAtUtc < j.CreatedAtUtc
                => (AiPipelineStatus.ProcessingFailed, j.FailureReason ?? "Processing failed."),
            (_, { Status: AiDraftStatus.Published }) => (AiPipelineStatus.Published, "Approved notes are published to participants."),
            (_, { Status: AiDraftStatus.Approved }) => (AiPipelineStatus.Approved, "Approved. Publish to share with participants."),
            (_, { Status: AiDraftStatus.AwaitingTeacherReview }) => (AiPipelineStatus.AwaitingTeacherReview, "A draft is waiting for your review."),
            _ when transcriptId is not null => (AiPipelineStatus.TranscriptReady, "Transcript ready. Generate an AI draft when you are ready."),
            _ when hasRecording => (AiPipelineStatus.TranscriptionPending, transcription.IsConfigured
                ? "Recording available. Request transcription or upload a transcript."
                : "Recording available. No transcription provider is configured — upload a transcript (VTT, SRT or TXT)."),
            _ => (AiPipelineStatus.RecordingPending, "No recording or transcript yet. Upload a transcript or a consented recording."),
        };
        return new AiSessionStatusDto(s.Id, status, detail, llm.IsConfigured, transcription.IsConfigured, tenant.AiAssistantEnabled, consent.Ai,
            transcriptId, jobList, drafts);
    }

    // ---------------- Transcripts ----------------

    public async Task<Guid> UploadTranscriptAsync(Guid sessionId, string fileName, string rawText, string language, CancellationToken ct)
    {
        var s = await ManageableAsync(sessionId, ct);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == s.TenantId, ct);
        if (!tenant.TranscriptionEnabled) throw new DomainException("Transcription is disabled by your institution.", "transcription_disabled");
        if (!(await content.AllConsentedAsync(s.Id, ct)).Transcription)
            throw new DomainException("Every participant (or their guardian) must consent to transcription first.", "consent_required");
        if (rawText.Length > 2_000_000) throw RequestValidationException.For("file", "The transcript is too large.");
        var parsed = TranscriptParser.Parse(rawText);
        if (parsed.Count == 0) throw RequestValidationException.For("file", "No transcript text was found in the file.");
        var transcript = new ClassTranscript
        {
            SessionId = s.Id,
            Source = TranscriptSource.Upload,
            Language = Text.Required(language, "language", 10),
            IsOriginal = true,
            FullText = string.Join('\n', parsed.Select(p => p.Speaker is null ? p.Text : $"{p.Speaker}: {p.Text}")),
        };
        var seq = 0;
        foreach (var p in parsed)
            transcript.Segments.Add(new TranscriptSegment { Sequence = seq++, StartSeconds = p.Start, EndSeconds = p.End, Speaker = p.Speaker, Text = p.Text });
        db.Transcripts.Add(transcript);
        audit.Record("transcript.uploaded", "ClassTranscript", transcript.Id, $"{Path.GetFileName(fileName)}, {parsed.Count} segments");
        await db.SaveChangesAsync(ct);
        return transcript.Id;
    }

    public async Task<TranscriptDto> TranscriptAsync(Guid transcriptId, CancellationToken ct)
    {
        var t = await db.Transcripts.Include(x => x.Segments).FirstOrDefaultAsync(x => x.Id == transcriptId, ct) ?? throw new NotFoundException("Transcript");
        await ManageableAsync(t.SessionId, ct);
        var segments = t.Segments.OrderBy(x => x.Sequence).Select(x => new TranscriptSegmentDto(x.Sequence, x.StartSeconds, x.EndSeconds, x.Speaker, x.Text, x.Confidence)).ToList();
        return new TranscriptDto(t.Id, t.SessionId, t.Source.ToString(), t.Language, t.IsOriginal, t.CorrectsTranscriptId, t.CreatedAtUtc, segments,
            segments.Count(x => x.Confidence is < 0.6));
    }

    /// <summary>Stores a teacher-corrected copy; the original transcript is never modified.</summary>
    public async Task<Guid> CorrectTranscriptAsync(Guid transcriptId, CorrectTranscriptRequest r, CancellationToken ct)
    {
        var original = await db.Transcripts.FirstOrDefaultAsync(x => x.Id == transcriptId, ct) ?? throw new NotFoundException("Transcript");
        await ManageableAsync(original.SessionId, ct);
        if (r.Segments.Count is 0 or > 20_000) throw RequestValidationException.For("segments", "Provide the corrected segments.");
        var corrected = new ClassTranscript
        {
            SessionId = original.SessionId,
            RecordingId = original.RecordingId,
            Source = TranscriptSource.Manual,
            Language = original.Language,
            IsOriginal = false,
            CorrectsTranscriptId = original.CorrectsTranscriptId ?? original.Id,
            FullText = string.Join('\n', r.Segments.Select(x => x.Speaker is null ? x.Text : $"{x.Speaker}: {x.Text}")),
        };
        var seq = 0;
        foreach (var x in r.Segments.OrderBy(x => x.Sequence))
            corrected.Segments.Add(new TranscriptSegment { Sequence = seq++, StartSeconds = x.StartSeconds, EndSeconds = x.EndSeconds, Speaker = Text.Optional(x.Speaker, "speaker", 100), Text = Text.Required(x.Text, "text", 10_000), Confidence = null });
        db.Transcripts.Add(corrected);
        audit.Record("transcript.corrected", "ClassTranscript", corrected.Id, $"corrects {original.Id}");
        await db.SaveChangesAsync(ct);
        return corrected.Id;
    }

    // ---------------- Draft generation ----------------

    public async Task<AiJobDto> RequestDraftAsync(Guid sessionId, GenerateDraftRequest r, CancellationToken ct)
    {
        var s = await ManageableAsync(sessionId, ct);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == s.TenantId, ct);
        if (!tenant.AiAssistantEnabled) throw new DomainException("The AI Class Assistant is disabled by your institution.", "ai_disabled");
        if (!(await content.AllConsentedAsync(s.Id, ct)).Ai)
            throw new DomainException("Every participant (or their guardian) must consent to AI processing first.", "consent_required");
        var transcript = r.TranscriptId is { } tid
            ? await db.Transcripts.FirstOrDefaultAsync(t => t.Id == tid && t.SessionId == s.Id, ct)
            : await db.Transcripts.Where(t => t.SessionId == s.Id).OrderByDescending(t => t.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (transcript is null) throw new DomainException("Upload or generate a transcript first.", "transcript_required");

        var active = await db.AiJobs.FirstOrDefaultAsync(j => j.TranscriptId == transcript.Id && j.Kind == AiJobKind.LessonDraft && (j.Status == AiJobStatus.Queued || j.Status == AiJobStatus.Running), ct);
        if (active is not null) return ToDto(active);
        if (await db.AiDrafts.AnyAsync(d => d.TranscriptId == transcript.Id && (d.Status == AiDraftStatus.AwaitingTeacherReview || d.Status == AiDraftStatus.Approved), ct))
            throw new ConflictException("A draft from this transcript is already awaiting review. Edit, approve or reject it first.", "draft_exists");

        var plan = await db.Subscriptions.Include(x => x.Plan).FirstOrDefaultAsync(x => x.TenantId == s.TenantId, ct);
        var monthStart = new DateTime(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var used = await db.AiJobs.CountAsync(j => j.CreatedAtUtc >= monthStart && j.Status != AiJobStatus.Blocked, ct);
        if (plan?.Plan is { } p && used >= p.MonthlyAiJobs)
            throw new DomainException($"Your plan's monthly AI limit ({p.MonthlyAiJobs} jobs) has been reached.", "ai_quota_exceeded");

        var previous = await db.AiJobs.CountAsync(j => j.TranscriptId == transcript.Id && j.Kind == AiJobKind.LessonDraft, ct);
        var job = new AiProcessingJob
        {
            SessionId = s.Id,
            Kind = AiJobKind.LessonDraft,
            TranscriptId = transcript.Id,
            RequestedByUserId = user.UserId,
            IdempotencyKey = $"lesson-draft:{transcript.Id:N}:{previous + 1}",
            Provider = llm.ProviderName,
            Model = llm.ModelName,
        };
        if (!llm.IsConfigured)
        {
            // Honest failure: no provider means no draft. Manual notes remain fully available.
            job.Status = AiJobStatus.Blocked;
            job.FailureReason = "No AI language-model provider is configured for this deployment. Write notes manually or ask your administrator to configure a provider.";
            job.CompletedAtUtc = clock.UtcNow;
        }
        db.AiJobs.Add(job);
        audit.Record("ai.draft_requested", "AiProcessingJob", job.Id, job.Status.ToString());
        await db.SaveChangesAsync(ct);
        if (job.Status == AiJobStatus.Queued) jobs.Enqueue<AiJobRunner>(x => x.RunLessonDraftAsync(job.TenantId, job.Id, CancellationToken.None));
        return ToDto(job);
    }

    private static AiJobDto ToDto(AiProcessingJob j) => new(j.Id, j.Kind, j.Status, j.Attempts, j.FailureReason, j.Provider, j.Model, j.CreatedAtUtc, j.CompletedAtUtc);

    // ---------------- Review & publication ----------------

    private async Task<AiLessonDraft> DraftForReviewAsync(Guid draftId, CancellationToken ct)
    {
        var d = await db.AiDrafts.Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == draftId, ct) ?? throw new NotFoundException("Draft");
        await ManageableAsync(d.SessionId, ct);
        return d;
    }

    public async Task<AiDraftDto> DraftAsync(Guid draftId, CancellationToken ct)
    {
        var d = await DraftForReviewAsync(draftId, ct);
        var title = await db.Sessions.Where(s => s.Id == d.SessionId).Select(s => s.Title).FirstAsync(ct);
        var editors = await db.Users.Where(u => d.Versions.Select(v => v.EditedByUserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var contentObj = JsonSerializer.Deserialize<LessonDraftContent>(d.ContentJson, Json)!;
        return new AiDraftDto(d.Id, d.SessionId, title, d.Status, d.CurrentVersion, contentObj, d.Provider, d.Model, d.IsDemo, d.RejectionReason, d.CreatedAtUtc,
            d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new AiDraftVersionDto(v.VersionNumber, v.CreatedAtUtc,
                v.EditedByUserId is { } e && editors.TryGetValue(e, out var n) ? n : "AI", v.ChangeNote)).ToList(),
            RowVersions.Encode(d.RowVersion));
    }

    public async Task EditDraftAsync(Guid draftId, EditAiDraftRequest r, CancellationToken ct)
    {
        var d = await DraftForReviewAsync(draftId, ct);
        RowVersions.Apply(db, d, r.RowVersion);
        var errors = r.Content.Validate();
        if (errors.Count > 0) throw new RequestValidationException(new Dictionary<string, string[]> { ["content"] = errors.ToArray() });
        d.Edit(JsonSerializer.Serialize(r.Content, Json), user.UserId, clock.UtcNow, Text.Optional(r.ChangeNote, "changeNote", 500));
        audit.Record("ai.draft_edited", "AiLessonDraft", d.Id, $"v{d.CurrentVersion}");
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveDraftAsync(Guid draftId, CancellationToken ct)
    {
        var d = await DraftForReviewAsync(draftId, ct);
        d.Approve(user.UserId, clock.UtcNow);
        audit.Record("ai.draft_approved", "AiLessonDraft", d.Id, $"v{d.CurrentVersion}");
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectDraftAsync(Guid draftId, RejectAiDraftRequest r, CancellationToken ct)
    {
        var d = await DraftForReviewAsync(draftId, ct);
        d.Reject(user.UserId, Text.Required(r.Reason, "reason", 1000));
        audit.Record("ai.draft_rejected", "AiLessonDraft", d.Id, r.Reason);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Publishes an approved draft as a participant-visible session note. Unapproved drafts are rejected by the domain.</summary>
    public async Task<Guid> PublishDraftAsync(Guid draftId, CancellationToken ct)
    {
        var d = await DraftForReviewAsync(draftId, ct);
        if (d.Status != AiDraftStatus.Approved) throw new DomainException("A teacher must approve this draft before it can be published.", "approval_required");
        var session = await db.Sessions.Include(s => s.Participants).FirstAsync(s => s.Id == d.SessionId, ct);
        var c = JsonSerializer.Deserialize<LessonDraftContent>(d.ContentJson, Json)!;
        var note = new SessionNote
        {
            SessionId = d.SessionId,
            AuthorUserId = user.UserId,
            Title = $"Class recap — {session.Title}",
            BodyMarkdown = LessonDraftMarkdown.Render(c),
            Visibility = NoteVisibility.Participants,
            Status = PublicationStatus.Published,
            PublishedAtUtc = clock.UtcNow,
            Source = ContentSource.AiApproved,
            AiDraftId = d.Id,
        };
        db.Notes.Add(note);
        d.MarkPublished(note.Id, clock.UtcNow);
        audit.Record("ai.draft_published", "AiLessonDraft", d.Id, $"note {note.Id}");
        await notify.NotifyAsync(session.Participants.Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId), NotificationType.NotesPublished,
            $"Class recap available: {session.Title}", "Reviewed by your teacher.", $"/sessions/{session.Id}", $"ai-published:{d.Id}", ct);
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    // ---------------- Student learning assistant ----------------

    public async Task<AssistantAnswerDto> AskAsync(AssistantAskRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AiAssistantUse);
        var question = Text.Required(r.Question, "question", 2000);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == user.TenantId, ct);
        const string disclaimer = "AI explanations are generated from your teacher-approved notes. They are not your teacher's words; ask your teacher if anything is unclear.";
        if (!tenant.AiAssistantEnabled) return new AssistantAnswerDto(false, null, [], "The AI assistant is disabled by your institution.");

        // Retrieval is restricted to published notes in sessions the student participates in.
        var sessions = await access.VisibleSessionsAsync(ct);
        if (r.SessionId is { } sid) sessions = sessions.Where(s => s.Id == sid);
        var terms = Regex.Split(question.ToLowerInvariant(), @"\W+").Where(t => t.Length > 3).Distinct().Take(8).ToList();
        var notes = await db.Notes.Where(n => n.Status == PublicationStatus.Published && n.Visibility == NoteVisibility.Participants && sessions.Any(s => s.Id == n.SessionId))
            .OrderByDescending(n => n.PublishedAtUtc).Take(100)
            .Select(n => new { n.Id, n.SessionId, n.Title, n.BodyMarkdown, SessionTitle = db.Sessions.Where(s => s.Id == n.SessionId).Select(s => s.Title).First() })
            .ToListAsync(ct);
        var ranked = notes.Select(n => new { n, Score = terms.Count(t => n.BodyMarkdown.Contains(t, StringComparison.OrdinalIgnoreCase) || n.Title.Contains(t, StringComparison.OrdinalIgnoreCase)) })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(3).ToList();
        var sources = ranked.Select(x => new AssistantSourceDto(x.n.SessionId, x.n.SessionTitle, x.n.Id, x.n.Title, Excerpt(x.n.BodyMarkdown, terms))).ToList();

        if (sources.Count == 0)
            return new AssistantAnswerDto(llm.IsConfigured, null, [], "No approved class notes match this question yet. You can send it to your teacher as a doubt.");
        if (!llm.IsConfigured)
            return new AssistantAnswerDto(false, null, sources, "No AI provider is configured, so here are the most relevant approved notes instead.");

        var prompt = new StringBuilder();
        prompt.AppendLine("Approved class notes:");
        foreach (var x in ranked) prompt.AppendLine($"<note title=\"{x.n.Title}\">\n{Truncate(x.n.BodyMarkdown, 6000)}\n</note>");
        prompt.AppendLine().AppendLine($"Student question: {question}");
        var result = await llm.CompleteAsync(
            "You are a study helper for a tuition centre. Answer only using the approved class notes provided. " +
            "If the notes do not cover the question, say so plainly and suggest asking the teacher. Never claim something was taught unless it appears in the notes. " +
            "Keep answers concise, encouraging and age-appropriate.", prompt.ToString(), 800, ct);
        db.AiUsage.Add(new AiUsageRecord { Feature = "student_assistant", Provider = llm.ProviderName, Model = llm.ModelName, InputTokens = result.InputTokens, OutputTokens = result.OutputTokens, EstimatedCostUsd = result.EstimatedCostUsd });
        await db.SaveChangesAsync(ct);
        return result.Success
            ? new AssistantAnswerDto(true, result.Text, sources, disclaimer)
            : new AssistantAnswerDto(true, null, sources, "The AI assistant is temporarily unavailable. Here are the most relevant approved notes.");
    }

    public async Task<AiUsageSummaryDto> UsageAsync(CancellationToken ct)
    {
        access.Require(Permissions.AiConfigure);
        var monthStart = new DateTime(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var jobsThisMonth = await db.AiJobs.CountAsync(j => j.CreatedAtUtc >= monthStart && j.Status != AiJobStatus.Blocked, ct);
        var usage = await db.AiUsage.Where(u => u.CreatedAtUtc >= monthStart).GroupBy(_ => 1)
            .Select(g => new { In = g.Sum(x => x.InputTokens), Out = g.Sum(x => x.OutputTokens), Cost = g.Sum(x => x.EstimatedCostUsd) }).FirstOrDefaultAsync(ct);
        var plan = await db.Subscriptions.Include(s => s.Plan).FirstOrDefaultAsync(ct);
        return new AiUsageSummaryDto(jobsThisMonth, plan?.Plan?.MonthlyAiJobs ?? 0, usage?.In ?? 0, usage?.Out ?? 0, usage?.Cost ?? 0,
            llm.IsConfigured, llm.IsConfigured ? llm.ProviderName : null, llm.IsConfigured ? llm.ModelName : null);
    }

    private static string Excerpt(string body, List<string> terms)
    {
        var idx = terms.Select(t => body.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, idx - 80);
        return (start > 0 ? "…" : "") + Truncate(body[start..], 280);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
