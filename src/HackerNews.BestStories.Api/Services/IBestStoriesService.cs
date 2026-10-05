namespace HackerNews.BestStories.Api.Services;

public interface IBestStoriesService
{
    Task<IReadOnlyList<Models.StoryResponse>> GetBestStoriesAsync(int count, CancellationToken cancellationToken);
}
