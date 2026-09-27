namespace DevHub.IntegrationTests;

/// <summary>
/// Host behind a trusted reverse proxy. The peer address is loopback, which is explicitly declared
/// as a known proxy, so X-Forwarded-* is honoured and the resolved client address drives the
/// per-address budget.
/// </summary>
public sealed class TrustedProxyApiFactory : DevHubApiFactory
{
    public TrustedProxyApiFactory()
        : base("127.0.0.1")
    {
        Environment.SetEnvironmentVariable("ForwardedHeaders__Enabled", "true");
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownProxies__0", "127.0.0.1");
        // Deliberately tight so the per-address budget can be reached within a test.
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__AccountPermitLimit", "1000");
    }
}

/// <summary>
/// Host that sees a client address which is not a declared proxy. Forwarded headers are enabled,
/// but the header must be ignored because the sender is not trusted.
/// </summary>
public sealed class UntrustedProxyApiFactory : DevHubApiFactory
{
    public UntrustedProxyApiFactory()
        : base("203.0.113.5")
    {
        Environment.SetEnvironmentVariable("ForwardedHeaders__Enabled", "true");
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownProxies__0", "127.0.0.1");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__AccountPermitLimit", "1000");
    }
}

/// <summary>
/// Host with a generous per-address budget and a tight per-account budget, so tests can prove that
/// rotating source addresses does not lift the limit on one account. The peer is a declared proxy so
/// that X-Forwarded-For is honoured and each forged address becomes a distinct rate limit key.
/// </summary>
public sealed class AccountRateLimitApiFactory : DevHubApiFactory
{
    public AccountRateLimitApiFactory()
        : base("127.0.0.1")
    {
        Environment.SetEnvironmentVariable("ForwardedHeaders__Enabled", "true");
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownProxies__0", "127.0.0.1");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__AccountPermitLimit", "3");
    }
}

/// <summary>
/// Host with a tight per-address budget and no proxy configuration, for exercising the address
/// based rejection path in isolation.
/// </summary>
public sealed class IpRateLimitApiFactory : DevHubApiFactory
{
    public IpRateLimitApiFactory()
        : base(null)
    {
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimit__AuthPolicy__AccountPermitLimit", "1000");
    }
}
