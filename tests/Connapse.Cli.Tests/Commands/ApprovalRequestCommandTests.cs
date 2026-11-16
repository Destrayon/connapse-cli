using System.Net;
using System.Text.Json;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

/// <summary><c>connapse approval request</c> asks the owner to approve a batch of deletions (CC-14).</summary>
[Collection(ConsoleCollection.Name)]
public sealed class ApprovalRequestCommandTests : IDisposable
{
    private const string BatchId = "0aae0f35-ddd6-457c-a031-aae9f8c11db6";
    private const string FactId = "5d0a3c1e-0000-4000-8000-0000000000aa";
    private const string Batch = $$"""
        {"id":"{{BatchId}}","state":"pending","createdAt":"2026-11-15T09:00:00Z","expiresAt":"2026-11-16T09:00:00Z",
        "decidedAt":null,"reason":null,"message":"Waiting for the owner.","isYours":true,"tokenKey":"k",
        "items":[
          {"id":"00000000-0000-4000-8000-000000000002","position":1,"kind":"folder","containerId":"00000000-0000-4000-8000-0000000000c1",
           "target":"/old","label":"notes:/old","reason":"stale","state":"pending","outcome":null},
          {"id":"00000000-0000-4000-8000-000000000001","position":0,"kind":"file","containerId":"00000000-0000-4000-8000-0000000000c1",
           "target":"/a.md","label":"notes:/a.md","reason":"stale","state":"pending","outcome":null}]}
        """;

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Request_SendsEveryItemWithTheReasonAndExits3()
    {
        _host.Routes(("POST /api/approvals/deletions", CommandHost.Json(Batch)));

        var result = await _host.RunAsync("approval", "request", "--reason", "stale", "--container", "notes",
            "--file", "/a.md", "--folder", "/old", "--empty-container", "scratch", "--space", "old-notes", "--fact", FactId);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("POST /api/approvals/deletions", _host.Sent.Single());
        var items = JsonDocument.Parse(_host.Bodies.Single()).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => (Kind: i.GetProperty("kind").GetString(), Container: i.GetProperty("container").GetString(),
                Target: i.TryGetProperty("target", out var t) ? t.GetString() : null, Reason: i.GetProperty("reason").GetString()))
            .ToList();
        Assert.Equal(
            [("file", "notes", "/a.md", "stale"), ("folder", "notes", "/old", "stale"), ("container", "scratch", null, "stale"),
             ("memory_space", "old-notes", null, "stale"), ("fact", "", FactId, "stale")],
            items);

        Assert.Contains($"Asked for approval (deletion batch {BatchId}).", result.Output);
        int file = result.Output.IndexOf("file notes:/a.md", StringComparison.Ordinal);
        int folder = result.Output.IndexOf("folder notes:/old", StringComparison.Ordinal);
        Assert.True(file >= 0 && folder > file, result.Output);
        Assert.Contains("https://server.example/approvals", result.Output);
        Assert.Contains($"connapse approval status {BatchId} --wait 50", result.Output);
    }

    [Fact]
    public async Task Request_Json_PrintsTheBatch()
    {
        _host.Routes(("POST /api/approvals/deletions", CommandHost.Json(Batch)));

        var result = await _host.RunAsync("approval", "request", "--reason", "stale", "--space", "old-notes", "--json");

        Assert.Equal(3, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(BatchId, doc.RootElement.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("--reason", "x")]
    [InlineData("--reason", "x", "--file", "/a.md")]
    [InlineData("--reason", " ", "--space", "s")]
    [InlineData("--reason", "x", "--fact", "not-an-id")]
    public async Task ABadRequest_IsRefusedBeforeAnyRequest(params string[] args)
    {
        var result = await _host.RunAsync(["approval", "request", .. args]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task MoreThanFiftyItems_IsRefused()
    {
        string[] files = Enumerable.Range(0, 51).SelectMany(i => new[] { "--file", $"/f{i}.md" }).ToArray();

        var result = await _host.RunAsync(["approval", "request", "--reason", "x", "--container", "notes", .. files]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("At most 50", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task AnItemTheServerRefuses_ShowsItsDetail()
    {
        _host.Routes(("POST /api/approvals/deletions", CommandHost.Json(
            """{"title":"Bad Request","status":400,"detail":"container 'scratch' still holds 2 files; delete them first"}""",
            HttpStatusCode.BadRequest)));

        var result = await _host.RunAsync("approval", "request", "--reason", "x", "--empty-container", "scratch");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("still holds 2 files", result.Error);
    }

    [Fact]
    public async Task Forbidden_NamesTheScope()
    {
        _host.Routes(("POST /api/approvals/deletions", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("approval", "request", "--reason", "x", "--space", "s");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("knowledge:write", result.Error);
    }
}
