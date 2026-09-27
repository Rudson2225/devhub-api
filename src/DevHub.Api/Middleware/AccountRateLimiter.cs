using System.Collections.Concurrent;

namespace DevHub.Api.Middleware;

/// <summary>
/// Fixed-window budget keyed on the account being authenticated.
/// <para>
/// The framework's rate limiter can only partition on one dimension, so it carries the per-address
/// budget while this component carries the per-account one. Together they stop an attacker from
/// reaching one account by rotating source addresses, which is the common way a per-IP limit is
/// bypassed against password guessing.
/// </para>
/// <para>
/// Deliberately self-contained: no third-party limiter types, explicit window bounds and explicit
/// eviction, so memory stays bounded no matter how many distinct identifiers a caller supplies.
/// </para>
/// </summary>
public sealed class AccountRateLimiter
{
    /// <summary>
    /// Upper bound on tracked accounts. Once exceeded, expired windows are swept; if the table is
    /// still full it is dropped entirely, which resets budgets but never widens them permanently.
    /// </summary>
    private const int MaxPartitions = 50_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed record Entry(long WindowStartTicks, int Count);

    public bool TryAcquire(string partitionKey, int permitLimit, TimeSpan window, out TimeSpan retryAfter)
    {
        var limit = Math.Max(1, permitLimit);
        var durationTicks = (window <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : window).Ticks;
        var now = Environment.TickCount64;

        if (_entries.Count > MaxPartitions)
        {
            Sweep(window);
        }

        while (true)
        {
            var currentWindowStart = now - (now % durationTicks);
            var entry = _entries.GetOrAdd(partitionKey, _ => new Entry(currentWindowStart, 0));

            if (entry.WindowStartTicks != currentWindowStart)
            {
                // The window rolled over: replace the stale entry before counting this request.
                if (_entries.TryUpdate(partitionKey, new Entry(currentWindowStart, 1), entry))
                {
                    retryAfter = TimeSpan.Zero;
                    return true;
                }

                continue;
            }

            if (entry.Count >= limit)
            {
                var elapsed = now - entry.WindowStartTicks;
                var remaining = durationTicks - elapsed;
                retryAfter = TimeSpan.FromTicks(Math.Max(1, remaining));
                return false;
            }

            if (_entries.TryUpdate(partitionKey, entry with { Count = entry.Count + 1 }, entry))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }

            // Lost a race with a concurrent request on the same account; retry with fresh state.
        }
    }

    /// <summary>
    /// Drops windows that can no longer reject anything. Invoked whenever the table grows large.
    /// </summary>
    private void Sweep(TimeSpan window)
    {
        var durationTicks = (window <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : window).Ticks;
        var now = Environment.TickCount64;

        foreach (var pair in _entries)
        {
            if (now - pair.Value.WindowStartTicks >= durationTicks)
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(pair.Key, pair.Value));
            }
        }

        if (_entries.Count > MaxPartitions)
        {
            _entries.Clear();
        }
    }

    internal int PartitionCount => _entries.Count;
}
