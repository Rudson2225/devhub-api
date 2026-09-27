using DevHub.Application.Abstractions;
using DevHub.Application.Options;
using DevHub.Application.UseCases;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IProjectAccessService, ProjectAccessService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectMemberService, ProjectMemberService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITaskCommentService, TaskCommentService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDashboardService, DashboardService>();

        return services;
    }
}
