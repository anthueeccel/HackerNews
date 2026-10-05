using HackerNews.BestStories.Api.Clients;
using HackerNews.BestStories.Api.Infrastructure;
using HackerNews.BestStories.Api.Models;
using HackerNews.BestStories.Api.Options;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Services;

public sealed class BestStoriesService(
    IHackerNewsClient client,
    SingleFlightCache cache,
    IOptions<StoriesOptions> options,
    ILogger<BestStoriesService> logger) : IBestStoriesService
{
    private const string IdListCacheKey = "beststories";

    public async Task<IReadOnlyList<StoryResponse>> GetBestStoriesAsync(int count, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var ids = await GetBestStoryIdsAsync(settings, cancellationToken);

        var selected = ids.Take(count).ToArray();
        var results = new StoryResponse?[selected.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, selected.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.Parallelism,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var story = await GetStoryAsync(selected[index], settings, token);
                results[index] = story;
            });

        return results
            .Where(story => story is not null)
            .Select(story => story!)
            .OrderByDescending(story => story.Score)
            .ToList();
    }

    private async Task<IReadOnlyList<long>> GetBestStoryIdsAsync(StoriesOptions settings, CancellationToken ct)
    {
        var (ids, wasHit) = await cache.GetOrAddAsync(
            IdListCacheKey,
            settings.IdListTtl,
            client.GetBestStoryIdsAsync,
            ct);

        logger.LogDebug("Best story ID list cache {CacheResult}.", wasHit ? "hit" : "miss");
        return ids;
    }

    private async Task<StoryResponse?> GetStoryAsync(long id, StoriesOptions settings, CancellationToken ct)
    {
        var (item, wasHit) = await cache.GetOrAddAsync(
            $"item:{id}",
            settings.ItemTtl,
            token => client.GetItemAsync(id, token),
            ct);

        logger.LogDebug("Item {ItemId} cache {CacheResult}.", id, wasHit ? "hit" : "miss");

        // EC-1: skip items that are null, deleted, dead, or not of type story.
        if (item is null || item.Deleted == true || item.Dead == true || item.Type is not "story")
        {
            return null;
        }

        return MapToResponse(item);
    }

    private static StoryResponse MapToResponse(HackerNewsItem item) =>
        new(
            Title: item.Title ?? string.Empty,
            Uri: item.Url,
            PostedBy: item.By ?? string.Empty,
            Time: item.Time is { } unixSeconds
                ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                : DateTimeOffset.MinValue,
            Score: item.Score ?? 0,
            CommentCount: item.Descendants ?? 0);
}
