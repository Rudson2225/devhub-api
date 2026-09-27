using System.Text.Json.Nodes;

namespace DevHub.IntegrationTests;

/// <summary>
/// Error bodies carry a <c>traceId</c> that is unique per request, so two answers can only be
/// compared for equality once it has been taken out. Everything else in the payload is meant to be
/// identical between two rejections of the same kind.
/// </summary>
internal static class ProblemBody
{
    public static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
        => Normalize(await response.Content.ReadAsStringAsync());

    public static Task<string> WithoutTraceIdAsync(string body)
        => Task.FromResult(Normalize(body));

    private static string Normalize(string body)
    {
        var node = JsonNode.Parse(body)?.AsObject();
        if (node is null)
        {
            return body;
        }

        node.Remove("traceId");
        return node.ToJsonString();
    }
}
