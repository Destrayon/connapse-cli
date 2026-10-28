using System.Net;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class AgentCommandTests : IDisposable
{
    private const string Id = "7b1c9e2a-0000-4000-8000-0000000000a1";
    private const string KeyId = "7b1c9e2a-0000-4000-8000-0000000000b1";
    private const string Key = $$"""
        {"id":"{{KeyId}}","name":"ci","tokenPrefix":"cnp_ab12","scopes":["knowledge:read"],
        "createdAt":"2026-10-01T00:00:00Z","isRevoked":false,"requiresApproval":false}
        """;
    private const string Agent = $$"""
        {"id":"{{Id}}","name":"indexer","description":"Nightly ingest","isActive":true,
        "createdAt":"2026-10-01T00:00:00Z","keys":[{{Key}}]}
        """;
    private const string List = $"[{Agent}]";

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task List_PrintsEachAgent()
    {
        _host.Routes(("GET /api/v1/agents", CommandHost.Json(List)));

        var result = await _host.RunAsync("agent", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("indexer", result.Output);
        Assert.Contains(Id, result.Output);
    }

    [Fact]
    public async Task List_Forbidden_NamesTheRoleAndScope()
    {
        _host.Routes(("GET /api/v1/agents", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("agent", "list");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Owner or Admin role", result.Error);
        Assert.Contains("'admin:users'", result.Error);
    }

    [Fact]
    public async Task Get_ByName_PrintsTheAgent()
    {
        _host.Routes(
            ("GET /api/v1/agents", CommandHost.Json(List)),
            ($"GET /api/v1/agents/{Id}", CommandHost.Json(Agent)));

        var result = await _host.RunAsync("agent", "get", "INDEXER");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nightly ingest", result.Output);
        Assert.Contains("Keys:     1 active, 0 revoked", result.Output);
    }

    [Fact]
    public async Task Get_CountsExpiredKeysAsInactive()
    {
        var expired = Key.Replace("\"name\":\"ci\"", "\"name\":\"old\",\"expiresAt\":\"2020-01-01T00:00:00Z\"");
        var agent = Agent.Replace($"[{Key}]", $"[{Key},{expired}]");
        _host.Routes(($"GET /api/v1/agents", CommandHost.Json($"[{agent}]")), ($"GET /api/v1/agents/{Id}", CommandHost.Json(agent)));

        var result = await _host.RunAsync("agent", "get", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Keys:     1 active, 0 revoked", result.Output);
    }

    [Fact]
    public async Task KeyCreate_AnExplicitFalseApproval_IsSent()
    {
        _host.Routes(($"POST /api/v1/agents/{Id}/keys", CommandHost.Json($$"""
            {"token":"t","keyId":"{{KeyId}}","agentId":"{{Id}}","scopes":["knowledge:read"]}
            """, HttpStatusCode.Created)));

        var result = await _host.RunAsync("agent", "key", "create", Id, "--name", "ci",
            "--scope", "knowledge:read", "--requires-approval", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"requiresApproval\":false", _host.Bodies.Single());
    }

    [Fact]
    public async Task Get_AnUnknownName_SaysSo()
    {
        _host.Routes(("GET /api/v1/agents", CommandHost.Json("[]")));

        var result = await _host.RunAsync("agent", "get", "nope");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No agent named 'nope'.", result.Error);
    }

    [Fact]
    public async Task Create_SendsTheNameAndDescription()
    {
        _host.Routes(("POST /api/v1/agents", CommandHost.Json(Agent, HttpStatusCode.Created)));

        var result = await _host.RunAsync("agent", "create", "indexer", "--description", "Nightly ingest");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("""{"description":"Nightly ingest","name":"indexer"}""", _host.Bodies.Single());
        Assert.Contains($"Created agent 'indexer' ({Id}).", result.Output);
    }

    [Fact]
    public async Task Delete_WithoutYes_SendsNothing()
    {
        var result = await _host.RunAsync("agent", "delete", Id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("pass --yes", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Delete_WithYes_DeletesTheAgent()
    {
        _host.Routes(($"DELETE /api/v1/agents/{Id}", CommandHost.Status(HttpStatusCode.NoContent)));

        var result = await _host.RunAsync("agent", "delete", Id, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"DELETE /api/v1/agents/{Id}", _host.Sent.Single());
    }

    [Theory]
    [InlineData("enable", "true")]
    [InlineData("disable", "false")]
    public async Task EnableAndDisable_SetIsActive(string verb, string expected)
    {
        _host.Routes(($"PUT /api/v1/agents/{Id}/active", CommandHost.Status(HttpStatusCode.NoContent)));

        var result = await _host.RunAsync("agent", verb, Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($$"""{"isActive":{{expected}}}""", _host.Bodies.Single());
    }

    [Fact]
    public async Task KeyList_PrintsEachKeyWithoutATokenToShow()
    {
        var revoked = Key.Replace("\"isRevoked\":false", "\"isRevoked\":true").Replace("\"ci\"", "\"old\"");
        _host.Routes(($"GET /api/v1/agents/{Id}/keys", CommandHost.Json($"[{Key},{revoked}]")));

        var result = await _host.RunAsync("agent", "key", "list", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("cnp_ab12", result.Output);
        Assert.Contains("active", result.Output);
        Assert.Contains("revoked", result.Output);
    }

    [Fact]
    public async Task KeyCreate_SendsTheScopesAndPrintsTheTokenOnce()
    {
        _host.Routes(($"POST /api/v1/agents/{Id}/keys", CommandHost.Json($$"""
            {"token":"cnp_secret_token","keyId":"{{KeyId}}","agentId":"{{Id}}","scopes":["knowledge:read","agent:ingest"]}
            """, HttpStatusCode.Created)));

        var result = await _host.RunAsync("agent", "key", "create", Id, "--name", "ci",
            "--scope", "knowledge:read", "--scope", "agent:ingest", "--expires-in-days", "30");

        Assert.Equal(0, result.ExitCode);
        var body = _host.Bodies.Single();
        Assert.Contains("\"scopes\":[\"knowledge:read\",\"agent:ingest\"]", body);
        Assert.Contains("\"expiresAt\":", body);
        Assert.Contains("cnp_secret_token", result.Output);
        Assert.Contains("not shown again", result.Output);
    }

    [Fact]
    public async Task KeyCreate_AnAdminScope_IsAValidationError()
    {
        var result = await _host.RunAsync("agent", "key", "create", Id, "--name", "ci", "--scope", "admin:users");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'admin:users' isn't an agent scope", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task KeyRevoke_DeletesTheKey()
    {
        _host.Routes(($"DELETE /api/v1/agents/{Id}/keys/{KeyId}", CommandHost.Status(HttpStatusCode.NoContent)));

        var result = await _host.RunAsync("agent", "key", "revoke", Id, KeyId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Revoked key {KeyId}.", result.Output);
    }
}
