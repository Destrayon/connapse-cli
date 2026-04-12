using System.Net;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Services;

public sealed class EditionDetectorTests : IDisposable
{
    private static readonly Uri BaseUri = new("https://server.example");

    private readonly string _tempDir;
    private readonly ConfigStore _config;
    private readonly MockHttpMessageHandler _handler;
    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly FakeClock _clock;

    public EditionDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid());
        _config = new ConfigStore(_tempDir);
        _handler = new MockHttpMessageHandler();
        _httpClientFactory = new MockHttpClientFactory(_handler, BaseUri);
        _clock = new FakeClock(new DateTime(2026, 4, 12, 12, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private EditionDetector CreateDetector() => new(_httpClientFactory, _config, _clock);

    [Fact]
    public async Task Fetches_edition_from_api_v1_info_on_first_call()
    {
        _handler.Responder = _ => MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"edition":"cloud","version":"1.0.0","features":["organizations"]}""");

        var edition = await CreateDetector().GetEditionAsync();

        Assert.Equal(ServerEdition.Cloud, edition);
        var req = Assert.Single(_handler.Requests);
        Assert.Equal("/api/v1/info", req.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Caches_result_in_config()
    {
        _handler.Responder = _ => MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"edition":"cloud","version":"1.0.0"}""");

        await CreateDetector().GetEditionAsync();

        var saved = _config.Load();
        Assert.Equal(ServerEdition.Cloud, saved.Edition);
        Assert.Equal(_clock.UtcNow, saved.EditionCheckedAt);
    }

    [Fact]
    public async Task Uses_cached_result_when_fresh()
    {
        _config.Save(new CliConfig
        {
            Edition = ServerEdition.Cloud,
            EditionCheckedAt = _clock.UtcNow.AddDays(-2), // 2 days old, still fresh
        });

        var edition = await CreateDetector().GetEditionAsync();

        Assert.Equal(ServerEdition.Cloud, edition);
        Assert.Empty(_handler.Requests); // no HTTP call
    }

    [Fact]
    public async Task Re_fetches_when_cache_is_stale()
    {
        _config.Save(new CliConfig
        {
            Edition = ServerEdition.SelfHosted,
            EditionCheckedAt = _clock.UtcNow.AddDays(-8), // 8 days old, stale
        });
        _handler.Responder = _ => MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"edition":"cloud","version":"1.0.0"}""");

        var edition = await CreateDetector().GetEditionAsync();

        Assert.Equal(ServerEdition.Cloud, edition);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Treats_404_as_self_hosted()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var edition = await CreateDetector().GetEditionAsync();

        Assert.Equal(ServerEdition.SelfHosted, edition);
        var saved = _config.Load();
        Assert.Equal(ServerEdition.SelfHosted, saved.Edition);
    }

    [Fact]
    public async Task Network_error_does_not_cache_and_returns_unknown()
    {
        _handler.Responder = _ => throw new HttpRequestException("boom");

        var edition = await CreateDetector().GetEditionAsync();

        Assert.Equal(ServerEdition.Unknown, edition);
        Assert.Null(_config.Load().Edition);
    }

    [Fact]
    public async Task ForceRefresh_ignores_cache()
    {
        _config.Save(new CliConfig
        {
            Edition = ServerEdition.Cloud,
            EditionCheckedAt = _clock.UtcNow.AddMinutes(-1), // very fresh
        });
        _handler.Responder = _ => MockHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"edition":"self-hosted","version":"1.0.0"}""");

        var edition = await CreateDetector().GetEditionAsync(forceRefresh: true);

        Assert.Equal(ServerEdition.SelfHosted, edition);
        Assert.Single(_handler.Requests);
    }

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }
}
