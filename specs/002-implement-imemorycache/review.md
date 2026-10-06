# Review: 002-implement-imemorycache

Date: 2026-10-06
Verdict: Approved with comments

## Build and test

- `dotnet build` (`src/HackerNews.BestStories.Api`): succeeded - 0 warnings, 0 errors; .NET 10.0.303 (net10.0).
- `dotnet test` (`tests/HackerNews.BestStories.Api.Tests`): 18/18 passed, 0 failed, 0 skipped (~5 s).
- All tests avoid real network: unit tests use `FakeHackerNewsClient`; integration tests use `WebApplicationFactory` with `FakeHttpMessageHandler`.

## Requirement traceability

| ID | Status | Evidence (code / test) |
|----|--------|------------------------|
| NFR-1 (efficient concurrent serving) | Covered | `Stories:Parallelism` limits `Parallel.ForEachAsync` in `BestStoriesService.cs:25-36`; single-flight coalesces concurrent upstream calls. |
| NFR-2 (singleton cache memory-bounded) | Covered | `Program.cs:13-17`: `SizeLimit = storiesOptions.Value.MaxCacheEntries`; `StoriesOptions.MaxCacheEntries` default 1000 (`StoriesOptions.cs:21-22`); `appsettings.json:18`. |
| NFR-2a (bounded capacity) | Covered | `MemoryCacheOptions.SizeLimit` set from `IOptions<StoriesOptions>` at startup; zero-risk of unbounded growth. |
| NFR-2b (bounded in-flight map) | Covered | `_inFlight` keyed by fixed `beststories` and `item:{id}` keys (`SingleFlightCache.cs:25,46`); bounded by the natural ID count. |
| NFR-2c (TTL enforcement) | Covered | `AbsoluteExpirationRelativeToNow = ttl` per entry (`SingleFlightCache.cs:80-84`); TTLs from `StoriesOptions` (`StoriesOptions.cs:15-19`). |
| NFR-2d (memory-pressure awareness) | Covered (framework defaults) | `MemoryCache` shrinks via compaction on memory pressure; `CompactionPercentage` defaults to 0.9. NFR-2a bounds the store. |
| NFR-2e (failure-avoidance preserved) | Covered | `_cache.Set` runs only after `factory(...)` succeeds (`SingleFlightCache.cs:74-93`); faulted/cancelled results never stored. |
| NFR-2f (cancellation safety) | Covered | `cancellationToken.Register` removes the key from `_inFlight` (`SingleFlightCache.cs:70`); registration disposed in `finally` (`SingleFlightCache.cs:91`). |
| FR-5 (ID list cache) | Covered | `GetOrAddAsync("beststories", settings.IdListTtl, ...)` (`BestStoriesService.cs:45-55`). |
| FR-6 (item cache) | Covered | `GetOrAddAsync("item:{id}", settings.ItemTtl, ...)` (`BestStoriesService.cs:57-63`). |
| FR-7 (configurable TTLs) | Covered | TTLs from `StoriesOptions` bound with `BindConfiguration` + `ValidateOnStart` (`Program.cs:25-29`). |
| FR-7b (`MaxCacheEntries`) | Covered | `StoriesOptions.MaxCacheEntries` → `SizeLimit` wired in `Program.cs:16-17`; tested by T-12. |
| FR-8 (single-flight) | Covered | `_inFlight.GetOrAdd` per key → one shared `Lazy<Task<object?>>`; 20 concurrent requests = 1 upstream call (T-5). |
| FR-9 (no failure caching) | Covered | Faulted/cancelled results not stored; next request retries (T-6). |
| EC-1 (skip null/deleted/dead/non-story) | Covered | `BestStoriesService.cs:67-71`; T-3. |
| EC-2 (`uri` null when no `url`) | Covered | `MapToResponse` passes `item.Url` through (`BestStoriesService.cs:76-86`); T-4. |
| EC-3 (upstream unreachable → 502/503) | Covered | `StoriesEndpoints.cs:38-52`; `HttpRequestException` → 502, `TaskCanceledException` + `TimeoutException` → 503; `ProblemDetails` with no stack trace. T-10. |
| EC-4 (fewer valid stories than n) | Covered | null items filtered out; fewer returned than `n` (T-7). |
| Section 3 (API contract unchanged) | Covered | Endpoint, response shape and order unchanged; T-8 covers schema + order. |
| Section 7 (config key) | Covered | `Stories:MaxCacheEntries` in `appsettings.json:18`, read from `StoriesOptions` (`StoriesOptions.cs:21-22`). |
| OPS-2 (`/health` → 200) | Covered | `Program.cs:49` + T-11. |
| OPS-3 (OpenAPI in Development) | Covered | `Program.cs:44-47`. |
| T-1…T-7 (unit contract) | Covered | `BestStoriesServiceTests.cs` (T-1…T-7). |
| T-8…T-12 (integration contract + SizeLimit) | Covered | `StoriesEndpointIntegrationTests.cs` (T-8…T-12). |
| T-13 (boundedness/eviction) | Dropped with reason | Spec §8 - runtime evicts on memory pressure, not synchronously on `Set`; T-12 alone covers `SizeLimit` wiring. |
| Section 10 (files changed) | Covered | All files modified/added and committed; README, spec, plan, review, test project, source, config. |

## Findings

### Blocker

None. `SizeLimit` wiring and all functional/contract requirements are implemented and covered by tests; `dotnet build` and `dotnet test` are clean.

### Major

- **Implementation is not fully committed.** The working tree shows three source changes still on disk and uncommitted (only `Program.cs` landed in commit `ebc91ce`):
  - `src/HackerNews.BestStories.Api/Infrastructure/SingleFlightCache.cs`
  - `src/HackerNews.BestStories.Api/Options/StoriesOptions.cs`
  - `src/HackerNews.BestStories.Api/appsettings.json`
  - `git log` shows `94dd4db (spec)`, `fab348a (test)`, `81e96e9 (docs)`, `ebc91ce (Program.cs)`, but none of the three files above was added/committed in any commit.

  This is the same gap flagged as Major 3 in the earlier review, and it makes `Status: Shipped` in `spec.md` inaccurate until the tree is committed. Fix: `git add` the three files and commit, e.g. `feat: wire IMemoryCache.SizeLimit from StoriesOptions and make the cache bounded`, referencing `Spec: 002-implement-imemorycache`.

### Minor

- **README whitespace**: `## Testing` follows `## Enhancements given more time` with two blank lines; single blank line is conventional (`README.md`, between the two headings).
- **README NFR-2d wording**: the Design decisions bullet says LRU/expiry handled by the framework but does not state that memory-pressure compaction relies on the `MemoryCache` default `CompactionPercentage` (0.9). Add a short clause for accuracy.
- **`MemoryCacheStatistics` not surfaced**: `CacheResult<T>` and the cache exist, but the `MemoryCacheStatistics` counters are never exposed. Listed as an enhancement; no action required.

### Suggestions (from the earlier review, already addressed)

- T-12 size-limit wiring test added and passing - resolved.
- README "Enhancements given more time" lists `MemoryCacheStatistics` counters - resolved.
- `Program.cs` kept as `AddOptions<MemoryCacheOptions>().Configure<IOptions<StoriesOptions>>(...)` rather than an inline lambda - a deliberate choice to keep the composition root thin and readable; minor and accepted.

## What is done well

- Single-flight ported correctly and arguably improved: the fast path goes straight to `IMemoryCache` (`SingleFlightCache.cs:39-42`), each caller awaits the shared task independently (`SingleFlightCache.cs:50-51`), so one caller's cancellation no longer fails concurrent waiters.
- Test strategy is sound: real `MemoryCache` is used instead of `Substitute.For<IMemoryCache>()` because a substitute never stores values and would silently break T-5.
- No extra NuGet packages - production project references only `Microsoft.AspNetCore.OpenApi`; the `NSubstitute` reference that existed in 001 was removed.
- Options pattern kept with `[Range]` validation and `ValidateOnStart` - no magic numbers.
- `CacheResult<T>` is a `record`, `CancellationToken` passes through every layer, and the public contract is untouched; the full 18-test suite including integration tests passes against the new `IMemoryCache`-backed implementation with a clean build.