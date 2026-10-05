using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace HackerNews.BestStories.Api.Tests;

// True end-to-end test: no fake handler, so the app calls the real Hacker News API.
// Tagged "E2E" so the offline unit/integration run skips it; CI runs it separately.
[TestFixture]
[Category("E2E")]
public class BestStoriesE2ETests
{
    [Test]
    public async Task GetBestStories_RealUpstream_N1_ReturnsOneStoryWithAllFields()
    {
        var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var response = await client.GetAsync("/api/stories/best?n=1", timeoutCts.Token);

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        var stories = await response.Content.ReadFromJsonAsync<JsonElement>(timeoutCts.Token);

        Assert.That(stories.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(stories.GetArrayLength(), Is.EqualTo(1));

        var story = stories[0];
        Assert.Multiple(() =>
        {
            Assert.That(story.TryGetProperty("title", out var title), Is.True);
            Assert.That(title.GetString(), Is.Not.Empty);
            Assert.That(story.TryGetProperty("postedBy", out var postedBy), Is.True);
            Assert.That(postedBy.GetString(), Is.Not.Empty);
            Assert.That(story.TryGetProperty("uri", out _), Is.True);
            Assert.That(story.TryGetProperty("time", out var time), Is.True);
            Assert.That(time.GetDateTimeOffset(), Is.TypeOf(typeof(DateTimeOffset)));
            Assert.That(story.TryGetProperty("score", out var score), Is.True);
            Assert.That(score.GetInt32(), Is.GreaterThanOrEqualTo(0));
            Assert.That(story.TryGetProperty("commentCount", out var commentCount), Is.True);
            Assert.That(commentCount.GetInt32(), Is.GreaterThanOrEqualTo(0));
        });
    }
}
