using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentSync.Api.Hosting;
using StudentSync.Application.Academics;
using StudentSync.Application.AI;
using StudentSync.Application.Assessments;
using StudentSync.Application.Assignments;
using StudentSync.Application.Common;
using StudentSync.Application.Communication;
using StudentSync.Application.Content;
using StudentSync.Application.Doubts;
using StudentSync.Application.Reporting;
using StudentSync.Domain.Assessments;
using StudentSync.Application.Scheduling;
using StudentSync.Contracts;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.Api.Endpoints;

public static class LearningEndpoints
{
    private static async Task<(Stream, string, string, long)> Read(IFormFile? file)
    {
        if (file is null || file.Length == 0) throw RequestValidationException.For("file", "Choose a file to upload.");
        return (file.OpenReadStream(), file.FileName, file.ContentType ?? "", file.Length);
    }

    public static void MapSessions(this RouteGroupBuilder api)
    {
        var s = api.MapGroup("/sessions").WithTags("Sessions & scheduling");
        s.MapGet("/", ([AsParameters] SessionQuery q, SchedulingService svc, CancellationToken ct) => svc.ListAsync(q, ct));
        s.MapPost("/check", (CreateSessionRequest r, SchedulingService svc, CancellationToken ct) => svc.CheckAsync(r, ct));
        s.MapPost("/", (CreateSessionRequest r, SchedulingService svc, CancellationToken ct) => svc.CreateAsync(r, ct)).AddEndpointFilter<IdempotencyFilter>();
        s.MapGet("/{id:guid}", (Guid id, SchedulingService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        s.MapPut("/{id:guid}", async (Guid id, UpdateSessionDetailsRequest r, SchedulingService svc, CancellationToken ct) => { await svc.UpdateDetailsAsync(id, r, ct); return Results.NoContent(); });
        s.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleSessionRequest r, SchedulingService svc, CancellationToken ct) => new { warnings = await svc.RescheduleAsync(id, r, ct) });
        s.MapPost("/{id:guid}/cancel", async (Guid id, CancelSessionRequest r, SchedulingService svc, CancellationToken ct) => new { cancelled = await svc.CancelAsync(id, r, ct) });
        s.MapPost("/{id:guid}/status", async (Guid id, SetSessionStatusRequest r, SchedulingService svc, CancellationToken ct) => { await svc.SetStatusAsync(id, r, ct); return Results.NoContent(); });
        s.MapPost("/{id:guid}/participants", async (Guid id, AddParticipantRequest r, SchedulingService svc, CancellationToken ct) => { await svc.AddParticipantAsync(id, r, ct); return Results.NoContent(); });
        s.MapPost("/{id:guid}/participants/{studentId:guid}/remove", async (Guid id, Guid studentId, RemoveParticipantRequest r, SchedulingService svc, CancellationToken ct) => { await svc.RemoveParticipantAsync(id, studentId, r, ct); return Results.NoContent(); });
        s.MapGet("/{id:guid}/history", (Guid id, SchedulingService svc, CancellationToken ct) => svc.HistoryAsync(id, ct));
        s.MapGet("/{id:guid}/agenda", (Guid id, SchedulingService svc, CancellationToken ct) => svc.AgendaAsync(id, ct));
        s.MapPost("/{id:guid}/agenda", async (Guid id, AddAgendaItemRequest r, SchedulingService svc, CancellationToken ct) => new IdResponse(await svc.AddAgendaItemAsync(id, r, ct)));
        s.MapPost("/{id:guid}/agenda/{itemId:guid}/discussed", async (Guid id, Guid itemId, DoubtService svc, CancellationToken ct) => { await svc.MarkAgendaDiscussedAsync(id, itemId, ct); return Results.NoContent(); });
        s.MapGet("/{id:guid}/preparation", (Guid id, SchedulingService svc, CancellationToken ct) => svc.PreparationAsync(id, ct));
        s.MapGet("/{id:guid}/attendance", (Guid id, AttendanceService svc, CancellationToken ct) => svc.RosterAsync(id, ct));
        s.MapPut("/{id:guid}/attendance", async (Guid id, MarkAttendanceRequest r, AttendanceService svc, CancellationToken ct) => { await svc.MarkAsync(id, r, ct); return Results.NoContent(); });

        s.MapGet("/{id:guid}/notes", (Guid id, ContentService svc, CancellationToken ct) => svc.NotesAsync(id, ct));
        s.MapPost("/{id:guid}/notes", async (Guid id, SaveNoteRequest r, ContentService svc, CancellationToken ct) => new IdResponse(await svc.CreateNoteAsync(id, r, ct)));
        s.MapGet("/{id:guid}/recordings", (Guid id, ContentService svc, CancellationToken ct) => svc.RecordingsAsync(id, ct));
        s.MapPost("/{id:guid}/recordings", async (Guid id, IFormFile file, [FromForm] string title, [FromForm] int? durationSeconds, ContentService svc, CancellationToken ct) =>
        {
            var (stream, name, type, length) = await Read(file);
            await using (stream) return new IdResponse(await svc.UploadRecordingAsync(id, stream, name, type, length, title, durationSeconds, ct));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(2_000_000_000));
        s.MapGet("/{id:guid}/videos", (Guid id, VideoService svc, CancellationToken ct) => svc.ForSessionAsync(id, ct));
        s.MapPost("/{id:guid}/videos", async (Guid id, IFormFile file, [FromForm] string title, [FromForm] string? description, [FromForm] VideoUnlockRule unlockRule,
            [FromForm] int unlockOffsetMinutes, [FromForm] int? durationSeconds, VideoService svc, CancellationToken ct) =>
        {
            var (stream, name, type, length) = await Read(file);
            await using (stream) return new IdResponse(await svc.UploadAsync(id, stream, name, type, length, title, description, unlockRule, unlockOffsetMinutes, durationSeconds, ct));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(2_000_000_000));
        s.MapGet("/{id:guid}/consent", (Guid id, ContentService svc, CancellationToken ct) => svc.ConsentStatusAsync(id, ct));
        s.MapPost("/{id:guid}/consent", async (Guid id, GiveConsentRequest r, ContentService svc, CancellationToken ct) => { await svc.GiveConsentAsync(id, r, ct); return Results.NoContent(); });
        s.MapGet("/{id:guid}/materials", (Guid id, ContentService svc, CancellationToken ct) => svc.MaterialsAsync(new LibraryQuery { SessionId = id, PageSize = 100 }, ct));

        // AI Class Assistant (session scoped)
        s.MapGet("/{id:guid}/ai", (Guid id, AiService svc, CancellationToken ct) => svc.StatusAsync(id, ct));
        s.MapPost("/{id:guid}/transcripts", async (Guid id, IFormFile file, [FromForm] string? language, AiService svc, CancellationToken ct) =>
        {
            if (file is null || file.Length is 0 or > 5_000_000) throw RequestValidationException.For("file", "Upload a VTT, SRT or TXT transcript up to 5 MB.");
            using var reader = new StreamReader(file.OpenReadStream());
            return new IdResponse(await svc.UploadTranscriptAsync(id, file.FileName, await reader.ReadToEndAsync(ct), language ?? "en", ct));
        }).DisableAntiforgery();
        s.MapPost("/{id:guid}/ai/drafts", (Guid id, GenerateDraftRequest r, AiService svc, CancellationToken ct) => svc.RequestDraftAsync(id, r, ct));

        api.MapGet("/calendar.ics", async (SchedulingService svc, CancellationToken ct) => Results.Text(await svc.ExportIcsAsync(ct), "text/calendar")).WithTags("Sessions & scheduling");
        api.MapGet("/teachers/{id:guid}/availability", (Guid id, SchedulingService svc, CancellationToken ct) => svc.AvailabilityAsync(id, ct)).WithTags("Sessions & scheduling");
        api.MapPut("/teachers/{id:guid}/availability", async (Guid id, SaveAvailabilityRequest r, SchedulingService svc, CancellationToken ct) => { await svc.SaveAvailabilityAsync(id, r, ct); return Results.NoContent(); }).WithTags("Sessions & scheduling");
    }

    public static void MapContent(this RouteGroupBuilder api)
    {
        var n = api.MapGroup("/notes").WithTags("Notes & materials");
        n.MapPut("/{id:guid}", async (Guid id, SaveNoteRequest r, ContentService s, CancellationToken ct) => { await s.UpdateNoteAsync(id, r, ct); return Results.NoContent(); });
        n.MapPost("/{id:guid}/publish", async (Guid id, ContentService s, CancellationToken ct) => { await s.PublishNoteAsync(id, true, ct); return Results.NoContent(); });
        n.MapPost("/{id:guid}/unpublish", async (Guid id, ContentService s, CancellationToken ct) => { await s.PublishNoteAsync(id, false, ct); return Results.NoContent(); });
        n.MapDelete("/{id:guid}", async (Guid id, ContentService s, CancellationToken ct) => { await s.DeleteNoteAsync(id, ct); return Results.NoContent(); });

        var m = api.MapGroup("/materials").WithTags("Notes & materials");
        m.MapGet("/", ([AsParameters] LibraryQuery q, ContentService s, CancellationToken ct) => s.MaterialsAsync(q, ct));
        m.MapPost("/link", async (CreateLinkMaterialRequest r, ContentService s, CancellationToken ct) => new IdResponse(await s.CreateLinkMaterialAsync(r, ct)));
        m.MapPost("/file", async (IFormFile file, [FromForm] string title, [FromForm] string? description, [FromForm] Guid? sessionId, [FromForm] Guid? subjectId,
            [FromForm] Guid? chapterId, [FromForm] Guid? batchId, [FromForm] bool? availableOffline, ContentService s, CancellationToken ct) =>
        {
            var (stream, name, type, length) = await Read(file);
            await using (stream) return new IdResponse(await s.CreateFileMaterialAsync(stream, name, type, length, title, description, sessionId, subjectId, chapterId, batchId, availableOffline ?? false, ct));
        }).DisableAntiforgery();
        m.MapDelete("/{id:guid}", async (Guid id, ContentService s, CancellationToken ct) => { await s.DeleteMaterialAsync(id, ct); return Results.NoContent(); });

        var l = api.MapGroup("/library").WithTags("Notes & materials");
        l.MapGet("/notes", ([AsParameters] LibraryQuery q, ContentService s, CancellationToken ct) => s.LibraryNotesAsync(q, ct));
        l.MapGet("/recordings", ([AsParameters] LibraryQuery q, ContentService s, CancellationToken ct) => s.LibraryRecordingsAsync(q, ct));

        var r = api.MapGroup("/recordings").WithTags("Notes & materials");
        r.MapDelete("/{id:guid}", async (Guid id, ContentService s, CancellationToken ct) => { await s.DeleteRecordingAsync(id, ct); return Results.NoContent(); });
        r.MapPost("/{id:guid}/progress", async (Guid id, RecordingProgressRequest req, ContentService s, CancellationToken ct) => { await s.SaveProgressAsync(id, req, ct); return Results.NoContent(); });

        var v = api.MapGroup("/videos").WithTags("Pre-recorded videos");
        v.MapGet("/", ([AsParameters] VideoQuery q, VideoService s, CancellationToken ct) => s.ListAsync(q, ct));
        v.MapPut("/{id:guid}", async (Guid id, UpdatePrerecordedVideoRequest req, VideoService s, CancellationToken ct) => { await s.UpdateAsync(id, req, ct); return Results.NoContent(); });
        v.MapPost("/{id:guid}/access", async (Guid id, SetVideoAccessRequest req, VideoService s, CancellationToken ct) => { await s.SetAccessAsync(id, req, ct); return Results.NoContent(); });
        v.MapDelete("/{id:guid}", async (Guid id, VideoService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); });

        var f = api.MapGroup("/files").WithTags("Files");
        f.MapPost("/", async (IFormFile file, [FromForm] FilePurpose purpose, FileService s, CancellationToken ct) =>
        {
            if (purpose is FilePurpose.Recording or FilePurpose.PrerecordedVideo or FilePurpose.Material or FilePurpose.AssignmentAttachment or FilePurpose.Avatar)
                throw RequestValidationException.For("purpose", "Use the dedicated upload endpoint for this file type.");
            var (stream, name, type, length) = await Read(file);
            await using (stream) return await s.UploadAsync(stream, name, type, length, purpose, ct);
        }).DisableAntiforgery();
        f.MapGet("/{id:guid}/link", (Guid id, FileService s, CancellationToken ct) => s.CreateLinkAsync(id, ct));
        // Anonymous by design: authorisation is carried by the short-lived, user-bound token issued by /link.
        f.MapGet("/download", async (string token, IFileAccessTokens tokens, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            if (!tokens.TryValidate(token, out var fileId, out _, out var tenantId)) return Results.Problem("This link has expired.", statusCode: 403);
            var file = await db.Files.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == fileId && x.TenantId == tenantId && x.DeletedAtUtc == null, ct);
            if (file is null) return Results.NotFound();
            var stream = await storage.OpenReadAsync(file.StorageKey, ct);
            var inline = file.ContentType.StartsWith("image/") || file.ContentType.StartsWith("video/") || file.ContentType.StartsWith("audio/") || file.ContentType == "application/pdf";
            return Results.File(stream, file.ContentType, inline ? null : file.FileName, enableRangeProcessing: true);
        }).AllowAnonymous();
    }

    /// <summary>Student-facing learning path: subjects → syllabus chapters → classes, videos and recordings. Always limited to visible classes.</summary>
    public static void MapMySubjects(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/my/subjects").WithTags("Learning");
        g.MapGet("/", (MySubjectsService s, CancellationToken ct) => s.ListAsync(ct));
        g.MapGet("/{subjectId:guid}", (Guid subjectId, MySubjectsService s, CancellationToken ct) => s.GetAsync(subjectId, ct));
    }

    public static void MapAssessments(this RouteGroupBuilder api)
    {
        var a = api.MapGroup("/assignments").WithTags("Assignments");
        a.MapGet("/", ([AsParameters] AssignmentQuery q, AssignmentService s, CancellationToken ct) => s.ListAsync(q, ct));
        a.MapPost("/", async (SaveAssignmentRequest r, AssignmentService s, CancellationToken ct) => new IdResponse(await s.CreateAsync(r, ct)));
        a.MapGet("/{id:guid}", (Guid id, AssignmentService s, CancellationToken ct) => s.GetAsync(id, ct));
        a.MapPut("/{id:guid}", async (Guid id, SaveAssignmentRequest r, AssignmentService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); });
        a.MapPost("/{id:guid}/attachments", async (Guid id, IFormFile file, AssignmentService s, CancellationToken ct) =>
        {
            var (stream, name, type, length) = await Read(file);
            await using (stream) await s.AttachAsync(id, stream, name, type, length, ct);
            return Results.NoContent();
        }).DisableAntiforgery();
        a.MapPost("/{id:guid}/publish", async (Guid id, AssignmentService s, CancellationToken ct) => { await s.PublishAsync(id, ct); return Results.NoContent(); });
        a.MapPost("/{id:guid}/close", async (Guid id, AssignmentService s, CancellationToken ct) => { await s.CloseAsync(id, ct); return Results.NoContent(); });
        a.MapGet("/{id:guid}/submissions", (Guid id, AssignmentService s, CancellationToken ct) => s.SubmissionsAsync(id, ct));
        a.MapGet("/{id:guid}/my-submission", async (Guid id, Guid? studentId, AssignmentService s, CancellationToken ct) => await s.MySubmissionAsync(id, studentId, ct) is { } dto ? Results.Ok(dto) : Results.NoContent());
        a.MapPut("/{id:guid}/my-submission", async (Guid id, SaveSubmissionRequest r, AssignmentService s, CancellationToken ct) => new IdResponse(await s.SaveSubmissionAsync(id, r, ct))).AddEndpointFilter<IdempotencyFilter>();
        api.MapPost("/submissions/{id:guid}/grade", async (Guid id, GradeSubmissionRequest r, AssignmentService s, CancellationToken ct) => { await s.GradeAsync(id, r, ct); return Results.NoContent(); }).WithTags("Assignments");

        var q = api.MapGroup("/questions").WithTags("Exams & question bank");
        q.MapGet("/", ([AsParameters] QuestionQuery query, ExamService s, CancellationToken ct) => s.QuestionsAsync(query, ct));
        q.MapPost("/", async (SaveQuestionRequest r, ExamService s, CancellationToken ct) => new IdResponse(await s.SaveQuestionAsync(null, r, ct)));
        q.MapPut("/{id:guid}", async (Guid id, SaveQuestionRequest r, ExamService s, CancellationToken ct) => new IdResponse(await s.SaveQuestionAsync(id, r, ct)));
        q.MapDelete("/{id:guid}", async (Guid id, ExamService s, CancellationToken ct) => { await s.ArchiveQuestionAsync(id, ct); return Results.NoContent(); });

        var e = api.MapGroup("/exams").WithTags("Exams & question bank");
        e.MapGet("/", (Guid? studentId, ExamPurpose? purpose, ExamService s, CancellationToken ct) => s.ListAsync(studentId, purpose, ct));
        e.MapPost("/", async (SaveExamRequest r, ExamService s, CancellationToken ct) => new IdResponse(await s.SaveAsync(null, r, ct)));
        e.MapGet("/{id:guid}", (Guid id, ExamService s, CancellationToken ct) => s.GetAsync(id, ct));
        e.MapPut("/{id:guid}", async (Guid id, SaveExamRequest r, ExamService s, CancellationToken ct) => new IdResponse(await s.SaveAsync(id, r, ct)));
        e.MapPost("/{id:guid}/publish", async (Guid id, ExamService s, CancellationToken ct) => { await s.PublishAsync(id, ct); return Results.NoContent(); });
        e.MapPost("/{id:guid}/release-results", async (Guid id, ExamService s, CancellationToken ct) => { await s.ReleaseResultsAsync(id, ct); return Results.NoContent(); });
        e.MapGet("/{id:guid}/results", (Guid id, ExamService s, CancellationToken ct) => s.ResultsAsync(id, ct));
        e.MapPost("/{id:guid}/attempts", (Guid id, ExamService s, CancellationToken ct) => s.StartAsync(id, ct));
        // Paper / OMR-style answers entered by staff against the key (diagnostics); retried requests replay with Idempotency-Key.
        e.MapPost("/{id:guid}/paper-attempts", (Guid id, PaperAttemptRequest r, ExamService s, CancellationToken ct) => s.EnterPaperAttemptAsync(id, r, ct)).AddEndpointFilter<IdempotencyFilter>();

        var at = api.MapGroup("/attempts").WithTags("Exams & question bank");
        at.MapGet("/{id:guid}", (Guid id, ExamService s, CancellationToken ct) => s.GetAttemptAsync(id, ct));
        at.MapPut("/{id:guid}/answers", async (Guid id, SaveAnswerRequest r, ExamService s, CancellationToken ct) => { await s.SaveAnswerAsync(id, r, ct); return Results.NoContent(); });
        at.MapPost("/{id:guid}/submit", (Guid id, ExamService s, CancellationToken ct) => s.SubmitAsync(id, ct));
        at.MapGet("/{id:guid}/result", (Guid id, ExamService s, CancellationToken ct) => s.ResultAsync(id, ct));
        at.MapGet("/{id:guid}/breakdown", (Guid id, ExamService s, CancellationToken ct) => s.BreakdownAsync(id, ct));
        at.MapPost("/{id:guid}/learning-needs", (Guid id, ApplyDiagnosticRequest r, ExamService s, CancellationToken ct) => s.ApplyDiagnosticNeedsAsync(id, r, ct));
        at.MapPost("/{id:guid}/grade", async (Guid id, GradeAnswerRequest r, ExamService s, CancellationToken ct) => { await s.GradeAnswerAsync(id, r, ct); return Results.NoContent(); });
    }

    public static void MapDoubts(this RouteGroupBuilder api)
    {
        var d = api.MapGroup("/doubts").WithTags("Doubt Centre");
        d.MapGet("/", ([AsParameters] DoubtQuery q, DoubtService s, CancellationToken ct) => s.ListAsync(q, ct));
        d.MapPost("/", async (CreateDoubtRequest r, DoubtService s, CancellationToken ct) => new IdResponse(await s.CreateAsync(r, ct))).AddEndpointFilter<IdempotencyFilter>();
        d.MapGet("/{id:guid}", (Guid id, DoubtService s, CancellationToken ct) => s.GetAsync(id, ct));
        d.MapPost("/{id:guid}/replies", async (Guid id, ReplyDoubtRequest r, DoubtService s, CancellationToken ct) => { await s.ReplyAsync(id, r, ct); return Results.NoContent(); });
        d.MapPut("/{id:guid}", async (Guid id, UpdateDoubtRequest r, DoubtService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); });
        d.MapPost("/{id:guid}/confirm", async (Guid id, ConfirmDoubtRequest r, DoubtService s, CancellationToken ct) => { await s.ConfirmAsync(id, r, ct); return Results.NoContent(); });
    }

    public static void MapCommunication(this RouteGroupBuilder api)
    {
        var m = api.MapGroup("/").WithTags("Messaging & notifications");
        m.MapGet("/contacts", (MessagingService s, CancellationToken ct) => s.ContactsAsync(ct));
        m.MapGet("/conversations", (MessagingService s, CancellationToken ct) => s.ConversationsAsync(ct));
        m.MapPost("/conversations", async (StartConversationRequest r, MessagingService s, CancellationToken ct) => new IdResponse(await s.StartAsync(r, ct)));
        m.MapGet("/conversations/{id:guid}/messages", (Guid id, DateTime? before, MessagingService s, CancellationToken ct) => s.MessagesAsync(id, before, ct));
        m.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest r, MessagingService s, CancellationToken ct) => new IdResponse(await s.SendAsync(id, r, ct))).AddEndpointFilter<IdempotencyFilter>();
        m.MapGet("/announcements", (MessagingService s, CancellationToken ct) => s.AnnouncementsAsync(ct));
        m.MapPost("/announcements", async (CreateAnnouncementRequest r, MessagingService s, CancellationToken ct) => new IdResponse(await s.PublishAnnouncementAsync(r, ct)));

        m.MapGet("/notifications", ([AsParameters] PageQuery q, bool? unreadOnly, NotificationQueryService s, CancellationToken ct) => s.ListAsync(q, unreadOnly ?? false, ct));
        m.MapGet("/notifications/unread", (NotificationQueryService s, CancellationToken ct) => s.UnreadAsync(ct));
        m.MapPost("/notifications/read", async (NotificationQueryService s, CancellationToken ct) => { await s.MarkReadAsync(null, ct); return Results.NoContent(); });
        m.MapPost("/notifications/{id:guid}/read", async (Guid id, NotificationQueryService s, CancellationToken ct) => { await s.MarkReadAsync(id, ct); return Results.NoContent(); });
        m.MapGet("/notifications/preferences", (NotificationQueryService s, CancellationToken ct) => s.PreferencesAsync(ct));
        m.MapPut("/notifications/preferences", async (UpdateNotificationPreferencesRequest r, NotificationQueryService s, CancellationToken ct) => { await s.SavePreferencesAsync(r, ct); return Results.NoContent(); });
        m.MapPost("/devices", async (RegisterDeviceRequest r, NotificationQueryService s, CancellationToken ct) => { await s.RegisterDeviceAsync(r, ct); return Results.NoContent(); });
        m.MapDelete("/devices/{token}", async (string token, NotificationQueryService s, CancellationToken ct) => { await s.UnregisterDeviceAsync(token, ct); return Results.NoContent(); });
    }

    public static void MapAi(this RouteGroupBuilder api)
    {
        var a = api.MapGroup("/ai").WithTags("AI Class Assistant");
        a.MapGet("/drafts/{id:guid}", (Guid id, AiService s, CancellationToken ct) => s.DraftAsync(id, ct));
        a.MapPut("/drafts/{id:guid}", async (Guid id, EditAiDraftRequest r, AiService s, CancellationToken ct) => { await s.EditDraftAsync(id, r, ct); return Results.NoContent(); });
        a.MapPost("/drafts/{id:guid}/approve", async (Guid id, AiService s, CancellationToken ct) => { await s.ApproveDraftAsync(id, ct); return Results.NoContent(); });
        a.MapPost("/drafts/{id:guid}/reject", async (Guid id, RejectAiDraftRequest r, AiService s, CancellationToken ct) => { await s.RejectDraftAsync(id, r, ct); return Results.NoContent(); });
        a.MapPost("/drafts/{id:guid}/publish", async (Guid id, AiService s, CancellationToken ct) => new IdResponse(await s.PublishDraftAsync(id, ct)));
        a.MapPost("/assistant", (AssistantAskRequest r, AiService s, CancellationToken ct) => s.AskAsync(r, ct)).RequireRateLimiting("ai");
        a.MapGet("/usage", (AiService s, CancellationToken ct) => s.UsageAsync(ct));
        api.MapGet("/transcripts/{id:guid}", (Guid id, AiService s, CancellationToken ct) => s.TranscriptAsync(id, ct)).WithTags("AI Class Assistant");
        api.MapPost("/transcripts/{id:guid}/corrections", async (Guid id, CorrectTranscriptRequest r, AiService s, CancellationToken ct) => new IdResponse(await s.CorrectTranscriptAsync(id, r, ct))).WithTags("AI Class Assistant");
    }

    public static void MapReporting(this RouteGroupBuilder api)
    {
        var d = api.MapGroup("/dashboard").WithTags("Dashboards & reports");
        d.MapGet("/admin", (DashboardService s, CancellationToken ct) => s.AdminAsync(ct));
        d.MapGet("/teacher", (DashboardService s, CancellationToken ct) => s.TeacherAsync(ct));
        d.MapGet("/student", (DashboardService s, CancellationToken ct) => s.StudentAsync(ct));
        d.MapGet("/parent", (Guid? childId, DashboardService s, CancellationToken ct) => s.ParentAsync(childId, ct));

        var r = api.MapGroup("/reports").WithTags("Dashboards & reports").RequirePermission(Permissions.ReportsView);
        r.MapGet("/institution", ([AsParameters] ReportQuery q, ReportService s, CancellationToken ct) => s.InstitutionAsync(q, ct));
        r.MapGet("/attendance.csv", async ([AsParameters] ReportQuery q, ReportService s, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await s.AttendanceCsvAsync(q, ct)), "text/csv", "attendance.csv"));
        api.MapGet("/students/{id:guid}/progress", (Guid id, ProgressService s, CancellationToken ct) => s.ForStudentAsync(id, ct)).WithTags("Dashboards & reports");
    }
}
