using System.ComponentModel.DataAnnotations;

namespace HackerNews.BestStories.Api.Options;

public record HackerNewsOptions
{
    public const string SectionName = "HackerNews";

    [Required]
    public string BaseAddress { get; init; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 5;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}
