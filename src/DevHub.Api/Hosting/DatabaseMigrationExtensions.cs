using DevHub.Application.Abstractions;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DevHub.Api.Hosting;

public static class DatabaseMigrationExtensions
{
    public static async Task MigrateDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.Seed.DatabaseSeeder>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");

        var canConnect = await context.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            logger.LogError(
                "Could not connect to the database using the configured connection string. " +
                "Make sure PostgreSQL is running and the connection settings are correct.");
            return;
        }

        await context.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);

        logger.LogInformation("Database migrations applied and seed data ensured.");
    }
}
