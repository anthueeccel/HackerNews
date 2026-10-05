# 001 - Best Stories API

Status: Draft for planning

## 1. Goal

Using ASP.NET Core, implement a RESTful API that returns the details of the best `n` stories from the Hacker News API, ordered by score descending, where `n` is provided by the caller.

Hacker News API docs: https://github.com/HackerNews/API

- Best story IDs: https://hacker-news.firebaseio.com/v0/beststories.json
- Story details: https://hacker-news.firebaseio.com/v0/item/{id}.json (example id: 21233041)

## 2. Non-functional requirement (main design driver)

- **NFR-1**: The API must efficiently serve large numbers of concurrent requests without overloading the Hacker News API.

## 3. API contract

`GET /api/stories/best?n=10` returns HTTP 200 with a JSON array in descending order of score:

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

Field mapping from the Hacker News item:

| HN field              | Response field | Notes                                    |
| --------------------- | -------------- | ---------------------------------------- |
| `title`               | `title`        |                                          |
| `url`                 | `uri`          | `null` when the item has no url          |
| `by`                  | `postedBy`     |                                          |
| `time` (Unix seconds) | `time`         | ISO 8601 `DateTimeOffset`, UTC, `+00:00` |
| `score`               | `score`        |                                          |
| `descendants`         | `commentCount` |                                          |

## 4. Functional requirements

- **FR-1 Input validation**: `n` is required, an integer, minimum 1, maximum configurable (default 200, because `beststories` returns at most about 200 IDs). Invalid input returns 400 with `ProblemDetails`.
- **FR-2 Fetching**: get the IDs from `beststories`, take the first `n`, fetch each item, sort by `score` descending, return.
- **FR-3 Bounded parallelism**: fetch items in parallel with a configurable limit (default 10). Never run an unbounded number of concurrent upstream calls.
- **FR-4 Response shape**: matches section 3 exactly (property names, camelCase, time format).

## 5. Caching and protection of the upstream (NFR-1)

- **FR-5 ID list cache**: cache the best-story ID list in memory with a short TTL (default 2 minutes).
- **FR-6 Item cache**: cache each item by ID in memory with a longer TTL (default 10 minutes).
- **FR-7 Configurable TTLs**: both TTLs come from configuration.
- **FR-8 Single-flight**: concurrent requests for the same cache key trigger only one upstream call.
- **FR-9 No failure caching**: failed or cancelled upstream calls are not kept in the cache, so the next request retries.

## 6. Upstream HTTP client

- **FR-10**: typed client through `IHttpClientFactory`. Base address and timeout (default 5 seconds) come from configuration.
- **FR-11**: no retry or circuit-breaker library in this version. Both are listed as README enhancements.

## 7. Edge cases

- **EC-1**: skip items that are null, deleted, dead, or not of type `story`.
- **EC-2**: items without `url` (for example Ask HN) return `uri: null`. Document this assumption in the README.
- **EC-3**: if Hacker News is unreachable or times out, return 502 or 503 with `ProblemDetails`. Do not crash and do not leak internal details.
- **EC-4**: if fewer than `n` valid stories exist, return what is available.

## 8. Observability and operations

- **OPS-1**: structured logging with `ILogger`. Cache hits and misses at Debug, upstream failures at Warning or Error.
- **OPS-2**: `GET /health` using built-in health checks.
- **OPS-3**: OpenAPI document using the built-in ASP.NET Core support, enabled in Development.

## 9. Testing requirements

Framework and conventions are in `.clinerules/01-engineering-rules.md`.

Unit tests for `BestStoriesService` with a faked `IHackerNewsClient`:

- **T-1**: mapping to the public DTO, including Unix time to `DateTimeOffset` (FR-4)
- **T-2**: sorting by score descending (FR-2)
- **T-3**: filtering of null, deleted, dead and non-story items (EC-1)
- **T-4**: `uri` is null when the item has no url (EC-2)
- **T-5**: upstream called once under many concurrent requests (FR-8)
- **T-6**: failed upstream call is not cached, the next call retries (FR-9)
- **T-7**: fewer valid stories than `n` returns what is available (EC-4)

Integration tests with `WebApplicationFactory` and a fake `HttpMessageHandler`:

- **T-8**: 200 happy path with correct JSON shape and order (FR-2, FR-4)
- **T-9**: 400 for invalid `n`: missing, zero, negative, above max, not a number (FR-1)
- **T-10**: 502 or 503 when upstream fails or times out (EC-3)
- **T-11**: `GET /health` returns 200 (OPS-2)

## 10. Delivery artifacts

- **D-1**: multi-stage `Dockerfile` (SDK build, ASP.NET runtime, non-root user if simple) and `.dockerignore`.
- **D-2**: `.gitignore` for .NET.
- **D-3**: minimal GitHub Actions workflow: restore, build, test on push and pull request.
- **D-4**: `README.md` in clear, professional English with:
  1. Overview and the endpoint with example request and response
  2. How to run: `dotnet run`, `dotnet test`, Docker
  3. Configuration options and defaults
  4. Design decisions: single project and why (and how it would be split in a larger system), caching and single-flight, bounded parallelism
  5. Assumptions: first `n` IDs then sort by score, `uri` null when missing, TTLs, max `n`
  6. Enhancements given more time: Redis or other distributed cache, Polly retries and circuit breaker, rate limiting on this API, background cache refresh (`BackgroundService`), OpenTelemetry metrics and tracing, split into Domain/Application/Infrastructure projects, contract tests, authentication
  7. Note that tests use NUnit
  8. "How this was built" note linking to `docs/ai-prompt.md` and `specs/`

## 11. Design decisions and hints

Decided:

- **Minimal API** (no controllers). Keep `Program.cs` thin: register services there and map routes through an extension method such as `MapStoriesEndpoints()` in `Endpoints/`. Validate `n` explicitly and return `ProblemDetails`.

Non-binding hints (propose a better simple option in `plan.md` if you have one):

```
/src/HackerNews.BestStories.Api
  Program.cs
  Endpoints/   StoriesEndpoints (MapStoriesEndpoints extension)
  Services/    IBestStoriesService, BestStoriesService
  Clients/     IHackerNewsClient, HackerNewsClient
  Models/      HackerNewsItem (upstream), StoryResponse (public DTO)
  Options/     HackerNewsOptions, StoriesOptions
/tests/HackerNews.BestStories.Api.Tests
```

Possible single-flight approaches: cache `Lazy<Task<T>>`, or a per-key `SemaphoreSlim`.
Possible bounded parallelism: `Parallel.ForEachAsync` with `MaxDegreeOfParallelism`.

## 12. Out of scope

Authentication, distributed cache, retry and circuit breaker, rate limiting, persistence, and any UI.
