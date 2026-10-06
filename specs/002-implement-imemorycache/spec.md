# 002 - Replace custom cache with `IMemoryCache`

Status: Shipped

## 1. Goal

Replace the hand-written `SingleFlightCache` (which stored entries in a plain `ConcurrentDictionary` with no size limit) with a cache that is bounded by `Microsoft.Extensions.Caching.Memory.IMemoryCache`. The single-flight coordination logic is retained, but expiry (TTL) and eviction are delegated to the framework so the process-wide Singleton cache can never grow unbounded.

**References:** `specs/001-best-stories-api/spec.md` (FR-5, FR-6, FR-7, FR-8, FR-9), the discussion in the task that motivated the change (`SingleFlightCache` Singleton lifetime + OOM risk).

## 2. Non-functional requirement (main design driver)

- **NFR-2**: The Singleton cache must be memory-bounded. A fixed maximum entry count is enforced by `IMemoryCache.SizeLimit`, so even a malicious or pathological key set cannot cause `OutOfMemoryException` or unbounded growth.

## 3. API contract

No external API changes. The `GET /api/stories/best?n=...` endpoint, response shape and behaviour are identical.

Internal API change:

```csharp
// Before:
public SingleFlightCache() { }

// After:
public SingleFlightCache(IMemoryCache cache)
{
    _cache = cache;
}
```

## 4. Functional requirements

- **FR-5 ID list cache**: unchanged — still cached in memory with a short TTL (default 2 minutes), TTL from configuration.
- **FR-6 Item cache**: unchanged — still cached per-ID in memory with a longer TTL (default 10 minutes), TTL from configuration.
- **FR-7 Configurable TTLs**: unchanged — TTLs come from `StoriesOptions`.
- **FR-8 Single-flight**: unchanged — concurrent callers for the same key trigger one upstream call (`Lazy<Task<T>>` coordination on top of `IMemoryCache`).
- **FR-9 No failure caching**: unchanged — faulted or cancelled results are never stored, so the next request retries.
- **FR-7b New**: `MaxCacheEntries` is a new configurable limit on the total number of cache entries (default 1000). Configured via `Stories:MaxCacheEntries`.

## 5. Caching and protection of the upstream (NFR-1, NFR-2)

- **NFR-2a**: bounded capacity — `IMemoryCache.SizeLimit = StoriesOptions.MaxCacheEntries` (default 1000), LRU eviction handled by the framework.
- **NFR-2b**: bounded `in-flight` map — the coordination dictionary is keyed by the same bounded key set (max ~201 keys), so it cannot grow beyond the natural limit of the API.
- **NFR-2c**: TTL enforcement — `AbsoluteExpirationRelativeToNow = ttl` passed per entry; `IMemoryCache` evicts expired entries automatically.
- **NFR-2d**: memory-pressure awareness — `IMemoryCache` monitors `GC.GetTotalMemory` and evicts entries before the process runs out of memory.
- **NFR-2e**: failure-avoidance preserved — the factory’s result is only added to `IMemoryCache` after the upstream call completes successfully; exceptions (including `OperationCanceledException`) are rethrown and never cached.
- **NFR-2f**: cancellation safety — if the caller cancels before the operation completes, the cancellation registration removes the key from the `in-flight` map so a fresh request can retry instead of awaiting a dead operation.

## 6. Design decisions

| Decision | Justification |
|---|---|
| Use `IMemoryCache` as the storage backend, keep `ConcurrentDictionary<string, Lazy<Task<object?>>>` for coordination | `IMemoryCache` provides TTL, size limits, LRU eviction and memory-pressure handling out of the box; the `Lazy<Task>` coordination layer is still required because `IMemoryCache` has no native single-flight semantics. |
| Size = 1 per entry | `IMemoryCache.SizeLimit` counts entries, not bytes; every entry contributes the same unit, making the count limit exact. |
| `AbsoluteExpirationRelativeToNow` instead of an absolute expiry | Same elapsed TTL semantics as before; no drift between successive reads. |
| Reject the mock-based test approach (`Substitute.For<IMemoryCache>()`) and use a real `MemoryCache` | A substitute never stores values, which silently broke single-flight verification (T-5). |
| Keep `SingleFlightCache` name and public API | Minimal churn in callers (`BestStoriesService`, `Program.cs` registration). |

## 7. Configuration (new key)

`appsettings.json`:

```json
"Stories": {
  "MaxCount": 200,
  "Parallelism": 10,
  "IdListTtlSeconds": 120,
  "ItemTtlSeconds": 600,
  "MaxCacheEntries": 1000
}
```

`StoriesOptions.MaxCacheEntries` (default 1000) is exposed via `IOptions<StoriesOptions>` and applied to `IMemoryCache.SizeLimit` at startup.

## 8. Testing requirements

- No new tests added; existing suite re-runs against the new implementation:
  - T-5 (FR-8): 20 concurrent requests → exactly 1 upstream call each. Passes only with a real `MemoryCache`.
  - T-6 (FR-9): failed upstream call not cached, next request retries.
  - T-1…T-4, T-7 (FR-4, FR-2, EC-1, EC-2, EC-4): unchanged behaviour.
  - Integration tests T-8…T-11: unchanged endpoint contract.

- **T-12 (new, review finding)**: `SizeLimit` is applied from configuration. An integration
  test starts the app with `Stories:MaxCacheEntries` overridden to 42 and asserts that
  `IOptions<MemoryCacheOptions>.Value.SizeLimit == 42`, proving FR-7b / NFR-2a end to end.
  (Spec section 8 waived new tests, but the central NFR deserves direct coverage — see
  `specs/002-implement-imemorycache/review.md` Suggestion 1.)

- **T-13 (new, review finding, optional)**: dropped before implementation — the runtime does
  not evict synchronously on `Set`; entries only compact on memory pressure. Keeping the test
  would assert framework-specific timing rather than our design. T-12 covers the required
  assertion (SizeLimit applied from configuration).

## 9. Out of scope

- Distributed caching (Redis / other) — listed as an enhancement in `specs/001-best-stories-api/spec.md` §6 (FR-7) and README.
- Background cache refresh (`BackgroundService`) — would need `IPostConfigureOptions`/`PostConfigure` cache entries; out of scope.
- Metrics for cache hit rate / eviction count (would use `MemoryCacheStatistics` or DI diagnostics) — future work.

## 10. Files changed

- `src/HackerNews.BestStories.Api/Infrastructure/SingleFlightCache.cs` — rewritten to use `IMemoryCache`; value-aware in-flight removal; `Size` per entry; updated comment.
- `src/HackerNews.BestStories.Api/Options/StoriesOptions.cs` — added `MaxCacheEntries` (default 1000).
- `src/HackerNews.BestStories.Api/appsettings.json` — added `MaxCacheEntries: 1000`.
- `src/HackerNews.BestStories.Api/Program.cs` — `AddMemoryCache()` plus `SizeLimit = StoriesOptions.MaxCacheEntries`; `AddSingleton<SingleFlightCache>()`.
- `tests/HackerNews.BestStories.Api.Tests/HackerNews.BestStories.Api.Tests.csproj` — removed unused `NSubstitute` package.
- `tests/HackerNews.BestStories.Api.Tests/StoriesEndpointIntegrationTests.cs` — added T-12 (SizeLimit wiring).
- `README.md` — configuration table row, design-decisions bullet, project tree, enhancements.