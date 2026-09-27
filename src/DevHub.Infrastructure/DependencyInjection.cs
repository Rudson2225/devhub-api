using DevHub.Application;
using DevHub.Application.Abstractions;
using DevHub.Application.Options;
using DevHub.Infrastructure.Authentication;
using DevHub.Infrastructure.Persistence;
using DevHub.Infrastructure.Persistence.Repositories;
using DevHub.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            options.EnableSensitiveDataLogging(false);
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProjectMemberRepository, ProjectMemberRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddScoped<IAccessTokenGenerator, AccessTokenGenerator>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
