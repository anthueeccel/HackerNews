using System.ComponentModel.DataAnnotations;

namespace HackerNews.BestStories.Api.Options;

public record StoriesOptions
{
    public const string SectionName = "Stories";

    [Range(1, int.MaxValue)]
    public int MaxCount { get; init; } = 200;

    [Range(1, 100)]
    public int Parallelism { get; init; } = 10;

    [Range(1, int.MaxValue)]
    public int IdListTtlSeconds { get; init; } = 120;

    [Range(1, int.MaxValue)]
    public int ItemTtlSeconds { get; init; } = 600;

    [Range(1, int.MaxValue)]
    public int MaxCacheEntries { get; init; } = 1000;

    public TimeSpan IdListTtl => TimeSpan.FromSeconds(IdListTtlSeconds);

    public TimeSpan ItemTtl => TimeSpan.FromSeconds(ItemTtlSeconds);
}
