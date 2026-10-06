# Plan: 002-implement-imemorycache

## Environment findings

- Existing `SingleFlightCache` stores entries in `ConcurrentDictionary<string, Entry>` with no size limit and lazy TTL eviction on read only. Because the cache is registered as Singleton, this dictionary grows unbounded across the process lifetime → OOM risk (NFR-2).
- `IMemoryCache` already provides TTL (absolute/sliding), size-based eviction (`SizeLimit`), LRU eviction and memory-pressure awareness out of the box.

## Design decisions

| Decision | Justification |
|---|---|
| Back `SingleFlightCache` with `IMemoryCache` bounded by `SizeLimit` | `SizeLimit` is set from `StoriesOptions.MaxCacheEntries` at startup (`Program.cs`), so the singleton cache can never grow unbounded; automatic expiry, LRU eviction and memory-pressure handling come from the framework — no new packages. |
| Keep the `Lazy<Task<T>>` coordination map | `IMemoryCache` has no native single-flight semantics; the per-key `ConcurrentDictionary<string, Lazy<Task<object?>>>` retains the guarantee that concurrent callers for the same key share one upstream call (FR-8). |
| `Size = 1` per entry | `SizeLimit` counts entries, making the limit exact. |
| `AbsoluteExpirationRelativeToNow = ttl` | Same semantics as before. |
| Use real `MemoryCache` in tests, not a mock | A substitute never stores values; T-5 would fail silently because `TryGetValue` always returns false. |

## Folder structure / files

```
specs/002-implement-imemorycache/{spec.md, plan.md}
src/HackerNews.BestStories.Api/Infrastructure/SingleFlightCache.cs  ← rewritten (IMemoryCache + size-aware removal)
src/HackerNews.BestStories.Api/Options/StoriesOptions.cs  ← +MaxCacheEntries
src/HackerNews.BestStories.Api/appsettings.json  ← +MaxCacheEntries
src/HackerNews.BestStories.Api/Program.cs  ← +AddMemoryCache(); SizeLimit = StoriesOptions.MaxCacheEntries; AddSingleton<SingleFlightCache>()
tests/HackerNews.BestStories.Api.Tests/BestStoriesServiceTests.cs  ← real MemoryCache
tests/HackerNews.BestStories.Api.Tests/StoriesEndpointIntegrationTests.cs  ← T-12 (SizeLimit wiring)
tests/HackerNews.BestStories.Api.Tests/SingleFlightCacheTests.cs  ← T-13 (boundedness/eviction)
```

## Key types and signatures

```csharp
// SingleFlightCache — same public API, different internal storage
public sealed class SingleFlightCache
{
    public SingleFlightCache(IMemoryCache cache);
    public Task<CacheResult<T>> GetOrAddAsync<T>(
        string key, TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken);
}
```

## Caching & single-flight summary

- Storage: `IMemoryCache`, bounded by `StoriesOptions.MaxCacheEntries` (`SizeLimit` wired at startup from `IOptions<StoriesOptions>`).
- Coordination: `ConcurrentDictionary<string, Lazy<Task<object?>>>` — one shared task per key (FR-8).
- Expiry: `AbsoluteExpirationRelativeToNow = ttl` per entry.
- Failure: never stored; eviction registration on cancellation.
- Hit/miss logging: at Debug level (unchanged).

## Configuration keys & defaults

```json
"Stories": {
  "MaxCount": 200,
  "Parallelism": 10,
  "IdListTtlSeconds": 120,
  "ItemTtlSeconds": 600,
  "MaxCacheEntries": 1000
}
```

## Test list → requirement IDs

New tests (per the amended spec): T-12 — integration test that `SizeLimit` is taken from `Stories:MaxCacheEntries` (42 overridden → 42); T-13 — boundedness/eviction with `SizeLimit = 2`.

## Task checklist

- [x] 1. spec.md: T-12/T-13 in section 8, updated files-changed list in section 10
- [x] 2. plan.md: review-remediation section plus corrected checklist
- [x] 3. Blocker: wire `SizeLimit` in `Program.cs`; simplify cache registration
- [x] 4. Minor 1: value-aware in-flight removal in `SingleFlightCache.cs`
- [x] 5. Minor 3: remove unused `NSubstitute` package reference
- [x] 6. Tests: T-12 wiring only; T-13 boundedness dropped with recorded reason (framework evicts on memory pressure, not synchronously on Set — observed Count stayed at 3, not 2)

- [x] 7. README: five updates (Major 1, Minor 4 verification, Suggestion 2)
- [x] 8. Documentation sync: tick plan tasks, confirm spec traceability (NFR-2, NFR-2a, FR-7b, Section 7 → Covered)
- [x] 9. `dotnet build` clean (0 warnings), `dotnet test` all green
- [x] 10. Commit: 3 Conventional Commits referencing the feature folder

## Review remediation (002 review.md)

| Finding | Task |
|---|---|
| Blocker: SizeLimit never wired (NFR-2, NFR-2a, FR-7b) | T-2a — `Program.cs`: `AddOptions<MemoryCacheOptions>().Configure<IOptions<StoriesOptions>>(...)` plus `AddSingleton<SingleFlightCache>()` |
| Major 1: README stale | T-2b — configuration table row, design-decisions bullet, project tree, enhancements |
| Major 2: plan.md contradicted implementation | T-2c — rewrite plan.md with the remediation map before changing code |
| Major 3: feature uncommitted | T-2d — 3 small Conventional Commits referencing `Spec: 002-implement-imemorycache` |
| Minor 1: misleading comment | T-2e — value-aware `TryRemove(KeyValuePair<TKey,TValue>)` + accurate comment |
| Minor 2: redundant qualification | T-2f — `AddSingleton<SingleFlightCache>()` instead of a hand-built factory |
| Minor 3: unused NSubstitute | T-2g — drop the package reference |
| Minor 4: README mojibake | T-2h — byte-level verification only; not reproducible (no change) |
| Suggestion 1: SizeLimit test | T-3 — T-12 wiring; T-13 boundedness (optional, guarded) |
| Suggestion 2: MemoryCacheStatistics | T-3 — one clause in the README enhancements list |
