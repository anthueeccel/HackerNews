using HackerNews.BestStories.Api.Clients;
using HackerNews.BestStories.Api.Infrastructure;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Api.Options;
using HackerNews.BestStories.Api.Services;
using HackerNews.BestStories.Api.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace HackerNews.BestStories.Api.Tests;

[TestFixture]
public class BestStoriesServiceTests
{
    private static readonly HackerNewsItem FullStory = new(
        Id: 1,
        Deleted: null,
        Dead: null,
        Type: "story",
        Title: "A uBlock Origin update was rejected from the Chrome Web Store",
        Url: "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
        By: "ismaildonmez",
        Time: 1570890181,
        Score: 1716,
        Descendants: 572);

    private static BestStoriesService CreateService(IHackerNewsClient client) =>
        new(
            client,
            new SingleFlightCache(),
            Microsoft.Extensions.Options.Options.Create(new StoriesOptions()),
            NullLogger<BestStoriesService>.Instance);

    private static readonly Func<long, HackerNewsItem?> DefaultItem = _ => FullStory with { Id = _ };

    // T-1 (FR-4)
    [Test]
    public async Task GetBestStoriesAsync_ValidItem_MapsToPublicDto()
    {
        var client = new FakeHackerNewsClient(() => [1], _ => FullStory);
        var service = CreateService(client);

        var stories = await service.GetBestStoriesAsync(1, CancellationToken.None);

        Assert.That(stories, Has.Count.EqualTo(1));
        var story = stories[0];
        Assert.Multiple(() =>
        {
            Assert.That(story.Title, Is.EqualTo(FullStory.Title));
            Assert.That(story.Uri, Is.EqualTo(FullStory.Url));
            Assert.That(story.PostedBy, Is.EqualTo("ismaildonmez"));
            Assert.That(story.Time, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1570890181)));
            Assert.That(story.Score, Is.EqualTo(1716));
            Assert.That(story.CommentCount, Is.EqualTo(572));
        });
    }

    // T-2 (FR-2)
    [Test]
    public async Task GetBestStoriesAsync_MultipleStories_SortsByScoreDescending()
    {
        var client = new FakeHackerNewsClient(
            () => [1, 2, 3],
            id => FullStory with { Id = id, Score = (int)(id * 10) });
        var service = CreateService(client);

        var stories = await service.GetBestStoriesAsync(3, CancellationToken.None);

        Assert.That(stories.Select(s => s.Score), Is.Ordered.Descending);
        Assert.That(stories[0].Score, Is.EqualTo(30));
    }

    // T-3 (EC-1)
    [Test]
    public async Task GetBestStoriesAsync_InvalidItems_SkipsThem()
    {
        HackerNewsItem?[] items =
        [
            FullStory with { Id = 1 },
            null,
            FullStory with { Id = 2, Deleted = true },
            FullStory with { Id = 3, Dead = true },
            FullStory with { Id = 4, Type = "comment" }
        ];
        var client = new FakeHackerNewsClient(() => [1, 2, 3, 4, 5], id => items[id - 1]);
        var service = CreateService(client);

        var stories = await service.GetBestStoriesAsync(5, CancellationToken.None);

        Assert.That(stories, Has.Count.EqualTo(1));
        Assert.That(stories[0].Title, Is.EqualTo(FullStory.Title));
    }

    // T-4 (EC-2)
    [Test]
    public async Task GetBestStoriesAsync_ItemWithoutUrl_ReturnsNullUri()
    {
        var client = new FakeHackerNewsClient(
            () => [1],
            _ => FullStory with { Url = null });
        var service = CreateService(client);

        var stories = await service.GetBestStoriesAsync(1, CancellationToken.None);

        Assert.That(stories[0].Uri, Is.Null);
    }

    // T-5 (FR-8)
    [Test]
    public async Task GetBestStoriesAsync_ConcurrentRequests_CallsUpstreamOnce()
    {
        var client = new FakeHackerNewsClient(
            () => [1],
            DefaultItem);
        var service = CreateService(client);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => service.GetBestStoriesAsync(1, CancellationToken.None));
        await Task.WhenAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(client.BestStoryIdsCalls, Is.EqualTo(1));
            Assert.That(client.ItemCalls, Is.EqualTo(1));
        });
    }

    // T-6 (FR-9)
    [Test]
    public async Task GetBestStoriesAsync_UpstreamFailure_NotCachedAndRetried()
    {
        var calls = 0;
        var client = new FakeHackerNewsClient(
            () => { calls++; return calls == 1 ? throw new HttpRequestException() : (IReadOnlyList<long>)[1]; },
            DefaultItem);
        var service = CreateService(client);

        Assert.That(
            async () => await service.GetBestStoriesAsync(1, CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
        var stories = await service.GetBestStoriesAsync(1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(stories, Has.Count.EqualTo(1));
        });
    }

    // T-7 (EC-4)
    [Test]
    public async Task GetBestStoriesAsync_FewerValidStoriesThanN_ReturnsAvailable()
    {
        var client = new FakeHackerNewsClient(() => [1, 2], DefaultItem);
        var service = CreateService(client);

        var stories = await service.GetBestStoriesAsync(10, CancellationToken.None);

        Assert.That(stories, Has.Count.EqualTo(2));
    }
}

