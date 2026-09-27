namespace DevHub.Api.Options;

/// <summary>
/// Rate limiting for the authentication endpoints. Two independent budgets are enforced: one per
/// client address and one per submitted account, so rotating IP addresses does not lift the limit
/// for a targeted password guessing or account spraying attack.
/// </summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimit";

    public AuthPolicySettings AuthPolicy { get; set; } = new();

    public sealed class AuthPolicySettings
    {
        /// <summary>Requests allowed per window, per client address.</summary>
        public int PermitLimit { get; set; } = 10;

        /// <summary>Requests allowed per window, per submitted account, across all client addresses.</summary>
        public int AccountPermitLimit { get; set; } = 20;

        public int WindowSeconds { get; set; } = 60;

        public int QueueLimit { get; set; }

        public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds <= 0 ? 60 : WindowSeconds);
    }
}
