![.Net](http://img.shields.io/badge/-v10.0-008999?style=plastic&logo=.net&logoColor=ffffff) [![Build](https://github.com/anthueeccel/HackerNews/actions/workflows/build-test.yml/badge.svg)](https://github.com/anthueeccel/HackerNews/actions/workflows/dotnet.yml) [![E2E Tests](https://github.com/anthueeccel/HackerNews/actions/workflows/e2e-tests.yml/badge.svg)](https://github.com/anthueeccel/HackerNews/actions/workflows/e2e-tests.yml) ![last_commit](https://img.shields.io/github/last-commit/anthueeccel/HackerNews) ![license](https://img.shields.io/github/license/anthueeccel/HackerNews)

# HackerNews Best Stories API

A RESTful API built with ASP.NET Core (Minimal API, .NET 10) that returns the details of the best `n` Hacker News stories, ordered by score descending. It protects the upstream [Hacker News API](https://github.com/HackerNews/API) from load with in-memory caching, single-flight request coalescing, and bounded parallelism.
**Repository**: https://github.com/anthueeccel/HackerNews 

## Endpoint

`GET /api/stories/best?n=10`

Example response (HTTP 200):

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

| Status    | Meaning                                                                             |
| --------- | ----------------------------------------------------------------------------------- |
| 200       | Success, JSON array ordered by score descending                                     |
| 400       | `n` missing, not a number, `< 1` or above the configured maximum (`ProblemDetails`) |
| 502 / 503 | Upstream unreachable / timed out (`ProblemDetails`, no internal details leaked)     |

There is also a health check at `GET /health`, and an OpenAPI document at `/openapi/v1.json` in Development.

## How to run

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (and Docker, optional).
The API listens on `http://localhost:5149` (see the console output of `dotnet run`; in Docker it is `http://localhost:8080`).

```bash
dotnet run --project src/HackerNews.BestStories.Api   # run
dotnet test                                           # tests (NUnit)
dotnet test --filter "FullyQualifiedName~BestStoriesE2ETests"   # E2E test (real Hacker News API)
docker build -t hackernews-api .                      # Docker
docker run -p 8080:8080 hackernews-api
```

The E2E test (`BestStoriesE2ETests`) is the only test that calls the real Hacker News API over the network;
it is excluded from the default offline test run and is executed as a separate step in CI (a failure fails the build).

## Configuration

| Key                         | Default                                  | Description                          |
| --------------------------- | ---------------------------------------- | ------------------------------------ |
| `HackerNews:BaseAddress`    | `https://hacker-news.firebaseio.com/v0/` | Hacker News API base address         |
| `HackerNews:TimeoutSeconds` | `5`                                      | Upstream HTTP timeout                |
| `Stories:MaxCount`          | `200`                                    | Maximum allowed `n`                  |
| `Stories:Parallelism`       | `10`                                     | Max concurrent upstream item fetches |
| `Stories:IdListTtlSeconds`  | `120`                                    | Cache TTL for the best-story ID list |
| `Stories:ItemTtlSeconds`    | `600`                                    | Cache TTL for each story item        |

| `Stories:MaxCacheEntries` | `1000` | Maximum cache entry count for the `IMemoryCache` backing store (every entry is registered with `Size = 1`) |

All options are validated at startup; invalid values fail fast.

## Project structure

```text
HackerNews/
├── .clinerules/                          # Engineering rules, spec/review workflows, AI prompts
│   ├── 01-engineering-rules.md
│   ├── ai-prompt.md
│   ├── README.md
│   ├── review.md
│   └── spec.md
├── .github/
│   └── workflows/
│       ├── build-test.yml                # CI: restore, build, test on push/PR
│       └── e2e-tests.yml                 # End-to-end test workflow
├── docs/
│   └── ai-prompt.md                      # "How this was built" note
├── specs/
│   └── 001-best-stories-api/
│       ├── spec.md                       # What and why (source of truth)
│       ├── plan.md                       # How, plus task checklist
│       └── review.md                     # Review workflow output
├── src/
│   └── HackerNews.BestStories.Api/
│       ├── Clients/
│       │   ├── IHackerNewsClient.cs      # Upstream HN API abstraction
│       │   └── HackerNewsClient.cs       # Typed client via IHttpClientFactory
│       ├── Endpoints/
│       │   └── StoriesEndpoints.cs       # MapStoriesEndpoints extension (Minimal API)
│       ├── Infrastructure/
│       │   └── SingleFlightCache.cs      # In-memory cache with single-flight + TTLs
│       ├── Models/
│       │   ├── HackerNewsItem.cs         # Upstream item model
│       │   └── StoryResponse.cs          # Public DTO (record)
│       ├── Options/
│       │   ├── HackerNewsOptions.cs      # Base address, timeout
│       │   └── StoriesOptions.cs         # Max n, parallelism, cache TTLs
│       ├── Properties/
│       │   └── launchSettings.json
│       ├── Services/
│       │   ├── IBestStoriesService.cs
│       │   └── BestStoriesService.cs     # Fetch, filter, sort by score desc
│       ├── Program.cs                    # Thin composition root
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── HackerNews.BestStories.Api.csproj
├── tests/
│   └── HackerNews.BestStories.Api.Tests/
│       ├── Support/
│       │   ├── FakeHackerNewsClient.cs   # Unit-test fake
│       │   └── FakeHttpMessageHandler.cs # Integration-test HTTP fake
│       ├── BestStoriesServiceTests.cs    # Unit tests (T-1 … T-7)
│       ├── StoriesEndpointIntegrationTests.cs
│       ├── BestStoriesE2ETests.cs
│       └── HackerNews.BestStories.Api.Tests.csproj
├── .dockerignore
├── .gitignore
├── Dockerfile
├── HackerNews.slnx
├── LICENSE
└── README.md
```

## Design decisions

- **Single project** (`src/HackerNews.BestStories.Api`) plus one test project. For a service this small, project separation adds ceremony without value; separation of concerns is expressed with folders and interfaces (`Clients/`, `Services/`, `Endpoints/`, `Models/`, `Options/`). In a larger system it would split into Domain/Application/Infrastructure projects.
- **Caching and single-flight**: the `SingleFlightCache` is backed by `IMemoryCache` — the cache is bounded by `Stories:MaxCacheEntries` (`SizeLimit`), every entry is registered with `Size = 1`, and expiry/LRU eviction are managed by the framework. A `ConcurrentDictionary<string, Lazy<Task<T>>>` keeps one shared upstream call per key, so concurrent requests for the same key trigger exactly one upstream call; a failed or cancelled call is never stored and is retried on the next request.
- **Bounded parallelism**: items are fetched with `Parallel.ForEachAsync` capped by `Stories:Parallelism` — never an unbounded fan-out over all IDs.
- **TTLs, timeout, max `n` and parallelism are all configuration**, no magic numbers in code.
- **Typed client** through `IHttpClientFactory` (`HackerNewsClient`), no retry/circuit-breaker library in this version.

## Assumptions

- The first `n` IDs from `beststories` are taken, then the resulting stories are sorted by score descending (not all ~200 IDs are fetched to answer the question).
- Items that are null, deleted, dead, or not of type `story` are skipped; if fewer than `n` valid stories exist, what is available is returned.
- `uri` is `null` when the item has no `url` (for example Ask HN posts).
- An individual failed item fetch fails the whole request (502) rather than silently skipping, so callers get a consistent snapshot or an error.

## Enhancements given more time

Redis or another distributed cache, Polly retries and circuit breaker, rate limiting on this API, background cache refresh (`BackgroundService`), OpenTelemetry metrics and tracing, splitting into Domain/Application/Infrastructure projects, contract tests against the upstream shape, authentication, and cache size observability (`MemoryCacheStatistics` counters).


## Testing

Tests use **NUnit** with the constraint model. Unit tests cover the service with a faked `IHackerNewsClient`; integration tests use `WebApplicationFactory` with a fake `HttpMessageHandler` — no real network calls are ever made except the separate E2E test (`BestStoriesE2ETests`), which calls the real Hacker News API. One integration test additionally asserts that the `IMemoryCache` size limit is wired from configuration (`Stories:MaxCacheEntries` → `MemoryCacheOptions.SizeLimit`).

## How this was built

This project was built with AI assistance (Cline in VS Code) using a spec-driven workflow. See [`docs/ai-prompt.md`](docs/ai-prompt.md) and the [`specs/`](specs/001-best-stories-api/spec.md) folder for the spec and plan.
