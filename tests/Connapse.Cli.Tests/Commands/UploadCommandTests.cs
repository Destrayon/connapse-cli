using System.Net;
using System.Net.Http.Headers;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class UploadCommandTests : IDisposable
{
    private const string ContainerId = "7b1c9e2a-0000-4000-8000-0000000000a1";

    private readonly CommandHost _host = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "connapse-cli-upload-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private void MakeFolder(int files)
    {
        for (var i = 0; i < files; i++)
        {
            var dir = Path.Combine(_folder, $"part-{i % 7}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"file-{i:D3}.md"), $"# file {i}");
        }
    }

    /// <summary>
    /// The server's default limit: a fixed window of 200 requests a minute per user, every request
    /// counted, and a 429 naming the whole seconds left in the window (rounded down, as the server does).
    /// Time moves on a little with each request, and by however long the CLI waits.
    /// </summary>
    private List<string> ServeWithRateLimit(int permitsPerMinute = 200)
    {
        var uploaded = new List<string>();
        var now = TimeSpan.Zero;
        var windowStart = TimeSpan.Zero;
        var window = TimeSpan.FromMinutes(1);
        var count = 0;
        _host.OnWait = wait => now += wait;
        _host.Respond(request =>
        {
            now += TimeSpan.FromMilliseconds(50);
            if (now >= windowStart + window)
            {
                windowStart = TimeSpan.FromTicks(now.Ticks / window.Ticks * window.Ticks);
                count = 0;
            }
            if (count >= permitsPerMinute)
            {
                var limited = MockHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, """{"error":"Rate limit exceeded. Try again later."}""");
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds((int)(windowStart + window - now).TotalSeconds));
                return limited;
            }
            count++;

            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/containers/by-name/docs")
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"id":"{{ContainerId}}","name":"docs"}""");
            if (request.Method == HttpMethod.Post && path == $"/api/containers/{ContainerId}/files")
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                uploaded.Add(body[(body.IndexOf("file-", StringComparison.Ordinal))..][..12]);
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
            }
            return MockHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"title":"Not found","status":404}""");
        });
        return uploaded;
    }

    [Fact]
    public async Task Upload_FolderOf311Files_FinishesUnderTheDefaultRateLimit()
    {
        MakeFolder(311);
        var uploaded = ServeWithRateLimit();

        var result = await _host.RunAsync("upload", _folder, "--container", "docs");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Summary: 311 successful, 0 failed", result.Output);
        Assert.Equal(311, uploaded.Distinct().Count());
        // The 200 permits ran out once, partway through; the CLI waited out the rest of that minute.
        var wait = Assert.Single(_host.Waits);
        Assert.InRange(wait, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(61));
        Assert.Contains("rate limiting", result.Error);
    }

    [Fact]
    public async Task Upload_FolderOf650Files_WaitsOnceAMinute()
    {
        MakeFolder(650);
        var uploaded = ServeWithRateLimit();

        var result = await _host.RunAsync("upload", _folder, "--container", "docs");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(650, uploaded.Distinct().Count());
        Assert.Equal(3, _host.Waits.Count);
    }
}
