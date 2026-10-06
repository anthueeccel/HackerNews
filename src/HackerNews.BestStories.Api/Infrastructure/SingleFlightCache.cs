using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace HackerNews.BestStories.Api.Infrastructure;

/// <summary>
/// Result returned by <see cref="GetOrAddAsync{T}"/>: the cached value and whether it was
/// already stored (hit) or retrieved by the factory call (miss).
/// </summary>
public sealed record CacheResult<T>(T Value, bool WasHit);

/// <summary>
/// In-memory cache with single-flight semantics: concurrent callers for the same key
/// share one underlying upstream call, and only successfully completed results are kept.
///
/// Storage is backed by <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> so
/// expiry (TTL) and eviction are managed by the framework: an entry's size is tracked via
/// <see cref="MemoryCacheEntryOptions.Size"/> and the cache is bounded by a configurable
/// maximum entry count. A faulted or cancelled upstream call is never stored, so the next
/// request retries as before.
/// </summary>
public sealed class SingleFlightCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlight = new();

    public SingleFlightCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<CacheResult<T>> GetOrAddAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        // 1. Fast path: served from IMemoryCache without touching the single-flight map.
        if (_cache.TryGetValue(key, out T? cached))
        {
            return new CacheResult<T>(cached!, WasHit: true);
        }

        // 2. Single-flight: get or create the shared task for this key. If another thread
        //    already entered the map, it wins the race and this caller just awaits the same task.
        var lazy = _inFlight.GetOrAdd(key, _ => CreateLazyTask(key, ttl, factory, cancellationToken));

        try
        {
            var value = await lazy.Value.WaitAsync(cancellationToken);
            return new CacheResult<T>((T)value!, WasHit: false);
        }
        finally
        {
            // Remove this entry from the in-flight map. TryRemove(KeyValuePair) compares the
            // exact Lazy we observed, so only the current entry is removed and a retry by
            // another caller cannot be evicted by mistake.
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, lazy));
        }
    }

    private Lazy<Task<object?>> CreateLazyTask<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        // If the caller cancels before this operation completes, drop the key from the
        // in-flight map so a fresh request can retry instead of awaiting a dead operation.
        var registration = cancellationToken.Register(() => _inFlight.TryRemove(key, out _));

        return new Lazy<Task<object?>>(async () =>
        {
            try
            {
                var result = await factory(cancellationToken).ConfigureAwait(false);

                // Only successfully completed results are stored. Size = 1 and a relative TTL
                // let IMemoryCache enforce the entry limit and automatic expiry.
                _cache.Set(key, result, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = ttl,
                });

                return result!;
            }
            finally
            {
                // Never keep failed or cancelled results in the cache; clean up the registration.
                registration.Dispose();
            }
        });
    }
}
