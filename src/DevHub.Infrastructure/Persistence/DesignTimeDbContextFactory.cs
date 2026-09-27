using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DevHub.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DEVHUB_CONNECTIONSTRING")
            ?? $"Host={Environment.GetEnvironmentVariable("DEVHUB_DB_HOST") ?? "localhost"};" +
               $"Database={Environment.GetEnvironmentVariable("DEVHUB_DB_NAME") ?? "devhub"};" +
               $"Username={Environment.GetEnvironmentVariable("DEVHUB_DB_USER") ?? "devhub"};" +
               $"Password={Environment.GetEnvironmentVariable("DEVHUB_DB_PASSWORD") ?? string.Empty}";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
