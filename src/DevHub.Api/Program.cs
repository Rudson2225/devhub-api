using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DevHub.Api.Hosting;
using DevHub.Api.Middleware;
using DevHub.Api.Models;
using DevHub.Api.OpenApi;
using DevHub.Api.Options;
using DevHub.Application;
using DevHub.Application.Abstractions;
using DevHub.Application.Options;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    // Anything MVC refuses to bind - malformed JSON, a missing body, a value of the wrong type - is
    // a 400 that has to look like every other failure in the API. Left to itself the framework would
    // answer with its own payload: application/json instead of application/problem+json, no
    // errorCode to switch on, and none of the correlation id. Business rules stay on 422.
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var entry in context.ModelState)
            {
                var messages = entry.Value?.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The value is invalid."
                        : error.ErrorMessage)
                    .ToArray() ?? [];

                if (messages.Length > 0)
                {
                    errors[entry.Key] = messages;
                }
            }

            return new ProblemResult(
                StatusCodes.Status400BadRequest,
                "The request is malformed.",
                "bad_request",
                new Dictionary<string, object?> { ["errors"] = errors });
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DevHub API",
        Version = "v1",
        Description = "A backend platform for developers to manage projects, tasks, teams, documentation and activity.",
        Contact = new OpenApiContact { Name = "DevHub" }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT access token obtained from /api/auth/login or /api/auth/register."
    });

    // The requirement lives on the document root so it is declared once; the two filters below trim
    // it off the public endpoints and record the 401/403 answers the protected ones can give.
    options.DocumentFilter<AuthorizationDocumentFilter>();
    options.OperationFilter<AuthorizationOperationFilter>();
});

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException($"'{JwtOptions.SectionName}' configuration section is missing.");

if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey))
{
    throw new InvalidOperationException("JWT SecretKey is not configured. Set it via the 'Jwt__SecretKey' environment variable.");
}

if (Encoding.UTF8.GetByteCount(jwtOptions.SecretKey) < 32)
{
    throw new InvalidOperationException("JWT SecretKey must be at least 32 bytes long.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            // Missing, malformed or invalid bearer tokens are answered by the framework with an empty
            // body by default. Both callbacks emit the same ProblemDetails contract used everywhere
            // else, and the 401 keeps the WWW-Authenticate header the client needs to retry.
            OnChallenge = async context =>
            {
                context.HandleResponse();

                context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                await ProblemDetailsWriter.WriteAsync(
                    context.Response.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Authentication required",
                    "unauthorized");
            },
            OnForbidden = async context =>
            {
                await ProblemDetailsWriter.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "You do not have permission to perform this action.",
                    "forbidden");
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database", tags: ["ready"]);

// --- Forwarded headers ---------------------------------------------------
// Installed first so that every later stage (logging, rate limiting, HSTS, the IP recorded on
// refresh tokens) sees the address the request actually came from instead of the proxy's address.
var forwardedSettings = builder.Configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
    ?? new ForwardedHeadersSettings();

// The middleware is only installed when an operator asked for it. With no trust list configured it
// would either ignore every header (breaking the real IP) or trust every client (breaking rate
// limiting), so both cases are rejected at startup instead of being silently tolerated.
if (forwardedSettings.Enabled && !forwardedSettings.HasTrustedProxies)
{
    throw new InvalidOperationException(
        "ForwardedHeaders:Enabled is true but no KnownProxies or KnownNetworks are configured. " +
        "Refusing to start rather than trusting X-Forwarded-* from any client.");
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = Math.Max(1, forwardedSettings.ForwardLimit),
    RequireHeaderSymmetry = forwardedSettings.RequireHeaderSymmetry
};

if (forwardedSettings.Enabled)
{
    foreach (var proxy in forwardedSettings.KnownProxies)
    {
        if (IPAddress.TryParse(proxy, out var proxyAddress))
        {
            forwardedHeadersOptions.KnownProxies.Add(proxyAddress);
        }
        else
        {
            throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains '{proxy}', which is not a valid IP address.");
        }
    }

    foreach (var network in forwardedSettings.KnownNetworks)
    {
        var parts = network.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var networkAddress)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(CidrError(network));
        }

        var maxLength = networkAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxLength)
        {
            throw new InvalidOperationException(CidrError(network));
        }

        forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(networkAddress, prefixLength));
    }

    static string CidrError(string network) =>
        $"ForwardedHeaders:KnownNetworks contains '{network}', which is not a valid CIDR range (expected e.g. 10.0.0.0/8).";
}

// --- Rate limiting -------------------------------------------------------
var rateLimitSettings = builder.Configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()
    ?? new RateLimitSettings();
var authPolicy = rateLimitSettings.AuthPolicy;

builder.Services.AddSingleton(rateLimitSettings);
builder.Services.AddSingleton<AccountRateLimiter>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        await ProblemDetailsWriter.WriteRateLimitedAsync(
            context.HttpContext,
            authPolicy.Window,
            cancellationToken);
    };

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, authPolicy.PermitLimit),
                Window = authPolicy.Window,
                QueueLimit = Math.Max(0, authPolicy.QueueLimit)
            }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    var anyOrigin = allowedOrigins.Contains("*");
    options.AddPolicy("api", policy =>
    {
        if (anyOrigin)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// Must stay at the very front of the pipeline: everything downstream, including request logging,
// rate limiting and the address written onto refresh tokens, depends on the resolved client IP.
if (forwardedSettings.Enabled)
{
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "DevHub API v1");
        options.DocumentTitle = "DevHub API";
    });
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("api");
app.UseRateLimiter();
// Second budget for the same endpoints: the framework policy covers the address, this covers the
// account, so both have to pass before the request is allowed to reach an auth handler.
app.UseMiddleware<AccountRateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new HealthResponse(Status: report.Status.ToString(), 200, null);
        await context.Response.WriteAsJsonAsync(response);
    }
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var details = report.Entries.ToDictionary(
            e => e.Key,
            e => e.Value.Status.ToString());
        var response = new HealthResponse(
            report.Status.ToString(),
            report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy ? 200 : 503,
            details);
        await context.Response.WriteAsJsonAsync(response);
    }
});

if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await app.MigrateDatabaseAsync();
}

app.Run();

public partial class Program;
