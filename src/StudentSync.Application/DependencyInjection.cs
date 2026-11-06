using Microsoft.Extensions.DependencyInjection;
using StudentSync.Application.Academics;
using StudentSync.Application.AI;
using StudentSync.Application.Assessments;
using StudentSync.Application.Assignments;
using StudentSync.Application.Common;
using StudentSync.Application.Communication;
using StudentSync.Application.Content;
using StudentSync.Application.Doubts;
using StudentSync.Application.Jobs;
using StudentSync.Application.People;
using StudentSync.Application.Platform;
using StudentSync.Application.Reporting;
using StudentSync.Application.Scheduling;

namespace StudentSync.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AccessPolicy>();
        services.AddScoped<AuditService>();
        services.AddScoped<PhotoUrls>();
        services.AddScoped<NotificationService>();
        services.AddScoped<FileService>();
        services.AddScoped<ContentService>();
        services.AddScoped<VideoService>();
        services.AddScoped<SchedulingService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<ExamService>();
        services.AddScoped<DoubtService>();
        services.AddScoped<MessagingService>();
        services.AddScoped<NotificationQueryService>();
        services.AddScoped<AiService>();
        services.AddScoped<AiJobRunner>();
        services.AddScoped<PeopleService>();
        services.AddScoped<ProfilePhotoService>();
        services.AddScoped<LearningProfileService>();
        services.AddScoped<AcademicsService>();
        services.AddScoped<StudentCurriculumService>();
        services.AddScoped<MySubjectsService>();
        services.AddScoped<ProgressService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReportService>();
        services.AddScoped<PlatformService>();
        services.AddScoped<InstitutionService>();
        services.AddScoped<NotificationDeliveryJob>();
        services.AddScoped<MaintenanceJobs>();
        return services;
    }
}
