using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.BestStories.Api.Models;

namespace HackerNews.BestStories.Api.Clients;

public sealed class HackerNewsClient(HttpClient httpClient) : IHackerNewsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<long>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await httpClient.GetFromJsonAsync<long[]>("beststories.json", JsonOptions, cancellationToken);
        return ids ?? [];
    }

    public async Task<HackerNewsItem?> GetItemAsync(long id, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<HackerNewsItem?>($"item/{id}.json", JsonOptions, cancellationToken);
}
