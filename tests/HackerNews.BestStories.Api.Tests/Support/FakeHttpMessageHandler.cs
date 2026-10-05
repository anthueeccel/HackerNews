using System.Net;
using System.Text;
using System.Text.Json;

namespace HackerNews.BestStories.Api.Tests.Support;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(Responder(request));
    }

    public static HttpResponseMessage Json(object payload, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
}
