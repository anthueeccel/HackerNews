using HackerNews.BestStories.Api.Options;
using HackerNews.BestStories.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Endpoints;

public static class StoriesEndpoints
{
    public static IEndpointRouteBuilder MapStoriesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stories/best", GetBestStories)
            .WithName("GetBestStories")
            .WithSummary("Returns the best n Hacker News stories ordered by score descending.");

        return app;
    }

    private static async Task<IResult> GetBestStories(
        [FromQuery] int? n,
        IBestStoriesService service,
        IOptions<StoriesOptions> options,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var maxCount = options.Value.MaxCount;

        if (n is null || n.Value < 1 || n.Value > maxCount)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["n"] = [$"'n' is required and must be between 1 and {maxCount}."]
            });
        }

        try
        {
            var stories = await service.GetBestStoriesAsync(n.Value, cancellationToken);
            return Results.Ok(stories);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "The Hacker News API could not be reached.");
            return Problem("The upstream Hacker News API is unavailable.", StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            logger.LogWarning(ex, "The Hacker News API request timed out.");
            return Problem("The upstream Hacker News API timed out.", StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult Problem(string detail, int statusCode) =>
        Results.Problem(statusCode: statusCode, title: "Upstream failure", detail: detail);
}
