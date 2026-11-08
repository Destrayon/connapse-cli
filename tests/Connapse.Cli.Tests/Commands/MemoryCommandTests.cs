using System.Net;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class MemoryCommandTests : IDisposable
{
    private const string FactId = "5d0c2a10-0000-4000-8000-0000000000f1";
    private const string SpaceId = "5d0c2a10-0000-4000-8000-0000000000a1";
    private const string DocId = "5d0c2a10-0000-4000-8000-0000000000d1";
    private const string Fact = $$"""
        {"id":"{{FactId}}","chainId":"{{FactId}}","containerId":"{{SpaceId}}",
        "subject":"billing-service","attribute":"owner","value":"team-payments",
        "validFrom":null,"validTo":null,"recordedAt":"2026-11-01T09:00:00Z","supersededAt":null,
        "replacesId":null,"reason":"from the on-call doc","author":{"userId":null,"agentId":null,"name":"alice"},
        "sources":[{"kind":"Document","ref":"{{DocId}}"}]}
        """;
    private const string Space = $$"""{"id":"{{SpaceId}}","name":"my-memory","audience":"Personal"}""";
    private const string TeamSpace = $$"""{"id":"{{SpaceId}}","name":"team notes","audience":"Team"}""";

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Save_PostsTheFactToMyMemoryByDefault()
    {
        _host.Routes(("POST /api/memory/spaces/my-memory/facts", CommandHost.Json(
            $$"""{"fact":{{Fact}},"savedTo":{{Space}}}""", HttpStatusCode.Created)));

        var result = await _host.RunAsync("memory", "save", "billing-service", "owner", "team-payments",
            "--reason", "from the on-call doc", "--source", DocId, "--valid-from", "2026-11-01");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("POST /api/memory/spaces/my-memory/facts", _host.Sent.Single());
        var body = _host.Bodies.Single();
        Assert.Contains("\"subject\":\"billing-service\"", body);
        Assert.Contains("\"attribute\":\"owner\"", body);
        Assert.Contains("\"value\":\"team-payments\"", body);
        Assert.Contains("\"validFrom\":\"2026-11-01\"", body);
        Assert.Contains($"\"sources\":[\"{DocId}\"]", body);
        Assert.Contains("\"reason\":\"from the on-call doc\"", body);
        Assert.Contains("Saved to my-memory:", result.Output);
        Assert.Contains("billing-service / owner = team-payments", result.Output);
        Assert.Contains(FactId, result.Output);
    }

    [Fact]
    public async Task Save_ToANamedSpace_EscapesTheNameInThePath()
    {
        _host.Routes(("POST /api/memory/spaces/team%20notes/facts", CommandHost.Json(
            $$"""{"fact":{{Fact}},"savedTo":{{TeamSpace}}}""",
            HttpStatusCode.Created)));

        var result = await _host.RunAsync("memory", "save", "billing-service", "owner", "team-payments",
            "--space", "team notes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Saved to team notes:", result.Output);
        Assert.DoesNotContain("sources", _host.Bodies.Single());
    }

    [Fact]
    public async Task Save_Json_PrintsTheServersResponse()
    {
        _host.Routes(("POST /api/memory/spaces/my-memory/facts", CommandHost.Json(
            $$"""{"fact":{{Fact}},"savedTo":{{Space}}}""", HttpStatusCode.Created)));

        var result = await _host.RunAsync("memory", "save", "billing-service", "owner", "team-payments", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal(FactId, doc.RootElement.GetProperty("fact").GetProperty("id").GetString());
        Assert.Equal("my-memory", doc.RootElement.GetProperty("savedTo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Save_Forbidden_NamesTheRoleAndScope()
    {
        _host.Routes(("POST /api/memory/spaces/my-memory/facts", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("memory", "save", "a", "b", "c");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Editor role", result.Error);
        Assert.Contains("knowledge:write", result.Error);
    }

    [Fact]
    public async Task Save_ABadDate_ShowsTheServersProblem()
    {
        _host.Routes(("POST /api/memory/spaces/my-memory/facts", CommandHost.Json(
            """{"title":"Bad Request","status":400,"detail":"validFrom must be an ISO 8601 date or time."}""",
            HttpStatusCode.BadRequest)));

        var result = await _host.RunAsync("memory", "save", "a", "b", "c", "--valid-from", "someday");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("validFrom must be an ISO 8601 date or time.", result.Error);
    }

    [Fact]
    public async Task Recall_SendsTheFiltersAndNamesEachFactsSpace()
    {
        _host.Routes(("GET /api/memory/facts", CommandHost.Json($$"""
            {"facts":[{"fact":{{Fact}},"sources":[{"kind":"Document","ref":"{{DocId}}","title":"oncall.md"}]}],
            "spaces":[{{Space}}],"skippedSpaces":0,"nextCursor":null}
            """)));

        var result = await _host.RunAsync("memory", "recall", "payments", "--space", "my-memory",
            "--subject", "billing-service", "--attribute", "owner", "--as-of", "2026-11-02", "--history", "--limit", "5");

        Assert.Equal(0, result.ExitCode);
        var sent = _host.Sent.Single();
        Assert.StartsWith("GET /api/memory/facts?", sent);
        Assert.Contains("q=payments", sent);
        Assert.Contains("space=my-memory", sent);
        Assert.Contains("subject=billing-service", sent);
        Assert.Contains("attribute=owner", sent);
        Assert.Contains("asOf=2026-11-02", sent);
        Assert.Contains("includeHistory=true", sent);
        Assert.Contains("limit=5", sent);
        Assert.Contains("billing-service / owner = team-payments", result.Output);
        Assert.Contains("my-memory", result.Output);
        Assert.Contains($"Sources: oncall.md ({DocId})", result.Output);
    }

    [Fact]
    public async Task Recall_WithNoArguments_ReadsEverySpace()
    {
        _host.Routes(("GET /api/memory/facts", CommandHost.Json(
            """{"facts":[],"spaces":[],"skippedSpaces":3,"nextCursor":null}""")));

        var result = await _host.RunAsync("memory", "recall");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GET /api/memory/facts", _host.Sent.Single());
        Assert.Contains("No facts found.", result.Output);
        Assert.Contains("3 space(s) were not searched", result.Output);
    }

    [Fact]
    public async Task Recall_Json_PrintsTheServersResponse()
    {
        _host.Routes(("GET /api/memory/facts", CommandHost.Json($$"""
            {"facts":[{"fact":{{Fact}},"sources":[]}],"spaces":[{{Space}}],"skippedSpaces":0,"nextCursor":"abc"}
            """)));

        var result = await _host.RunAsync("memory", "recall", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal("team-payments",
            doc.RootElement.GetProperty("facts")[0].GetProperty("fact").GetProperty("value").GetString());
        Assert.Equal("abc", doc.RootElement.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task List_ReadsMyMemoryByDefaultAndPrintsTheNextCursor()
    {
        _host.Routes(("GET /api/memory/spaces/my-memory/facts", CommandHost.Json($$"""
            {"facts":[{"fact":{{Fact}},"sources":[]}],"nextCursor":"next-page"}
            """)));

        var result = await _host.RunAsync("memory", "list", "--subject", "billing-service", "--status", "all",
            "--sort", "subject", "--limit", "20", "--cursor", "this-page");

        Assert.Equal(0, result.ExitCode);
        var sent = _host.Sent.Single();
        Assert.StartsWith("GET /api/memory/spaces/my-memory/facts?", sent);
        Assert.Contains("subject=billing-service", sent);
        Assert.Contains("status=all", sent);
        Assert.Contains("sort=subject", sent);
        Assert.Contains("limit=20", sent);
        Assert.Contains("cursor=this-page", sent);
        Assert.Contains("billing-service / owner = team-payments", result.Output);
        Assert.Contains($"Sources: {DocId}", result.Output);
        Assert.Contains("More facts: pass --cursor next-page", result.Output);
    }

    [Fact]
    public async Task List_ASpaceById_UsesTheId()
    {
        _host.Routes(($"GET /api/memory/spaces/{SpaceId}/facts", CommandHost.Json(
            """{"facts":[],"nextCursor":null}""")));

        var result = await _host.RunAsync("memory", "list", "--space", SpaceId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"No facts in {SpaceId}.", result.Output);
    }

    [Fact]
    public async Task List_AnUnknownSpace_ShowsTheServersProblem()
    {
        _host.Routes(("GET /api/memory/spaces/nope/facts", CommandHost.Json(
            """{"title":"Not Found","status":404,"detail":"Space 'nope' not found."}""", HttpStatusCode.NotFound)));

        var result = await _host.RunAsync("memory", "list", "--space", "nope");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Space 'nope' not found.", result.Error);
    }

    [Fact]
    public async Task Forget_DeletesTheFactAndSaysHowManyVersionsWent()
    {
        _host.Routes(($"DELETE /api/memory/facts/{FactId}", CommandHost.Json("""{"deleted":2}""")));

        var result = await _host.RunAsync("memory", "forget", FactId);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"DELETE /api/memory/facts/{FactId}", _host.Sent.Single());
        Assert.Contains($"Forgot fact {FactId} (2 version(s) deleted).", result.Output);
    }

    [Fact]
    public async Task Forget_Json_PrintsTheServersResponse()
    {
        _host.Routes(($"DELETE /api/memory/facts/{FactId}", CommandHost.Json("""{"deleted":1}""")));

        var result = await _host.RunAsync("memory", "forget", FactId, "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal(1, doc.RootElement.GetProperty("deleted").GetInt32());
    }

    [Fact]
    public async Task Forget_NotAnId_IsRefusedBeforeAnyRequest()
    {
        var result = await _host.RunAsync("memory", "forget", "billing-service");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'billing-service' is not a fact ID", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Forget_Forbidden_NamesTheRoleAndScope()
    {
        _host.Routes(($"DELETE /api/memory/facts/{FactId}", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("memory", "forget", FactId);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Editor role", result.Error);
    }
}
