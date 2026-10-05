using System.Collections.Concurrent;

namespace HackerNews.BestStories.Api.Infrastructure;

public sealed record CacheResult<T>(T Value, bool WasHit);

/// <summary>
/// Small in-memory cache with single-flight semantics: concurrent callers for the same key
/// share one underlying task, and only successfully completed results are kept.
/// Expiry is checked lazily on read; faulted entries are evicted so the next request retries.
/// </summary>
public sealed class SingleFlightCache
{
    private sealed record Entry(Lazy<Task<object?>> Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<CacheResult<T>> GetOrAddAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
        {
            try
            {
                var cached = await entry.Value.Value.WaitAsync(cancellationToken);
                return new CacheResult<T>((T)cached!, WasHit: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Evict(key, entry);
                throw;
            }
        }

        if (entry is not null)
        {
            Evict(key, entry);
        }

        // The first caller's token drives the shared operation; cancelling it fails everyone
        // waiting for that value, which is acceptable because a retry will start a new call.
        var lazy = new Lazy<Task<object?>>(
            async () => (object?)await factory(cancellationToken).ConfigureAwait(false));
        var fresh = new Entry(lazy, now + ttl);

        if (!_entries.TryAdd(key, fresh))
        {
            // Another thread won the race; retry the read.
            return await GetOrAddAsync(key, ttl, factory, cancellationToken);
        }

        try
        {
            var value = await lazy.Value.WaitAsync(cancellationToken);
            return new CacheResult<T>((T)value!, WasHit: false);
        }
        catch
        {
            // Never keep failed or cancelled results in the cache.
            Evict(key, fresh);
            throw;
        }
    }

    private void Evict(string key, Entry entry)
    {
        // Only remove the entry that was observed; a newer entry may already be in place.
        ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new(key, entry));
    }
}
