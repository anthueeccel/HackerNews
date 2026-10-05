using HackerNews.BestStories.Api.Clients;
using HackerNews.BestStories.Api.Endpoints;
using HackerNews.BestStories.Api.Infrastructure;
using HackerNews.BestStories.Api.Options;
using HackerNews.BestStories.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services
    .AddOptions<HackerNewsOptions>()
    .BindConfiguration(HackerNewsOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<StoriesOptions>()
    .BindConfiguration(StoriesOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IHackerNewsClient, HackerNewsClient>((services, httpClient) =>
{
    var settings = services.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
    httpClient.BaseAddress = new Uri(settings.BaseAddress);
    httpClient.Timeout = settings.Timeout;
});

builder.Services.AddSingleton<SingleFlightCache>();
builder.Services.AddScoped<IBestStoriesService, BestStoriesService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapStoriesEndpoints();

app.Run();

