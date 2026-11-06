using Microsoft.AspNetCore.Mvc;
using StudentSync.Api.Hosting;
using StudentSync.Application.Academics;
using StudentSync.Application.Common;
using StudentSync.Application.People;
using StudentSync.Application.Platform;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Infrastructure.Identity;

namespace StudentSync.Api.Endpoints;

public static class CoreEndpoints
{
    public static void MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication").RequireRateLimiting("auth");
        auth.MapPost("/login", (LoginRequest r, AuthService s, HttpContext http, CancellationToken ct) => s.LoginAsync(r, http.Connection.RemoteIpAddress?.ToString(), ct)).AllowAnonymous();
        auth.MapPost("/refresh", (RefreshRequest r, AuthService s, HttpContext http, CancellationToken ct) => s.RefreshAsync(r, http.Connection.RemoteIpAddress?.ToString(), ct)).AllowAnonymous();
        auth.MapPost("/logout", async (LogoutRequest r, AuthService s, CancellationToken ct) => { await s.LogoutAsync(r, ct); return Results.NoContent(); }).AllowAnonymous();

        var me = api.MapGroup("/me").WithTags("Authentication");
        me.MapGet("/", (AuthService s, ICurrentUser u, CancellationToken ct) => s.MeAsync(u.UserId, ct));
        me.MapPut("/profile", (UpdateProfileRequest r, AuthService s, CancellationToken ct) => s.UpdateProfileAsync(r, ct));
        me.MapPost("/password", async (ChangePasswordRequest r, AuthService s, CancellationToken ct) => { await s.ChangePasswordAsync(r, ct); return Results.NoContent(); });
        me.MapPut("/photo", async (IFormFile? file, ProfilePhotoService photos, AuthService s, ICurrentUser u, CancellationToken ct) =>
        {
            var (stream, name, type, length) = PhotoFile(file);
            await using (stream) await photos.SetOwnAsync(stream, name, type, length, ct);
            return await s.MeAsync(u.UserId, ct);
        }).DisableAntiforgery();
        me.MapDelete("/photo", async (ProfilePhotoService photos, AuthService s, ICurrentUser u, CancellationToken ct) => { await photos.RemoveOwnAsync(ct); return await s.MeAsync(u.UserId, ct); });
        me.MapGet("/children", (PeopleService s, CancellationToken ct) => s.MyChildrenAsync(ct)).RequirePermission(Permissions.ChildrenView);
        // A student's own learning plan and self-reported weak subjects (addendum C).
        var plan = api.MapGroup("/my").WithTags("Learning").RequirePermission(Permissions.LearningNeedsSelf);
        plan.MapGet("/learning-plan", (LearningProfileService s, CancellationToken ct) => s.MyPlanAsync(ct));
        plan.MapPost("/learning-needs", (AddLearningNeedRequest r, LearningProfileService s, ICurrentUser u, CancellationToken ct) => s.AddNeedAsync(u.UserId, r, ct));
        plan.MapDelete("/learning-needs/{needId:guid}", async (Guid needId, LearningProfileService s, CancellationToken ct) => { await s.WithdrawOwnNeedAsync(needId, ct); return Results.NoContent(); });
    }

    /// <summary>Multipart field "file". Size and type are enforced by FileService (JPEG/PNG up to 5 MB).</summary>
    private static (Stream, string, string, long) PhotoFile(IFormFile? file)
    {
        if (file is null || file.Length == 0) throw RequestValidationException.For("file", "Choose a photo to upload.");
        return (file.OpenReadStream(), file.FileName, file.ContentType ?? "", file.Length);
    }

    public static void MapPlatform(this RouteGroupBuilder api)
    {
        var p = api.MapGroup("/platform").WithTags("Platform administration").RequirePermission(Permissions.PlatformTenantsManage);
        p.MapGet("/overview", (PlatformService s, CancellationToken ct) => s.OverviewAsync(ct));
        p.MapGet("/tenants", (PlatformService s, CancellationToken ct) => s.TenantsAsync(ct));
        p.MapPost("/tenants", (CreateTenantRequest r, PlatformService s, CancellationToken ct) => s.CreateTenantAsync(r, ct));
        p.MapPut("/tenants/{id:guid}/status", async (Guid id, UpdateTenantStatusRequest r, PlatformService s, CancellationToken ct) => { await s.UpdateTenantStatusAsync(id, r, ct); return Results.NoContent(); });
        p.MapGet("/plans", (PlatformService s, CancellationToken ct) => s.PlansAsync(ct));
        p.MapPost("/plans", async (SavePlanRequest r, PlatformService s, CancellationToken ct) => new IdResponse(await s.SavePlanAsync(null, r, ct)));
        p.MapPut("/plans/{id:guid}", async (Guid id, SavePlanRequest r, PlatformService s, CancellationToken ct) => new IdResponse(await s.SavePlanAsync(id, r, ct)));
        p.MapGet("/subscriptions", (PlatformService s, CancellationToken ct) => s.SubscriptionsAsync(ct));
        p.MapPut("/tenants/{id:guid}/subscription", async (Guid id, ChangeSubscriptionRequest r, PlatformService s, CancellationToken ct) => { await s.ChangeSubscriptionAsync(id, r, ct); return Results.NoContent(); });
        p.MapGet("/tenants/{id:guid}/subscription/history", (Guid id, PlatformService s, CancellationToken ct) => s.SubscriptionHistoryAsync(id, ct));
        p.MapGet("/audit", ([AsParameters] AuditQuery q, PlatformService s, CancellationToken ct) => s.AuditAsync(q, null, ct)).RequirePermission(Permissions.PlatformAuditView);
        p.MapGet("/flags", (PlatformService s, CancellationToken ct) => s.FlagsAsync(ct));
        p.MapPut("/flags/{id:guid}", async (Guid id, SetFeatureFlagRequest r, PlatformService s, CancellationToken ct) => { await s.SetFlagAsync(id, r, ct); return Results.NoContent(); });
        api.MapGet("/plans", (PlatformService s, CancellationToken ct) => s.PlansAsync(ct)).WithTags("Platform administration");
    }

    public static void MapInstitution(this RouteGroupBuilder api)
    {
        var i = api.MapGroup("/institution").WithTags("Institution");
        i.MapGet("/settings", (InstitutionService s, CancellationToken ct) => s.SettingsAsync(ct)).RequirePermission(Permissions.InstitutionSettingsManage);
        i.MapPut("/settings", async (UpdateInstitutionSettingsRequest r, InstitutionService s, CancellationToken ct) => { await s.UpdateSettingsAsync(r, ct); return Results.NoContent(); }).RequirePermission(Permissions.InstitutionSettingsManage);
        i.MapGet("/holidays", (InstitutionService s, CancellationToken ct) => s.HolidaysAsync(ct));
        i.MapPost("/holidays", async (SaveHolidayRequest r, InstitutionService s, CancellationToken ct) => new IdResponse(await s.AddHolidayAsync(r, ct)));
        i.MapDelete("/holidays/{id:guid}", async (Guid id, InstitutionService s, CancellationToken ct) => { await s.DeleteHolidayAsync(id, ct); return Results.NoContent(); });
        i.MapGet("/subscription", (InstitutionService s, CancellationToken ct) => s.SubscriptionAsync(ct));
        i.MapGet("/permissions", (InstitutionService s) => s.PermissionMatrix()).RequirePermission(Permissions.InstitutionSettingsManage);
        i.MapGet("/audit", ([AsParameters] AuditQuery q, InstitutionService s, CancellationToken ct) => s.AuditAsync(q, ct));
        i.MapGet("/integrations", (PlatformService s) => s.IntegrationStatuses()).RequirePermission(Permissions.InstitutionSettingsManage);
    }

    public static void MapPeople(this RouteGroupBuilder api)
    {
        var t = api.MapGroup("/teachers").WithTags("People");
        t.MapGet("/", ([AsParameters] PersonQuery q, PeopleService s, CancellationToken ct) => s.TeachersAsync(q, ct));
        t.MapPost("/", (CreateTeacherRequest r, PeopleService s, CancellationToken ct) => s.CreateTeacherAsync(r, ct));
        t.MapPut("/{id:guid}", async (Guid id, UpdateTeacherRequest r, PeopleService s, CancellationToken ct) => { await s.UpdateTeacherAsync(id, r, ct); return Results.NoContent(); });
        t.MapPost("/{id:guid}/password-reset", (Guid id, PeopleService s, CancellationToken ct) => s.ResetTeacherPasswordAsync(id, ct));
        t.MapPut("/{id:guid}/photo", async (Guid id, IFormFile? file, ProfilePhotoService s, CancellationToken ct) =>
        {
            var (stream, name, type, length) = PhotoFile(file);
            await using (stream) await s.SetTeacherAsync(id, stream, name, type, length, ct);
            return Results.NoContent();
        }).DisableAntiforgery();
        t.MapDelete("/{id:guid}/photo", async (Guid id, ProfilePhotoService s, CancellationToken ct) => { await s.RemoveTeacherAsync(id, ct); return Results.NoContent(); });

        var st = api.MapGroup("/students").WithTags("People");
        st.MapGet("/", ([AsParameters] PersonQuery q, PeopleService s, CancellationToken ct) => s.StudentsAsync(q, ct));
        st.MapGet("/lookup", (string? search, PeopleService s, CancellationToken ct) => s.StudentLookupAsync(search, ct));
        st.MapGet("/{id:guid}", (Guid id, PeopleService s, CancellationToken ct) => s.StudentProfileAsync(id, ct));
        st.MapPost("/", (CreateStudentRequest r, PeopleService s, CancellationToken ct) => s.CreateStudentAsync(r, ct));
        st.MapPut("/{id:guid}", async (Guid id, UpdateStudentRequest r, PeopleService s, CancellationToken ct) => { await s.UpdateStudentAsync(id, r, ct); return Results.NoContent(); });
        // Admin-only credential issuance (addendum B): a new one-time temporary password that must be changed at the next sign-in.
        st.MapPost("/{id:guid}/password-reset", (Guid id, PeopleService s, CancellationToken ct) => s.ResetStudentPasswordAsync(id, ct));
        st.MapPut("/{id:guid}/photo", async (Guid id, IFormFile? file, ProfilePhotoService s, CancellationToken ct) =>
        {
            var (stream, name, type, length) = PhotoFile(file);
            await using (stream) await s.SetStudentAsync(id, stream, name, type, length, ct);
            return Results.NoContent();
        }).DisableAntiforgery();
        st.MapDelete("/{id:guid}/photo", async (Guid id, ProfilePhotoService s, CancellationToken ct) => { await s.RemoveStudentAsync(id, ct); return Results.NoContent(); });
        // Curriculum: core subjects (from course enrolment), language choices and additional subjects of one student.
        st.MapGet("/{id:guid}/subjects", (Guid id, StudentCurriculumService s, CancellationToken ct) => s.GetForStudentAsync(id, ct));
        st.MapPut("/{id:guid}/subjects", (Guid id, SaveStudentSubjectsRequest r, StudentCurriculumService s, CancellationToken ct) => s.SaveForStudentAsync(id, r, ct));
        // Learning profile (addendum C): weak subjects and staff notes. Scope (admin / teacher-of-student / self) is enforced in the service.
        var lp = st.MapGroup("/{id:guid}").RequirePermission(Permissions.LearningNeedsManage);
        lp.MapGet("/learning-needs", (Guid id, bool? includeResolved, LearningProfileService s, CancellationToken ct) => s.NeedsAsync(id, includeResolved == true, ct));
        lp.MapPost("/learning-needs", (Guid id, AddLearningNeedRequest r, LearningProfileService s, CancellationToken ct) => s.AddNeedAsync(id, r, ct));
        lp.MapPost("/learning-needs/{needId:guid}/resolve", async (Guid id, Guid needId, ResolveLearningNeedRequest r, LearningProfileService s, CancellationToken ct) => { await s.ResolveNeedAsync(id, needId, r, ct); return Results.NoContent(); });
        lp.MapGet("/notes", (Guid id, LearningProfileService s, CancellationToken ct) => s.NotesAsync(id, ct));
        lp.MapPost("/notes", (Guid id, AddStudentNoteRequest r, LearningProfileService s, CancellationToken ct) => s.AddNoteAsync(id, r, ct));
        lp.MapDelete("/notes/{noteId:guid}", async (Guid id, Guid noteId, LearningProfileService s, CancellationToken ct) => { await s.DeleteNoteAsync(id, noteId, ct); return Results.NoContent(); });

        var pa = api.MapGroup("/parents").WithTags("People");
        pa.MapGet("/", ([AsParameters] PersonQuery q, PeopleService s, CancellationToken ct) => s.ParentsAsync(q, ct));
        pa.MapPost("/", (CreateParentRequest r, PeopleService s, CancellationToken ct) => s.CreateParentAsync(r, ct));
        pa.MapPost("/{id:guid}/children", async (Guid id, LinkChildRequest r, PeopleService s, CancellationToken ct) => new IdResponse(await s.LinkChildAsync(id, r, ct)));
        pa.MapDelete("/{id:guid}/children/{linkId:guid}", async (Guid id, Guid linkId, PeopleService s, CancellationToken ct) => { await s.UnlinkChildAsync(id, linkId, ct); return Results.NoContent(); });
    }

    public static void MapAcademics(this RouteGroupBuilder api)
    {
        var a = api.MapGroup("/academics").WithTags("Academics");
        a.MapGet("/years", (AcademicsService s, CancellationToken ct) => s.YearsAsync(ct));
        a.MapPost("/years", async (SaveAcademicYearRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveYearAsync(null, r, ct)));
        a.MapPut("/years/{id:guid}", async (Guid id, SaveAcademicYearRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveYearAsync(id, r, ct)));
        a.MapPost("/years/{id:guid}/terms", async (Guid id, SaveTermRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.AddTermAsync(id, r, ct)));
        a.MapGet("/curricula", (AcademicsService s, CancellationToken ct) => s.CurriculaAsync(ct));
        a.MapPost("/curricula", async (SaveCurriculumRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveCurriculumAsync(null, r, ct)));
        a.MapPut("/curricula/{id:guid}", async (Guid id, SaveCurriculumRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveCurriculumAsync(id, r, ct)));
        a.MapGet("/grades", (AcademicsService s, CancellationToken ct) => s.GradesAsync(ct));
        a.MapPost("/grades", async (SaveGradeLevelRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveGradeAsync(null, r, ct)));
        a.MapPut("/grades/{id:guid}", async (Guid id, SaveGradeLevelRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveGradeAsync(id, r, ct)));

        a.MapGet("/courses", (AcademicsService s, CancellationToken ct) => s.CoursesAsync(ct));
        a.MapPost("/courses", async (SaveCourseRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveCourseAsync(null, r, ct)));
        a.MapPut("/courses/{id:guid}", async (Guid id, SaveCourseRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveCourseAsync(id, r, ct)));
        a.MapGet("/courses/{id:guid}/enrolments", (Guid id, AcademicsService s, CancellationToken ct) => s.CourseEnrolmentsAsync(id, ct));
        a.MapPost("/courses/{id:guid}/enrolments", async (Guid id, EnrolStudentRequest r, AcademicsService s, CancellationToken ct) => { await s.EnrolInCourseAsync(id, r, ct); return Results.NoContent(); });
        a.MapDelete("/courses/{id:guid}/enrolments/{studentId:guid}", async (Guid id, Guid studentId, AcademicsService s, CancellationToken ct) => { await s.WithdrawFromCourseAsync(id, studentId, ct); return Results.NoContent(); });

        a.MapGet("/subjects", (Guid? courseId, AcademicsService s, CancellationToken ct) => s.SubjectsAsync(courseId, ct));
        a.MapPost("/subjects", async (SaveSubjectRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveSubjectAsync(null, r, ct)));
        a.MapPut("/subjects/{id:guid}", async (Guid id, SaveSubjectRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveSubjectAsync(id, r, ct)));
        a.MapPost("/subjects/{id:guid}/chapters", async (Guid id, SaveChapterRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.AddChapterAsync(id, r, ct)));
        a.MapPost("/chapters/{id:guid}/topics", async (Guid id, SaveTopicRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.AddTopicAsync(id, r, ct)));
        a.MapPost("/subjects/{id:guid}/teachers", async (Guid id, AssignTeacherRequest r, AcademicsService s, CancellationToken ct) => { await s.AssignTeacherAsync(id, r, ct); return Results.NoContent(); });
        a.MapDelete("/subjects/{id:guid}/teachers/{teacherId:guid}", async (Guid id, Guid teacherId, AcademicsService s, CancellationToken ct) => { await s.UnassignTeacherAsync(id, teacherId, ct); return Results.NoContent(); });

        a.MapGet("/batches", (AcademicsService s, CancellationToken ct) => s.BatchesAsync(ct));
        a.MapPost("/batches", async (SaveBatchRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveBatchAsync(null, r, ct)));
        a.MapPut("/batches/{id:guid}", async (Guid id, SaveBatchRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveBatchAsync(id, r, ct)));
        a.MapGet("/batches/{id:guid}/students", (Guid id, AcademicsService s, CancellationToken ct) => s.BatchStudentsAsync(id, ct));
        a.MapPost("/batches/{id:guid}/students", async (Guid id, EnrolStudentRequest r, AcademicsService s, CancellationToken ct) => { await s.AddToBatchAsync(id, r, ct); return Results.NoContent(); });
        a.MapDelete("/batches/{id:guid}/students/{studentId:guid}", async (Guid id, Guid studentId, AcademicsService s, CancellationToken ct) => { await s.RemoveFromBatchAsync(id, studentId, ct); return Results.NoContent(); });

        a.MapGet("/arrangements", (AcademicsService s, CancellationToken ct) => s.ArrangementsAsync(ct));
        a.MapPost("/arrangements", async (SaveArrangementRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveArrangementAsync(null, r, ct)));
        a.MapPut("/arrangements/{id:guid}", async (Guid id, SaveArrangementRequest r, AcademicsService s, CancellationToken ct) => new IdResponse(await s.SaveArrangementAsync(id, r, ct)));
    }
}
