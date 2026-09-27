namespace DevHub.Api.Options;

/// <summary>
/// Controls whether the application honours X-Forwarded-For / X-Forwarded-Proto.
/// Trusting these headers without a list of known proxies lets any client spoof its own address,
/// which would defeat IP based rate limiting, so the middleware stays off unless an operator
/// explicitly enables it and declares which hops may set the headers.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Opt-in switch. Defaults to false because the default deployment has no reverse proxy.</summary>
    public bool Enabled { get; set; }

    /// <summary>Addresses of individual reverse proxies allowed to set the headers.</summary>
    public IList<string> KnownProxies { get; set; } = [];

    /// <summary>Networks (CIDR) allowed to set the headers, for proxies behind a load balancer range.</summary>
    public IList<string> KnownNetworks { get; set; } = [];

    /// <summary>
    /// How many proxies are in front of the app. 1 for a single reverse proxy. Each additional hop
    /// lets a client inject one more entry, so this should only be raised when the topology requires it.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Requires every X-Forwarded-* header to list the same number of hops. Left off by default:
    /// with XForwardedProto enabled a proxy that sends X-Forwarded-For but omits X-Forwarded-Proto
    /// would make the whole middleware bail out and silently fall back to the proxy address (the
    /// framework only emits a Debug level log for that). With <see cref="ForwardLimit"/> at 1 both
    /// the address and the scheme are taken from the single hop the trusted proxy asserted, so the
    /// symmetry check adds no protection here. Turn it on only if the topology really forwards
    /// several hops and every proxy is known to write all of the headers on every request.
    /// </summary>
    public bool RequireHeaderSymmetry { get; set; }

    public bool HasTrustedProxies => KnownProxies.Count > 0 || KnownNetworks.Count > 0;
}
