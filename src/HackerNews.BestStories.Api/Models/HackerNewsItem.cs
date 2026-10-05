using System.Text.Json.Serialization;

namespace HackerNews.BestStories.Api.Models;

// Shape of the Hacker News item endpoint; unused fields are ignored during deserialization.
public record HackerNewsItem(
    long Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Deleted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Dead,
    string? Type,
    string? Title,
    string? Url,
    string? By,
    long? Time,
    int? Score,
    int? Descendants);
