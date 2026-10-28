using System.Net;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class SourceCommandTests : IDisposable
{
    private const string Id = "7b1c9e2a-0000-4000-8000-0000000000d1";
    private const string ConnectionId = "7b1c9e2a-0000-4000-8000-0000000000c1";
    private const string Source = $$"""
        {"id":"{{Id}}","name":"handbook","description":"HR docs","kind":"s3","summary":"s3://docs/hr",
        "connectionId":"{{ConnectionId}}","enabled":true,"documentCount":12,"failedDocumentCount":1,
        "processingDocumentCount":0,"syncIntervalSeconds":3600,"lastSyncStatus":"Succeeded",
        "lastSyncedAt":"2026-10-02T00:00:00Z","withheldDeletions":0}
        """;
    private const string Page = $$"""{"items":[{{Source}}],"totalCount":1,"hasMore":false}""";
    private const string Connections = $$"""
        {"items":[{"id":"{{ConnectionId}}","name":"docs-bucket","provider":"S3"}],"totalCount":1,"hasMore":false}
        """;

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task List_PrintsEachSourceAndSendsThePage()
    {
        _host.Routes(("GET /api/sources", CommandHost.Json(Page)));

        var result = await _host.RunAsync("source", "list", "--skip", "5", "--take", "10");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GET /api/sources?skip=5&take=10", _host.Sent.Single());
        Assert.Contains("handbook", result.Output);
        Assert.Contains("Succeeded", result.Output);
    }

    [Fact]
    public async Task Get_ByName_FindsItThenPrintsIt()
    {
        _host.Routes(
            ("GET /api/sources", CommandHost.Json(Page)),
            ($"GET /api/sources/{Id}", CommandHost.Json(Source)));

        var result = await _host.RunAsync("source", "get", "Handbook");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Scope:       s3://docs/hr", result.Output);
        Assert.Contains("Documents:   12 (0 processing, 1 failed)", result.Output);
    }

    [Fact]
    public async Task Get_AnUnknownName_SaysSo()
    {
        _host.Routes(("GET /api/sources", CommandHost.Json("""{"items":[],"totalCount":0,"hasMore":false}""")));

        var result = await _host.RunAsync("source", "get", "nope");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No source named 'nope'.", result.Error);
    }

    [Fact]
    public async Task Stats_PrintsTheCounts()
    {
        _host.Routes(($"GET /api/sources/{Id}/stats", CommandHost.Json($$"""
            {"sourceId":"{{Id}}","sourceName":"handbook","documents":{"total":12,"ready":11,"processing":0,"failed":1},
            "totalChunks":340,"totalSizeBytes":2048}
            """)));

        var result = await _host.RunAsync("source", "stats", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("12 (11 ready, 0 processing, 1 failed)", result.Output);
        Assert.Contains("Chunks:        340", result.Output);
    }

    [Fact]
    public async Task Create_ResolvesTheConnectionAndSendsTheTypedScope()
    {
        _host.Routes(
            ("GET /api/v1/connections", CommandHost.Json(Connections)),
            ("POST /api/sources", CommandHost.Json(Source, HttpStatusCode.Created)));

        var result = await _host.RunAsync("source", "create", "handbook", "--connection", "docs-bucket",
            "--scope", """{"type":"s3","bucketName":"docs","prefix":"hr/"}""", "--sync-interval", "3600");

        Assert.Equal(0, result.ExitCode);
        var body = _host.Bodies.Single();
        Assert.Contains($"\"connectionId\":\"{ConnectionId}\"", body);
        Assert.Contains("\"bucketName\":\"docs\"", body);
        Assert.Contains("\"type\":\"s3\"", body);
        Assert.Contains("\"syncIntervalSeconds\":3600", body);
        Assert.Contains($"Created source 'handbook' ({Id}).", result.Output);
    }

    [Fact]
    public async Task Create_AScopeWithoutAType_ListsTheTypes()
    {
        var result = await _host.RunAsync("source", "create", "handbook", "--connection", ConnectionId,
            "--scope", """{"bucketName":"docs"}""");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("needs a \"type\"", result.Error);
        Assert.Contains("s3", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Create_WithoutAScope_IsAValidationError()
    {
        var result = await _host.RunAsync("source", "create", "handbook", "--connection", ConnectionId);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--scope or --scope-file", result.Error);
    }

    [Fact]
    public async Task Create_Forbidden_NamesTheRoleAndScope()
    {
        _host.Routes(("POST /api/sources", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("source", "create", "handbook", "--connection", ConnectionId,
            "--scope", """{"type":"s3","bucketName":"docs"}""");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Owner or Admin role", result.Error);
        Assert.Contains("'admin:sources'", result.Error);
    }

    [Fact]
    public async Task Update_SendsOnlyTheFieldsGiven()
    {
        _host.Routes(($"PATCH /api/sources/{Id}", CommandHost.Json(Source)));

        var result = await _host.RunAsync("source", "update", Id, "--description", "new", "--disable");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("""{"description":"new","enabled":false}""", _host.Bodies.Single());
    }

    [Fact]
    public async Task Update_NothingGiven_IsAValidationError()
    {
        var result = await _host.RunAsync("source", "update", Id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Nothing to update", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Update_EnableAndDisable_IsAValidationError()
    {
        var result = await _host.RunAsync("source", "update", Id, "--enable", "--disable");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not both", result.Error);
    }

    [Fact]
    public async Task Delete_SendsTheNameAsConfirmation()
    {
        _host.Routes(
            ($"GET /api/sources/{Id}", CommandHost.Json(Source)),
            ($"DELETE /api/sources/{Id}", CommandHost.Status(HttpStatusCode.NoContent)));

        var result = await _host.RunAsync("source", "delete", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"DELETE /api/sources/{Id}?confirmName=handbook", _host.Sent[1]);
        Assert.Contains("Deleted source 'handbook'.", result.Output);
    }

    [Fact]
    public async Task Sync_PrintsTheCountsAndWarnsAboutWithheldDeletions()
    {
        _host.Routes(($"POST /api/sources/{Id}/sync", CommandHost.Json(
            """{"upserted":3,"deleted":1,"withheldDeletions":40,"usedDeltaPath":true,"requiredResync":false}""")));

        var result = await _host.RunAsync("source", "sync", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"POST /api/sources/{Id}/sync", _host.Sent.Single());
        Assert.Contains("3 added or changed, 1 deleted", result.Output);
        Assert.Contains("40 deletion(s) withheld", result.Output);
    }

    [Fact]
    public async Task Sync_ApplyWithheldDeletions_SendsTheFlag()
    {
        _host.Routes(($"POST /api/sources/{Id}/sync", CommandHost.Json("""{"upserted":0,"deleted":40}""")));

        var result = await _host.RunAsync("source", "sync", Id, "--apply-withheld-deletions");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"POST /api/sources/{Id}/sync?applyWithheldDeletions=true", _host.Sent.Single());
    }

    [Fact]
    public async Task Sync_AnError_ExitsOne()
    {
        _host.Routes(($"POST /api/sources/{Id}/sync", CommandHost.Json("""{"error":"bucket not found"}""")));

        var result = await _host.RunAsync("source", "sync", Id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sync failed: bucket not found", result.Output);
    }

    [Fact]
    public async Task RetryFailed_PrintsHowManyWereQueued()
    {
        _host.Routes(($"POST /api/sources/{Id}/retry-failed", CommandHost.Json("""{"enqueuedCount":4}""")));

        var result = await _host.RunAsync("source", "retry-failed", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Queued 4 failed document(s)", result.Output);
    }

    [Fact]
    public async Task ScopeSchema_PrintsEachKind()
    {
        _host.Routes(("GET /api/v1/sources/scope-schema/S3", CommandHost.Json(
            """{"provider":"S3","kinds":[{"type":"s3","schema":{"type":"object","required":["bucketName"]}}]}""")));

        var result = await _host.RunAsync("source", "scope-schema", "s3");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("type: s3", result.Output);
        Assert.Contains("\"bucketName\"", result.Output);
    }
}
