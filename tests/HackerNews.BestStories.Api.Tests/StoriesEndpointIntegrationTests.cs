using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.BestStories.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NUnit.Framework;

namespace HackerNews.BestStories.Api.Tests;

[TestFixture]
public class StoriesEndpointIntegrationTests
{
    private sealed class TestApiFactory(FakeHttpMessageHandler handler) : WebApplicationFactory<Program>
    {
        public FakeHttpMessageHandler Handler { get; } = handler;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                        handlerBuilder.PrimaryHandler = Handler));
            });
        }
    }

    private static FakeHttpMessageHandler CreateHandler(IReadOnlyList<long> ids)
    {
        return new FakeHttpMessageHandler
        {
            Responder = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("beststories.json", StringComparison.Ordinal))
                {
                    return FakeHttpMessageHandler.Json(ids);
                }
                var id = long.Parse(path.Split('/').Last().Replace(".json", string.Empty));
                return FakeHttpMessageHandler.Json(new
                {
                    id,
                    type = "story",
                    title = $"Story {id}",
                    url = $"https://example.com/{id}",
                    by = $"user{id}",
                    time = 1570890181 + (int)(id * 60),
                    score = (int)id * 10,
                    descendants = (int)id
                });
            }
        };
    }

    private static async Task<JsonElement[]> GetStoriesAsync(TestApiFactory factory, string query)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/stories/best{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(content.ValueKind, Is.EqualTo(JsonValueKind.Array));
        return content.EnumerateArray().ToArray();
    }

    // T-8 (FR-2, FR-4)
    [Test]
    public async Task GetBestStories_ValidN_Returns200WithCamelCaseJsonSortedByScore()
    {
        var factory = new TestApiFactory(CreateHandler([3, 1, 2]));
        var stories = await GetStoriesAsync(factory, "?n=2");

        Assert.That(stories, Has.Length.EqualTo(2));
        Assert.That(stories[0].GetProperty("score").GetInt32(), Is.EqualTo(30));
        // Per the spec's assumption: the first n IDs are taken, then sorted by score.
        Assert.That(stories[1].GetProperty("score").GetInt32(), Is.EqualTo(10));

        var first = stories[0];
        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("title").GetString(), Is.EqualTo("Story 3"));
            Assert.That(first.GetProperty("uri").GetString(), Is.EqualTo("https://example.com/3"));
            Assert.That(first.GetProperty("postedBy").GetString(), Is.EqualTo("user3"));
            Assert.That(first.GetProperty("time").GetString(), Is.EqualTo("2019-10-12T14:26:01+00:00"));
            Assert.That(first.GetProperty("commentCount").GetInt32(), Is.EqualTo(3));
        });
    }

    // T-9 (FR-1)
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("201")]
    public async Task GetBestStories_InvalidNumericN_Returns400ProblemDetails(string n)
    {
        var factory = new TestApiFactory(CreateHandler([1]));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/stories/best?n={n}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(problem.TryGetProperty("title", out _), Is.True);
            Assert.That(problem.TryGetProperty("errors", out _), Is.True);
        });
    }

    [Test]
    public async Task GetBestStories_MissingN_Returns400ProblemDetails()
    {
        var factory = new TestApiFactory(CreateHandler([1]));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/stories/best");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetBestStories_NonNumericN_Returns400()
    {
        var factory = new TestApiFactory(CreateHandler([1]));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/stories/best?n=abc");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // T-10 (EC-3)
    [Test]
    public async Task GetBestStories_UpstreamFails_Returns502ProblemDetails()
    {
        var handler = new FakeHttpMessageHandler
        {
            Responder = _ => throw new HttpRequestException("boom")
        };
        var factory = new TestApiFactory(handler);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/stories/best?n=1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(problem.TryGetProperty("stackTrace", out _), Is.False);
    }

    [Test]
    public async Task GetBestStories_UpstreamTimeout_Returns503ProblemDetails()
    {
        var handler = new FakeHttpMessageHandler();
        var factory = new TestApiFactory(handler);
        factory.Handler.Responder = _ =>
        {
            // The client-side timeout (configured below) cancels the request before this returns.
            Thread.Sleep(TimeSpan.FromSeconds(2));
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        // Speed the timeout up through configuration instead of waiting for the 5s default.
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
                (_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["HackerNews:TimeoutSeconds"] = "1" })))
            .CreateClient();

        using var response = await client.GetAsync("/api/stories/best?n=1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    // T-11 (OPS-2)
    [Test]
    public async Task Health_Returns200()
    {
        var factory = new TestApiFactory(CreateHandler([]));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
