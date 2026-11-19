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

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/notes", "/notes/")]
    [InlineData("/notes/", "/notes/")]
    public async Task Upload_NestedFolder_KeepsEverySubfolderPath(string? destination, string prefix)
    {
        void Write(string relative, string text)
        {
            var full = Path.Combine(_folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        Write("top.md", "top");
        Write(Path.Combine("a", "readme.md"), "a");
        Write(Path.Combine("b", "readme.md"), "b");
        Write(Path.Combine("b", "deep", "er", "readme.md"), "deep");

        var sent = new List<(string Folder, string Name, string Text)>();
        _host.Respond(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/containers/by-name/docs")
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"id":"{{ContainerId}}","name":"docs"}""");
            if (request.Method == HttpMethod.Post && path == $"/api/containers/{ContainerId}/files")
            {
                var parts = ((MultipartFormDataContent)request.Content!).ToList();
                string Field(string name) => parts.Single(p => p.Headers.ContentDisposition!.Name == name)
                    .ReadAsStringAsync().GetAwaiter().GetResult();
                var file = parts.Single(p => p.Headers.ContentDisposition!.Name == "files");
                sent.Add((Field("path"), file.Headers.ContentDisposition!.FileName!.Trim('"'),
                    file.ReadAsStringAsync().GetAwaiter().GetResult()));
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
            }
            return MockHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"title":"Not found","status":404}""");
        });

        var args = destination is null
            ? new[] { "upload", _folder, "--container", "docs" }
            : new[] { "upload", _folder, "--container", "docs", "--destination", destination };
        var result = await _host.RunAsync(args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            new[]
            {
                (destination ?? "/", "top.md", "top"),
                (prefix + "a/", "readme.md", "a"),
                (prefix + "b/", "readme.md", "b"),
                (prefix + "b/deep/er/", "readme.md", "deep"),
            }.OrderBy(s => s.Item1),
            sent.OrderBy(s => s.Folder));
        Assert.Contains("b/deep/er/readme.md", result.Output);
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
