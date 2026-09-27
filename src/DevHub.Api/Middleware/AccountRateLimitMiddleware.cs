using DevHub.Api.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace DevHub.Api.Middleware;

/// <summary>
/// Complementary per-account budget for the rate limited authentication endpoints.
/// <para>
/// ASP.NET Core applies only one rate limiting policy per endpoint, so the per-address policy
/// registered on the framework cannot also carry a per-account budget. This middleware runs directly
/// after it and enforces the second budget: a request must pass both to reach the endpoint.
/// </para>
/// <para>
/// Requests without an account identifier (refresh, logout) simply skip this stage and remain
/// covered by the address budget; a refresh token is additionally single-use by design.
/// </para>
/// </summary>
public sealed class AccountRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AccountRateLimiter _limiter;
    private readonly RateLimitSettings.AuthPolicySettings _settings;
    private readonly ILogger<AccountRateLimitMiddleware> _logger;

    public AccountRateLimitMiddleware(
        RequestDelegate next,
        AccountRateLimiter limiter,
        RateLimitSettings settings,
        ILogger<AccountRateLimitMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _settings = settings.AuthPolicy;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
        {
            var accountKey = await AuthRateLimitKeyProvider.GetAccountKeyAsync(context);
            if (accountKey is not null)
            {
                var allowed = _limiter.TryAcquire(
                    accountKey,
                    _settings.AccountPermitLimit,
                    _settings.Window,
                    out var retryAfter);

                if (!allowed)
                {
                    // The partition key is a hash, so nothing identifying is written to the log.
                    _logger.LogWarning(
                        "Account rate limit exceeded for one partition in window of {Window}s",
                        (int)_settings.Window.TotalSeconds);

                    await ProblemDetailsWriter.WriteRateLimitedAsync(context, retryAfter, context.RequestAborted);
                    return;
                }
            }
        }

        await _next(context);
    }
}
