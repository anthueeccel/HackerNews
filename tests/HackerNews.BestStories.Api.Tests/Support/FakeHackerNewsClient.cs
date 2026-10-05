using HackerNews.BestStories.Api.Clients;
using HackerNews.BestStories.Api.Models;

namespace HackerNews.BestStories.Api.Tests.Support;

public sealed class FakeHackerNewsClient(
    Func<IReadOnlyList<long>>? bestStoryIds = null,
    Func<long, HackerNewsItem?>? item = null) : IHackerNewsClient
{
    public int BestStoryIdsCalls { get; private set; }

    public int ItemCalls { get; private set; }

    public Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        BestStoryIdsCalls++;
        return Task.FromResult(bestStoryIds?.Invoke() ?? (IReadOnlyList<long>)[]);
    }

    public Task<HackerNewsItem?> GetItemAsync(long id, CancellationToken cancellationToken)
    {
        ItemCalls++;
        return Task.FromResult(item?.Invoke(id));
    }
}
