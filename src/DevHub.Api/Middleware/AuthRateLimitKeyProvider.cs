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

        string body;
        try
        {
            body = await ReadPrefixAsync(context.Request);
        }
        catch (IOException)
        {
            // The body could not be read at all, most often because the client went away mid-request.
            // There is no account to derive a key from, and the per-address budget still applies, so
            // the request is handed on instead of failing with an error it did not cause.
            return null;
        }

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
        // Only the prefix is inspected, but the request body still has to reach model binding
        // intact, so the wrapper must be able to carry the whole body: a limit equal to the
        // inspection window made EnableBuffering throw "buffer limit exceeded" on any body larger
        // than it, which turned a request over MaxInspectedBytes into an unmapped 500. The limit on
        // how much is read here is the read loop below, not the buffering.
        request.EnableBuffering(bufferThreshold: MaxInspectedBytes);
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

            // Rewound even when the read threw, otherwise model binding would see a body that
            // starts in the middle and bind nothing at all.
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }
}
