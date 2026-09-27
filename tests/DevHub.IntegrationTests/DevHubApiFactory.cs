using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.DTOs.Documents;
using DevHub.Application.DTOs.Notifications;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.DTOs.Tasks;
using DevHub.Domain.Entities;
using DevHub.Infrastructure.Persistence;
using DevHub.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.IntegrationTests;

/// <summary>
/// Boots the real API (Program from DevHub.Api) against the local PostgreSQL
/// database "devhub_test", recreated on every fixture start.
/// </summary>
public class DevHubApiFactory : IAsyncLifetime
{
    public const string ConnectionString = "Host=127.0.0.1;Database=devhub_test;Username=devhub;Password=";

    private readonly string? _testPeerAddress;

    private WebApplicationFactory<Program> _inner = null!;

    public DevHubApiFactory()
        : this(null)
    {
    }

    /// <param name="testPeerAddress">
    /// Test-only peer address injected in front of the forwarded headers middleware. Kestrel always
    /// reports a real peer address while the in-memory test server reports none, so tests that need
    /// an untrusted proxy address have to supply one. Never enable this outside tests.
    /// </param>
    protected DevHubApiFactory(string? testPeerAddress)
    {
        _testPeerAddress = testPeerAddress;

        // Respect an externally supplied connection string (CI, docker) and only fall back to the
        // local default when the environment does not define one.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", "integration-test-secret-key-0123456789abcdef");
        Environment.SetEnvironmentVariable("Database__AutoMigrate", "true");
        Environment.SetEnvironmentVariable("Seed__CreateAdminUser", "true");

        // Reset the shared knobs every factory must start from, because environment variables
        // outlive a single fixture and the integration suite runs sequentially in one process.
        Environment.SetEnvironmentVariable("ForwardedHeaders__Enabled", "false");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__AccountPermitLimit", "1000");
    }

    public async Task InitializeAsync()
    {
        _inner = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            if (_testPeerAddress is not null)
            {
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IStartupFilter>(new TestPeerAddressStartupFilter(_testPeerAddress)));
            }
        });

        using var scope = _inner.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await seeder.SeedAsync();

        HttpClient = _inner.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (HttpClient is not null)
        {
            HttpClient.Dispose();
            using var scope = _inner.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureDeletedAsync();
        }

        await _inner.DisposeAsync();
    }

    public HttpClient HttpClient { get; private set; } = null!;

    /// <summary>Service provider of the running API host, for tests that need direct database access.</summary>
    public IServiceProvider Services => _inner.Services;

    private static readonly JsonSerializerOptions JsonOptions = CreateWebJsonOptions();

    internal static JsonSerializerOptions CreateWebJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        return options;
    }

    internal static JsonSerializerOptions ApiJsonOptions => JsonOptions;

    public async Task<AuthResponse> RegisterAsync(string email)
    {
        var response = await HttpClient.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Str0ng!Pass123",
            displayName = email.Split('@')[0]
        });
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return auth;
    }

    public HttpRequestMessage Authenticated(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    public async Task<ProjectResponse> CreateProjectAsync(string accessToken, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/projects")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new CreateProjectRequest(name, "Integration test project", null)),
                Encoding.UTF8,
                "application/json")
        };
        Authenticated(request, accessToken);

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(JsonOptions))!;
    }

    public async Task<TaskResponse> CreateTaskAsync(string accessToken, Guid projectId, string title)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{projectId}/tasks")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new CreateTaskRequest(title, null, null, null, null, null, []), JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        Authenticated(request, accessToken);

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions))!;
    }

    public async Task<DocumentResponse> CreateDocumentAsync(string accessToken, Guid projectId, string title)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{projectId}/documents")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new CreateDocumentRequest(title, null, $"# {title}"), JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        Authenticated(request, accessToken);

        var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions))!;
    }
}
