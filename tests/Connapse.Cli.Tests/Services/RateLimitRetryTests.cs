using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Services;

public sealed class RateLimitRetryTests : IDisposable
{
    private readonly List<TimeSpan> _waits = new();
    private readonly StringWriter _notices = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private RateLimitRetry Retry(TimeProvider? time = null) => new(
        delay: (wait, _) =>
        {
            _waits.Add(wait);
            return Task.CompletedTask;
        },
        time: time,
        random: new Random(7),
        notices: _notices);

    private static HttpResponseMessage TooMany(TimeSpan? retryAfter = null, DateTimeOffset? at = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is { } delta)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        else if (at is { } date)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(date);
        return response;
    }

    /// <summary>Answers with <paramref name="responses"/> in turn, counting the sends.</summary>
    private static Func<CancellationToken, Task<HttpResponseMessage>> Server(params Func<HttpResponseMessage>[] responses)
    {
        var sent = 0;
        return _ => Task.FromResult(responses[Math.Min(sent++, responses.Length - 1)]());
    }

    [Fact]
    public async Task TooManyRequests_WaitsTheSecondsTheServerNames_ThenSendsAgain()
    {
        var response = await Retry().SendAsync(
            Server(() => TooMany(TimeSpan.FromSeconds(42)), () => new HttpResponseMessage(HttpStatusCode.OK)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var wait = Assert.Single(_waits);
        // Retry-After, a second for the server rounding down, and under a second of jitter.
        Assert.InRange(wait, TimeSpan.FromSeconds(43), TimeSpan.FromSeconds(44));
        Assert.Contains("rate limiting", _notices.ToString());
        Assert.Contains("retry 1 of 5", _notices.ToString());
    }

    [Fact]
    public async Task RetryAfterAsADate_WaitsUntilThen()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

        await Retry(time).SendAsync(
            Server(() => TooMany(at: time.GetUtcNow().AddSeconds(30)), () => new HttpResponseMessage(HttpStatusCode.OK)),
            CancellationToken.None);

        Assert.InRange(Assert.Single(_waits), TimeSpan.FromSeconds(31), TimeSpan.FromSeconds(32));
    }

    [Fact]
    public async Task NoRetryAfter_BacksOffExponentiallyWithJitter()
    {
        var response = await Retry().SendAsync(Server(() => TooMany()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(RateLimitRetry.MaxRetries, _waits.Count);
        // Each wait is between half and all of 2s, 4s, 8s, 16s, 32s.
        for (var i = 0; i < _waits.Count; i++)
        {
            var step = TimeSpan.FromSeconds(2 * Math.Pow(2, i));
            Assert.InRange(_waits[i], step / 2, step);
        }
    }

    [Fact]
    public void NoRetryAfter_BackoffStopsGrowingAtAMinute()
    {
        var wait = Retry().WaitBefore(10, TooMany());

        Assert.InRange(wait!.Value, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task StillTooBusyAfterFiveRetries_ReturnsThe429()
    {
        var sends = 0;
        var response = await Retry().SendAsync(_ =>
        {
            sends++;
            return Task.FromResult(TooMany(TimeSpan.FromSeconds(1)));
        }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1 + RateLimitRetry.MaxRetries, sends);
    }

    [Fact]
    public async Task RetryAfterLongerThanTwoMinutes_ReturnsThe429WithoutWaiting()
    {
        var response = await Retry().SendAsync(Server(() => TooMany(TimeSpan.FromMinutes(10))), CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task ServiceUnavailableWithRetryAfter_IsRetried()
    {
        var unavailable = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        };

        var result = await Retry().SendAsync(Server(unavailable, () => new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Single(_waits);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task OtherFailures_AreNotRetried(HttpStatusCode status)
    {
        var response = await Retry().SendAsync(Server(() => new HttpResponseMessage(status)), CancellationToken.None);

        Assert.Equal(status, response.StatusCode);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task ApiClient_ResendsAFileUploadWhole_AfterA429()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "notes.txt");
        await File.WriteAllTextAsync(file, "the file's own words");

        var bodies = new List<string>();
        using var network = new SerializingHandler(async request =>
        {
            // Serialized as the socket handler does, not buffered: a stream that wasn't rewound sends nothing.
            using var buffer = new MemoryStream();
            await request.Content!.CopyToAsync(buffer);
            bodies.Add(Encoding.UTF8.GetString(buffer.ToArray()));
            return bodies.Count == 1 ? TooMany(TimeSpan.FromSeconds(3)) : new HttpResponseMessage(HttpStatusCode.OK);
        });
        var credStore = new CredentialStore(_tempDir);
        credStore.Save(new CliCredentials("access-abc", "https://server.example", "alice@example.com", null, DateTime.UtcNow.AddHours(1)));
        var api = new ApiClient(
            new MockHttpClientFactory(network, new Uri("https://server.example")),
            credStore,
            new OrgContextProvider(),
            new FakeEditionProvider(ServerEdition.SelfHosted),
            rateLimitRetry: Retry());

        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(file);
        content.Add(new StreamContent(stream), "files", "notes.txt");
        using var response = await api.SendRawAsync(HttpMethod.Post, "/api/containers/c1/files", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("the file's own words", bodies[1]);
        Assert.Equal(bodies[0], bodies[1]);
    }

    private sealed class SerializingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class FakeEditionProvider(ServerEdition edition) : IEditionProvider
    {
        public Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(edition);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
