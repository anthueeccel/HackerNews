# Review: 002-implement-imemorycache

Date: 2026-10-06
Verdict: Changes requested

## Build and test

- `dotnet build`: succeeded, 0 warnings, 0 errors (.NET 10.0.303, net10.0).
- `dotnet test`: 17/17 passed, 0 failed, 0 skipped (~5 s).
- No real network calls in tests: unit tests use `FakeHackerNewsClient`; integration tests use `WebApplicationFactory` with `FakeHttpMessageHandler`.

## Requirement traceability

| ID | Status | Evidence (code / test) |
|----|--------|------------------------|
| NFR-2 | Missing | The main design driver is not implemented: `MemoryCacheOptions.SizeLimit` is never set anywhere in the codebase (repo-wide search for `SizeLimit` / `MemoryCacheOptions` matches only `specs/002` documents). `Program.cs:13` calls `AddMemoryCache()` with no options, so the singleton cache has no entry cap. |
| NFR-2a | Missing | `IMemoryCache.SizeLimit = StoriesOptions.MaxCacheEntries` (spec line 43) does not exist in code; `MaxCacheEntries` is never read (see FR-7b). |
| NFR-2b | Covered | `SingleFlightCache.cs:25` (`_inFlight` dictionary), removed in `finally` at lines 53-59; keys are only the fixed `beststories` id-list key (`BestStoriesService.cs:15`) and `item:{id}` keys derived from upstream IDs (`BestStoriesService.cs:60`), so the map cannot exceed the natural key set. |
| NFR-2c | Covered | `SingleFlightCache.cs:80-84`: `AbsoluteExpirationRelativeToNow = ttl` per entry; TTLs from `StoriesOptions` (`StoriesOptions.cs:15-26`). |
| NFR-2d | Partial | Spec claims memory-pressure awareness, but per spec line 43 that behaviour is anchored to `SizeLimit`; with `SizeLimit` null the only eviction path that is actually exercised is TTL expiry. Cannot hold as specified until NFR-2a is implemented. |
| NFR-2e | Covered | `SingleFlightCache.cs:74-92`: `_cache.Set` runs only after `await factory(...)` succeeds; exceptions (including cancellation) propagate from the `Lazy` and nothing is stored. Test T-6 (`BestStoriesServiceTests.cs:132-150`) verifies retry after failure. |
| NFR-2f | Covered | `SingleFlightCache.cs:70` registers `cancellationToken.Register(() => _inFlight.TryRemove(key, out _))`; registration disposed at line 91. A cancelled caller's key is dropped so a fresh request retries. |
| FR-5 | Covered | ID list cached under `beststories` with `settings.IdListTtl` (`BestStoriesService.cs:45-55`); default 120 s from config. |
| FR-6 | Covered | Items cached under `item:{id}` with `settings.ItemTtl` (`BestStoriesService.cs:57-63`); default 600 s from config. |
| FR-7 | Covered | Both TTLs come from `StoriesOptions` (`StoriesOptions.cs:15-26`), bound with `BindConfiguration` and validated at startup (`Program.cs:21-25`). |
| FR-7b | Missing | `StoriesOptions.MaxCacheEntries` (`StoriesOptions.cs:22`) and `appsettings.json:18` exist, but no production code ever reads the property (repo-wide search: hits only in `specs/002` and `appsettings.json`). The configurable limit has no effect. |
| FR-8 | Covered | Single-flight retained on top of `IMemoryCache` (`SingleFlightCache.cs:46-50`). Test T-5 (`BestStoriesServiceTests.cs:112-128`): 20 concurrent requests, exactly 1 upstream call - passes with the real `MemoryCache` created at `BestStoriesServiceTests.cs:32`. |
| FR-9 | Covered | Faulted/cancelled results are never stored (NFR-2e evidence). Test T-6. |
| Section 3 (no API change) | Covered | Endpoint untouched; integration tests T-8...T-11 (`StoriesEndpointIntegrationTests.cs`) still pass. |
| Section 7 (config key) | Partial | Key present in `appsettings.json:18` and `StoriesOptions`, but not applied to `IMemoryCache` and not documented in the README configuration table. |
| Section 10 (files changed) | Covered | All five listed files are modified; `git status` confirms them (plus the untracked `specs/002-implement-imemorycache/`). |
| T-5, T-6, T-1...T-7, T-8...T-11 | Covered | Full suite green: 17/17 (`dotnet test`). |

## Findings

### Blocker

1. **`IMemoryCache.SizeLimit` is never configured - the spec's main design driver is missing (NFR-2, NFR-2a, FR-7b).** `Program.cs:13` registers `builder.Services.AddMemoryCache()` with default options, and neither `MemoryCacheOptions` nor `SizeLimit` appears anywhere in `src/` or `tests/`. Consequently:
   - the entry cap described in spec lines 13, 43 and 74 (`SizeLimit = StoriesOptions.MaxCacheEntries`, default 1000) is not enforced;
   - `StoriesOptions.MaxCacheEntries` (`StoriesOptions.cs:22`) is dead configuration - it violates "No dead code" and the Options rule that configuration must actually drive behaviour;
   - without `SizeLimit`, the memory-pressure claim (NFR-2d) has no capacity target, so the only real eviction path is TTL expiry.
   The spec exists precisely to bound the singleton cache; as shipped, the bounded-capacity guarantee (`plan.md:12`, `plan.md:45`) does not hold. Fix: configure the cache at startup, e.g. `AddMemoryCache(o => o.SizeLimit = ...)` reading `IOptions<StoriesOptions>` (or `PostConfigure<MemoryCacheOptions>`), then re-run the suite.

### Major

1. **README not updated although configuration and internal behaviour changed (Definition of Done).**
   - The configuration table (`README.md:54-59`) lists all other `Stories:*` keys but not `Stories:MaxCacheEntries`.
   - The Design decisions bullet (`README.md:128`) still describes the old design: "a small `SingleFlightCache` (`ConcurrentDictionary` of `Lazy<Task<T>>` entries with absolute expiry)" - storage is now `IMemoryCache`; the `ConcurrentDictionary` only coordinates in-flight tasks.
   - The project-structure tree shows only `specs/001-best-stories-api/` and omits `specs/002-implement-imemorycache/`.
2. **`plan.md` contradicts the implementation and tasks are ticked anyway.** `plan.md:12` and `plan.md:45` state the cache is "Bounded by `StoriesOptions.MaxCacheEntries`" and task 3 ("Wire ... into DI") is checked `[x]`, but the bound was never wired. Engineering rules section 2.5 require updating `plan.md` first when implementation diverges; task 5's claim (`dotnet build` clean, 17 tests pass) is true but gives a false impression of completeness.
3. **Feature work is uncommitted.** `git status` shows the five modified files plus untracked `specs/002-implement-imemorycache/`, while `spec.md` says `Status: Shipped`. The Git rule (small Conventional Commits referencing the feature folder, as done for 001) is not satisfied.

### Minor

1. **Misleading comment on removal semantics.** `SingleFlightCache.cs:55-58` claims "the comparison uses the exact Lazy we observed, so it is only removed if it is still the current entry", but `_inFlight.TryRemove(key, out lazy)` is the unconditional `ConcurrentDictionary.TryRemove(TKey, out TValue)` overload - it removes whatever value is present. Practical impact is limited (it may evict a newer in-flight entry, causing at most one extra upstream call, never a wrong result), but a "why" comment must be accurate. If value comparison is intended, use the `TryRemove(KeyValuePair<TKey, TValue>)` overload.
2. **Redundant qualification.** `Program.cs:34` writes the factory with the fully qualified `Microsoft.Extensions.Caching.Memory.IMemoryCache` although `using Microsoft.Extensions.Caching.Memory;` already exists at line 6; the line is also unnecessarily long for a thin `Program.cs`.
3. **Unused `NSubstitute` package (carried over from 001).** `HackerNews.BestStories.Api.Tests.csproj:10` still references `NSubstitute 6.2.0`; spec line 96 says the unused *import* was removed, but no test uses the library at all - violates "no unnecessary NuGet packages".
4. **README mojibake (carried over from 001).** The Design decisions and Testing sections still contain broken em-dash artifacts; re-save the README as UTF-8.

### Suggestions

1. After wiring `SizeLimit`, add one test asserting `MemoryCacheOptions.SizeLimit == StoriesOptions.MaxCacheEntries` (and optionally that entries with `Size = 1` are evicted past the limit). Spec section 8 waived new tests, but the central NFR deserves direct coverage.
2. Mention cache eviction/size observability (`MemoryCacheStatistics`) in the README "Enhancements given more time" list - it is already implied by spec section 9 but not by the README.
3. Consider `builder.Services.AddMemoryCache(...)` with an inline lambda over `IOptions<StoriesOptions>` instead of a hand-built factory lambda for the singleton registration; it would read cleaner than `Program.cs:34`.

## What is done well

- The single-flight logic was ported correctly and is arguably better than before: the fast path goes straight to `IMemoryCache` (`SingleFlightCache.cs:39-42`), each caller awaits the shared task through its own `WaitAsync(cancellationToken)` (`SingleFlightCache.cs:50`), so one caller's cancellation no longer fails concurrent waiters - this also resolves Minor 3 from the 001 review.
- The test strategy decision is sound: rejecting `Substitute.For<IMemoryCache>()` in favour of a real `MemoryCache` (`BestStoriesServiceTests.cs:32`) because a substitute never stores values and would silently break T-5.
- No new NuGet packages - `IMemoryCache` comes from the shared framework; the production project still references only `Microsoft.AspNetCore.OpenApi`.
- Options pattern kept with `[Range]` validation and `ValidateOnStart` (`StoriesOptions.cs`, `Program.cs:21-25`); TTLs and the new limit are configuration-driven, no magic numbers (documented `Size = 1` excepted and justified in the spec).
- `CacheResult<T>` remains a `record`, `CancellationToken` is still passed through every layer, and the public API contract is untouched - the full 17-test suite, including the integration tests, passes with a clean build.
