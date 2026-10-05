# Plan: 001-best-stories-api

## Environment findings

- Workspace contains only `.clinerules/` docs, `.gitignore`, `LICENSE`, `README.md` — no code yet. Greenfield implementation.
- Installed SDK: .NET 10.0.303 → target framework `net10.0` (latest installed LTS).
- Spec source of truth copied from `.clinerules/spec.md` to `specs/001-best-stories-api/spec.md`.

## Design decisions (beyond the spec's fixed Minimal API)

| Decision | Justification |
|---|---|
| In-memory cache implemented as a small `ConcurrentDictionary` + `Lazy<Task<T>>` | Gives single-flight (FR-8) and failure-avoidance (FR-9) for free: only completed successes are returned; a faulted entry is evicted so the next call retries. No extra NuGet packages. |
| Bounded parallelism via `Parallel.ForEachAsync(..., MaxDegreeOfParallelism)` | Built-in, simplest correct option (FR-3). |
| JSON: `System.Text.Json` with camelCase naming | Built-in; exact contract control. `time` serialized as ISO 8601 `DateTimeOffset` UTC. |
| Validation manual (no FluentValidation) | One parameter `n`; manual check returning `ProblemDetails` via `Results.ValidationProblem` is simplest. |
| Error mapping: `HttpRequestException`/timeout → 502/503 `ProblemDetails` (EC-3) | No internal details leak; structured log at Warning. |
| No retry/Polly, no distributed cache | Explicitly out of scope (spec §12). |
| Solutions layout: `HackerNews.sln` with `src/HackerNews.BestStories.Api` + `tests/HackerNews.BestStories.Api.Tests` | Matches rules (one production + one test project) and spec §11. |
| Upstream item fetch failure policy: treat as request failure (502) | Simpler; spec only mandates skip for null/deleted/dead/non-story items (EC-1). Failures are not cached (FR-9). |

## Folder structure / files

```
HackerNews.sln
.gitignore, README.md, docs/ai-prompt.md
Dockerfile, .dockerignore, .github/workflows/ci.yml
specs/001-best-stories-api/{spec.md, plan.md}
src/HackerNews.BestStories.Api/
  Program.cs, appsettings.json, appsettings.Development.json
  Endpoints/StoriesEndpoints.cs
  Services/IBestStoriesService.cs, BestStoriesService.cs
  Clients/IHackerNewsClient.cs, HackerNewsClient.cs
  Models/HackerNewsItem.cs, StoryResponse.cs
  Options/HackerNewsOptions.cs, StoriesOptions.cs
  Infrastructure/SingleFlightCache.cs
tests/HackerNews.BestStories.Api.Tests/
  Support/FakeHackerNewsClient.cs, FakeHttpMessageHandler.cs
  BestStoriesServiceTests.cs, StoriesEndpointIntegrationTests.cs
```

NuGet packages (all justified): `Microsoft.AspNetCore.OpenApi`; test: `Microsoft.AspNetCore.Mvc.Testing`, `NUnit`, `NUnit3TestAdapter`, `NSubstitute`, `Microsoft.NET.Test.Sdk`.

## Key types and signatures

```csharp
record HackerNewsOptions { string BaseAddress; TimeSpan Timeout; }   // defaults: https://hacker-news.firebaseio.com/v0/, 5s
record StoriesOptions { int MaxCount = 200; int Parallelism = 10; TimeSpan IdListTtl = 2min; TimeSpan ItemTtl = 10min; }

record HackerNewsItem(long Id, bool Deleted, bool Dead, string? Type, string? Title,
                      string? Url, string? By, long? Time, int? Score, int? Descendants);
record StoryResponse(string Title, string? Uri, string PostedBy, DateTimeOffset Time, int Score, int CommentCount);

interface IHackerNewsClient {
    Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken ct);
    Task<HackerNewsItem?> GetItemAsync(long id, CancellationToken ct);
}
interface IBestStoriesService {
    Task<IReadOnlyList<StoryResponse>> GetBestStoriesAsync(int count, CancellationToken ct);
}
class SingleFlightCache {
    Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct);
    // entry = (Lazy<Task<T>>, absoluteExpiration); hit & not expired → return; expired or faulted → remove and recompute
}
static class StoriesEndpoints { static IEndpointRouteBuilder MapStoriesEndpoints(this IEndpointRouteBuilder app); }
// GET /api/stories/best?n={n} → 200 array | 400 ValidationProblem | 502/503 Problem
```

## Caching & single-flight summary

- ID list: 1 entry, TTL `StoriesOptions.IdListTtl`.
- Items: per-ID entries, TTL `ItemTtl`.
- `Lazy<Task<T>>` semantics ⇒ concurrent callers for the same key await the same task (FR-8). Faulted entries are evicted (FR-9). Expiry checked on read (lazy eviction).
- Logging per OPS-1: hits/misses at Debug, upstream failures at Warning.

## Configuration keys & defaults (`appsettings.json`)

```json
{
  "HackerNews": { "BaseAddress": "https://hacker-news.firebaseio.com/v0/", "TimeoutSeconds": 5 },
  "Stories":    { "MaxCount": 200, "Parallelism": 10, "IdListTtlSeconds": 120, "ItemTtlSeconds": 600 }
}
```

## Test list → requirement IDs

Unit (`BestStoriesServiceTests`): T-1 mapping/time conversion (FR-4), T-2 sort desc (FR-2), T-3 filter null/deleted/dead/non-story (EC-1), T-4 null uri (EC-2), T-5 single upstream call under concurrency (FR-8), T-6 failure not cached / retries (FR-9), T-7 fewer than n returns available (EC-4).

Integration (`StoriesEndpointIntegrationTests`): T-8 200 happy path JSON shape & order (FR-2, FR-4), T-9 400 invalid `n` (FR-1), T-10 502/503 upstream failure & timeout (EC-3), T-11 `/health` 200 (OPS-2).

## Task checklist

- [x] 1. Scaffold solution and projects; copy spec.md; write plan.md
- [x] 2. Options and models
- [x] 3. HackerNewsClient
- [x] 4. Single-flight cache and BestStoriesService
- [x] 5. Endpoints, health checks, OpenAPI
- [x] 6. Unit tests (T-1…T-7)
- [x] 7. Integration tests (T-8…T-11)
- [x] 8. Delivery artifacts (Dockerfile, .dockerignore, CI, docs/ai-prompt.md)
- [x] 9. README rewrite; final green build + tests
