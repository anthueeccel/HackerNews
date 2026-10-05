using HackerNews.BestStories.Api.Models;

namespace HackerNews.BestStories.Api.Clients;

public interface IHackerNewsClient
{
    Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken);

    Task<HackerNewsItem?> GetItemAsync(long id, CancellationToken cancellationToken);
}
