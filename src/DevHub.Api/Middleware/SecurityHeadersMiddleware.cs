using Microsoft.Extensions.Hosting;

namespace DevHub.Api.Middleware;

/// <summary>
/// Response headers that narrow what a browser is allowed to do with an API response.
/// <para>
/// The API returns JSON only, so the content security policy denies every resource type for normal
/// responses. Swagger UI is the one exception: it is served as HTML with its own assets and is
/// therefore given a narrowly scoped policy on its own routes only.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string StrictCsp = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private const string SwaggerCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";

    private const int HstsMaxAgeSeconds = 31_536_000;

    private readonly RequestDelegate _next;
    private readonly bool _isProduction;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _isProduction = environment.IsProduction();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = IsSwaggerUiAsset(context.Request.Path)
            ? SwaggerCsp
            : StrictCsp;

        // HSTS only travels over an already secure connection and is scoped to production, so local
        // development over plain HTTP is never pinned by a stray browser.
        if (context.Request.IsHttps && _isProduction)
        {
            headers["Strict-Transport-Security"] = $"max-age={HstsMaxAgeSeconds}; includeSubDomains";
        }

        await _next(context);
    }

    /// <summary>
    /// Swagger UI is the only HTML the API serves, so it is the only place a permissive policy is
    /// ever offered. Everything else under /swagger - above all the machine readable document itself
    /// - keeps the policy that denies every resource type.
    /// </summary>
    private static bool IsSwaggerUiAsset(PathString path)
    {
        if (!path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase, out var remainder))
        {
            return false;
        }

        var value = remainder.Value ?? string.Empty;

        // "/" and "" are the UI entry point, which the middleware answers with a redirect or the page.
        if (value.Length == 0 || value == "/")
        {
            return true;
        }

        return value.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }
}
