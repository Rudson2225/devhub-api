namespace DevHub.Api.Models;

public sealed record HealthResponse(string Status, int Code, IReadOnlyDictionary<string, string>? Details);
