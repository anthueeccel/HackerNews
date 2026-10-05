# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HackerNews.slnx ./
COPY src ./src
COPY tests ./tests
RUN dotnet restore
RUN dotnet publish src/HackerNews.BestStories.Api -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN useradd --uid 1001 appuser && chown -R appuser /app
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "HackerNews.BestStories.Api.dll"]
