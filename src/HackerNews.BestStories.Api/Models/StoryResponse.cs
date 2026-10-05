namespace HackerNews.BestStories.Api.Models;

// Public DTO; serialized as camelCase by the ASP.NET Core default JSON options.
public record StoryResponse(
    string Title,
    string? Uri,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount);
