# Review: 001-best-stories-api

Date: 2026-10-05
Verdict: Approved with comments

## Build and test

- `dotnet build`: succeeded, 0 warnings, 0 errors (.NET 10, net10.0).
- `dotnet test`: 16/16 passed, 0 failed, 0 skipped (~4 s).
- No real network calls in tests: unit tests use `FakeHackerNewsClient` (`tests/HackerNews.BestStories.Api.Tests/Support/FakeHackerNewsClient.cs`); integration tests use `WebApplicationFactory` with a fake `HttpMessageHandler` (`Support/FakeHttpMessageHandler.cs`).

## Requirement traceability

| ID | Status | Evidence (code / test) |
|----|--------|------------------------|
| NFR-1 | Covered | `SingleFlightCache` (single-flight, `Infrastructure/SingleFlightCache.cs`), `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` (`Services/BestStoriesService.cs:25-36`), ID-list and item caches. |
| FR-1 | Covered | `Endpoints/StoriesEndpoints.cs:30-36` returns `Results.ValidationProblem` for missing/out-of-range `n`; max from `StoriesOptions.MaxCount`. Tests: invalid `n` via `[TestCase]` (0, -5, 201), missing `n`, non-numeric `n` (T-9). |
| FR-2 | Covered | `BestStoriesService.GetBestStoriesAsync`: IDs → take first n → fetch → `OrderByDescending(Score)`. Tests T-2, T-8. |
| FR-3 | Covered | `Parallel.ForEachAsync` with `ParallelOptions.MaxDegreeOfParallelism = StoriesOptions.Parallelism` (default 10). No unbounded fan-out. |
| FR-4 | Covered | `StoryResponse` record, camelCase by default, Unix seconds → `DateTimeOffset.FromUnixTimeSeconds`. Tests T-1, T-8 (asserts `title`, `uri`, `postedBy`, `time`, `score`, `commentCount`). |
| FR-5 | Covered | ID list cached under key `"beststories"` with `IdListTtl` (`BestStoriesService.cs:45-55`). |
| FR-6 | Covered | Items cached under `item:{id}` with `ItemTtl` (`BestStoriesService.cs:57-63`). |
| FR-7 | Covered | Both TTLs from `StoriesOptions` (`Options/StoriesOptions.cs`), bound to config, validated at startup. |
| FR-8 | Covered | `Lazy<Task<object?>>` entries in `SingleFlightCache`; concurrent callers await the same task. Test T-5 asserts 1 ID-list call and 1 item call under 20 concurrent requests. |
| FR-9 | Covered | Faulted/cancelled entries evicted in the `catch` of `GetOrAddAsync` (`SingleFlightCache.cs:62-67`). Test T-6 asserts retry after failure. |
| FR-10 | Covered | Typed client via `AddHttpClient<IHackerNewsClient, HackerNewsClient>`; base address and timeout from `HackerNewsOptions` (default 5 s). |
| FR-11 | Covered | No retry/circuit-breaker package anywhere; README lists both as enhancements. |
| EC-1 | Covered | Filter for null/deleted/dead/non-story in `GetStoryAsync` (`BestStoriesService.cs:68-71`). Test T-3 covers all four cases. |
| EC-2 | Covered | `Uri: item.Url` maps missing url to null. Test T-4. Documented in README Assumptions. |
| EC-3 | Covered | `HttpRequestException` → 502, timeout → 503, both `ProblemDetails` with no internals leaked (`StoriesEndpoints.cs:43-52`). Tests T-10 (502 + no `stackTrace`, 503). |
| EC-4 | Covered | Takes `ids.Take(count)` and returns what maps. Test T-7 (2 available, n=10 → 2). |
| OPS-1 | Covered | `ILogger` used in service (cache hit/miss at Debug) and endpoints (upstream failures at Warning). Not directly asserted by tests — acceptable for log plumbing. |
| OPS-2 | Covered | `app.MapHealthChecks("/health")` (`Program.cs:42`). Test T-11. |
| OPS-3 | Covered | `AddOpenApi()` + `MapOpenApi()` gated on Development (`Program.cs:10, 37-40`). |
| T-1…T-11 | Covered | All mapped in `BestStoriesServiceTests.cs` and `StoriesEndpointIntegrationTests.cs`; comments reference the T-IDs. |
| D-1 | Covered | Multi-stage `Dockerfile` (SDK 10.0 build → aspnet 10.0 runtime, non-root `appuser`), `.dockerignore` present. |
| D-2 | Covered | `.gitignore` present. |
| D-3 | Covered | `.github/workflows/ci.yml`: restore, build, test on push (main) and pull_request. |
| D-4 | Covered | README covers endpoint + examples, run instructions, configuration table, design decisions, assumptions, enhancements, NUnit note, and "How this was built" linking `docs/ai-prompt.md` and `specs/`. |

## Findings

### Blocker

None.

### Major

None.

### Minor

1. **Unused NSubstitute package.** `tests/HackerNews.BestStories.Api.Tests/HackerNews.BestStories.Api.Tests.csproj` references `NSubstitute 6.2.0`, but no test uses it — only hand-written fakes are used. Either remove the reference or use it; as-is it violates the "no unnecessary NuGet packages" rule (rules line 33).
2. **Mojibake in README.** The Design decisions and Testing sections contain `â€"` artifacts (broken em-dash encoding). The README should be re-saved as UTF-8.
3. **First-caller cancellation can fail concurrent waiters.** In `SingleFlightCache.GetOrAddAsync`, the first caller's `CancellationToken` drives the shared `Lazy<Task>`; if that caller is cancelled mid-flight, every concurrent waiter for the same key is cancelled with it (acknowledged in the code comment, `SingleFlightCache.cs:45-48`). Acceptable for this scope (failure is not cached, so a retry recovers), but worth noting as a known limitation.
4. **Type-unsafe cast in cache.** `SingleFlightCache` stores `object?` and casts back to `T` (`SingleFlightCache.cs:31, 60`); safety relies on callers using consistent types per key. Fine here (two call sites, both consistent); a typed cache would add complexity without benefit at this size.

### Suggestions

1. Consider a unit test for the race branch in `SingleFlightCache` (`TryAdd` losing the race) — currently untested, though hard to trigger deterministically.
2. OPS-1 could get a lightweight assertion (a fake `ILogger` capturing log levels) if logging levels ever become contractual.

## What is done well

- The `Lazy<Task<T>>` single-flight cache is small, correct for the requirements (single-flight FR-8, no failure caching FR-9, TTL from config FR-7), and uses only BCL types — no extra caching package.
- Bounded parallelism is real (`Parallel.ForEachAsync` with a configured `MaxDegreeOfParallelism`), not an unbounded `Task.WhenAll` over all IDs.
- Clean Minimal API structure: thin `Program.cs`, `MapStoriesEndpoints()` extension, explicit validation returning `ValidationProblem`.
- Test naming follows `Method_Scenario_ExpectedResult`, constraint model throughout, `[TestCase]` used for invalid `n`, and no test touches the network.
- Options are validated at startup (`ValidateDataAnnotations` + `ValidateOnStart`) so configuration errors fail fast.
- Delivery artifacts (Dockerfile with non-root user, CI, README, `.dockerignore`, `.gitignore`) are all present and match D-1…D-4.
- `plan.md` checklist is fully ticked and matches what was built (minor deviation, noted in the plan itself: the solution file is `HackerNews.slnx` instead of `HackerNews.sln`, which is fine on .NET 10).

