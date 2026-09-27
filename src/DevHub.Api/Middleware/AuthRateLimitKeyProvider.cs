using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevHub.Api.Middleware;

/// <summary>
/// Derives the per-account rate limit key from the submitted credentials.
/// <para>
/// The auth endpoints identify the account through the request body, so the body has to be inspected
/// before endpoint execution. It is buffered, read under a hard size cap and rewound, leaving it
/// untouched for model binding. The raw email is never logged or persisted: it only ever lives in
/// the in-memory partition table of the rate limiter, keyed by a non-reversible hash.
/// </para>
/// </summary>
internal static class AuthRateLimitKeyProvider
{
    /// <summary>
    /// Only this many leading bytes are inspected. Auth payloads are a few hundred bytes, so anything
    /// larger is malformed and simply gets no account budget rather than being read in full.
    /// </summary>
    private const int MaxInspectedBytes = 4096;

    public static async ValueTask<string?> GetAccountKeyAsync(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Post)
        {
            return null;
        }

        var contentType = context.Request.ContentType;
        if (contentType is null || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var body = await ReadPrefixAsync(context.Request);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string? email;
        try
        {
            using var document = JsonDocument.Parse(body);
            email = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("email", out var element)
                && element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return Hash(email);
    }

    /// <summary>
    /// Returns a stable, non-reversible partition key. The email itself is deliberately not used as
    /// the partition key so that it never appears in a rate limiter table dump or a diagnostics dump.
    /// </summary>
    private static string Hash(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }

    private static async ValueTask<string> ReadPrefixAsync(HttpRequest request)
    {
        request.EnableBuffering(bufferThreshold: MaxInspectedBytes, bufferLimit: MaxInspectedBytes);
        var stream = request.Body;
        var originalPosition = stream.CanSeek ? stream.Position : 0;

        var buffer = ArrayPool<byte>.Shared.Rent(MaxInspectedBytes);
        try
        {
            var read = 0;
            while (read < MaxInspectedBytes)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(read, MaxInspectedBytes - read));
                if (bytesRead == 0)
                {
                    break;
                }

                read += bytesRead;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }
}
