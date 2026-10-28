using System.Net;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class ConnectionCommandTests : IDisposable
{
    private const string Id = "7b1c9e2a-0000-4000-8000-0000000000c1";
    private const string Connection = $$"""
        {"id":"{{Id}}","name":"docs-bucket","provider":"S3","config":{"region":"us-east-1"},"hasSecret":true,
        "sourceCount":2,"syncPaused":false,"createdAt":"2026-10-01T00:00:00Z","updatedAt":"2026-10-02T00:00:00Z"}
        """;
    private const string Page = $$"""{"items":[{{Connection}}],"totalCount":1,"hasMore":false}""";

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task List_PrintsEachConnectionAndSendsTheFilter()
    {
        _host.Routes(("GET /api/v1/connections", CommandHost.Json(Page)));

        var result = await _host.RunAsync("connection", "list", "--provider", "s3", "--take", "10");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GET /api/v1/connections?provider=S3&skip=0&take=10", _host.Sent.Single());
        Assert.Contains("docs-bucket", result.Output);
        Assert.Contains(Id, result.Output);
    }

    [Fact]
    public async Task List_AnUnknownProvider_IsAValidationError()
    {
        var result = await _host.RunAsync("connection", "list", "--provider", "dropbox");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--provider must be one of: Filesystem, S3, AzureBlob, Sftp, GitHub, Atlassian, Custom", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Get_ByName_FindsItInTheListThenFetchesIt()
    {
        _host.Routes(
            ("GET /api/v1/connections", CommandHost.Json(Page)),
            ($"GET /api/v1/connections/{Id}", CommandHost.Json(Connection)));

        var result = await _host.RunAsync("connection", "get", "DOCS-BUCKET");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"GET /api/v1/connections/{Id}", _host.Sent[1]);
        Assert.Contains("Provider:   S3", result.Output);
        Assert.Contains("Secret:     stored", result.Output);
    }

    [Fact]
    public async Task Get_ById_SkipsTheLookup()
    {
        _host.Routes(($"GET /api/v1/connections/{Id}", CommandHost.Json(Connection)));

        var result = await _host.RunAsync("connection", "get", Id, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(_host.Sent);
        Assert.Contains("\"region\": \"us-east-1\"", result.Output);
    }

    [Fact]
    public async Task Get_AnUnknownName_SaysSo()
    {
        _host.Routes(("GET /api/v1/connections", CommandHost.Json("""{"items":[],"totalCount":0,"hasMore":false}""")));

        var result = await _host.RunAsync("connection", "get", "nope");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No connection named 'nope'.", result.Error);
    }

    [Fact]
    public async Task Get_ANameTwoConnectionsShare_AsksForTheId()
    {
        var other = Connection.Replace(Id, "7b1c9e2a-0000-4000-8000-0000000000c2");
        _host.Routes(("GET /api/v1/connections", CommandHost.Json($$"""{"items":[{{Connection}},{{other}}],"totalCount":2,"hasMore":false}""")));

        var result = await _host.RunAsync("connection", "get", "docs-bucket");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("pass its ID instead", result.Error);
    }

    [Fact]
    public async Task Get_ANameOnALaterPage_IsFound()
    {
        var first = $$"""{"items":[{{Connection.Replace("docs-bucket", "other")}}],"totalCount":2,"hasMore":true}""";
        _host.Respond(request => request.RequestUri!.PathAndQuery switch
        {
            "/api/v1/connections?skip=0&take=100" => MockHttpMessageHandler.Json(HttpStatusCode.OK, first),
            "/api/v1/connections?skip=100&take=100" => MockHttpMessageHandler.Json(HttpStatusCode.OK, Page),
            _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, Connection),
        });

        var result = await _host.RunAsync("connection", "get", "docs-bucket");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, _host.Sent.Count);
    }

    [Fact]
    public async Task Forbidden_NamesTheRoleAndScopeNeeded()
    {
        _host.Routes(("GET /api/v1/connections", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("connection", "list");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Not allowed.", result.Error);
        Assert.Contains("Owner or Admin role", result.Error);
        Assert.Contains("'admin:sources' scope", result.Error);
    }

    [Fact]
    public async Task Create_SendsTheProviderConfigAndToken()
    {
        _host.Routes(("POST /api/v1/connections", CommandHost.Json(
            $$"""{"connection":{{Connection}},"note":"Sources can now be added."}""", HttpStatusCode.Created)));

        var result = await _host.RunAsync("connection", "create", "docs-bucket", "--provider", "s3",
            "--config", """{"region":"us-east-1"}""", "--token", "secret-1");

        Assert.Equal(0, result.ExitCode);
        var body = _host.Bodies.Single();
        Assert.Contains("\"name\":\"docs-bucket\"", body);
        Assert.Contains("\"provider\":\"S3\"", body);
        Assert.Contains("\"config\":{\"region\":\"us-east-1\"}", body);
        Assert.Contains("\"token\":\"secret-1\"", body);
        Assert.Contains("Created connection 'docs-bucket'", result.Output);
        Assert.Contains("Sources can now be added.", result.Output);
    }

    [Fact]
    public async Task Create_ConfigFromAFileAndTokenFromStdin()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, """{"host":"files.example","port":22}""");
        var originalIn = Console.In;
        Console.SetIn(new StringReader("piped-token\n"));
        try
        {
            _host.Routes(("POST /api/v1/connections", CommandHost.Json($$"""{"connection":{{Connection}}}""", HttpStatusCode.Created)));

            var result = await _host.RunAsync("connection", "create", "files", "--provider", "Sftp", "--config-file", file, "--token-stdin");

            Assert.True(result.ExitCode == 0, result.Error);
            var body = _host.Bodies.Single();
            Assert.Contains("\"host\":\"files.example\"", body);
            Assert.Contains("\"port\":22", body);
            Assert.Contains("\"token\":\"piped-token\"", body);
        }
        finally
        {
            Console.SetIn(originalIn);
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Create_ConfigThatIsNotAnObject_IsRefused()
    {
        var result = await _host.RunAsync("connection", "create", "x", "--provider", "S3", "--config", "[1,2]");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--config must be a JSON object.", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Update_SendsOnlyWhatChanged()
    {
        _host.Routes(($"PATCH /api/v1/connections/{Id}", CommandHost.Json(Connection.Replace("docs-bucket", "renamed"))));

        var result = await _host.RunAsync("connection", "update", Id, "--name", "renamed");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("""{"name":"renamed"}""", _host.Bodies.Single());
        Assert.Contains("Updated connection 'renamed'.", result.Output);
    }

    [Fact]
    public async Task Update_WithNothing_IsRefused()
    {
        var result = await _host.RunAsync("connection", "update", Id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Nothing to update", result.Error);
    }

    [Fact]
    public async Task Delete_ConfirmsWithTheConnectionsName()
    {
        _host.Routes(
            ($"GET /api/v1/connections/{Id}", CommandHost.Json(Connection)),
            ($"DELETE /api/v1/connections/{Id}", CommandHost.Status(HttpStatusCode.NoContent)));

        var result = await _host.RunAsync("connection", "delete", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"DELETE /api/v1/connections/{Id}?confirmName=docs-bucket", _host.Sent[1]);
        Assert.Contains("Deleted connection 'docs-bucket'.", result.Output);
    }

    [Fact]
    public async Task Delete_InUse_ListsTheSourcesInTheWay()
    {
        _host.Routes(
            ($"GET /api/v1/connections/{Id}", CommandHost.Json(Connection)),
            ($"DELETE /api/v1/connections/{Id}", CommandHost.Json("""
                {"type":"https://connapse.dev/problems/connection-in-use","title":"Connection in use","status":409,
                "detail":"2 sources still use this connection.","sources":[{"id":"7b1c9e2a-0000-4000-8000-0000000000d1","name":"handbook"},
                {"id":"7b1c9e2a-0000-4000-8000-0000000000d2","name":"wiki"}]}
                """, HttpStatusCode.Conflict)));

        var result = await _host.RunAsync("connection", "delete", Id);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("2 sources still use this connection.", result.Error);
        Assert.Contains("Delete these sources first: handbook, wiki", result.Error);
    }

    [Fact]
    public async Task Pause_ReportsHowManySourcesStopped()
    {
        _host.Routes(($"POST /api/v1/connections/{Id}/pause", CommandHost.Json("""{"syncPaused":true,"sources":2}""")));

        var result = await _host.RunAsync("connection", "pause", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Paused syncing for 2 source(s).", result.Output);
    }

    [Fact]
    public async Task Resume_SendsTheChosenSources()
    {
        _host.Routes(($"POST /api/v1/connections/{Id}/resume", CommandHost.Json("""{"syncPaused":false,"sources":1}""")));

        var result = await _host.RunAsync("connection", "resume", Id, "--source", "7b1c9e2a-0000-4000-8000-0000000000d1");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("""{"sourceIds":["7b1c9e2a-0000-4000-8000-0000000000d1"]}""", _host.Bodies.Single());
        Assert.Contains("Resumed syncing for 1 source(s).", result.Output);
    }

    [Fact]
    public async Task Test_AFailure_ExitsOneWithTheServersMessage()
    {
        _host.Routes(($"POST /api/v1/connections/{Id}/test", CommandHost.Json("""{"success":false,"message":"Access denied to bucket."}""")));

        var result = await _host.RunAsync("connection", "test", Id, "--probe-target", "docs");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("\"probeTarget\":\"docs\"", _host.Bodies.Single());
        Assert.Contains("Failed: Access denied to bucket.", result.Output);
    }

    [Fact]
    public async Task Test_ASuccess_ExitsZero()
    {
        _host.Routes(($"POST /api/v1/connections/{Id}/test", CommandHost.Json("""{"success":true,"message":"Read 3 objects."}""")));

        var result = await _host.RunAsync("connection", "test", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Connected. Read 3 objects.", result.Output);
    }

    [Fact]
    public async Task Discover_ListsWhatTheConnectionReaches()
    {
        _host.Routes(($"GET /api/v1/connections/{Id}/discover", CommandHost.Json("""
            {"outcome":"Listed","buckets":["docs","logs"],"spaces":[{"key":"ENG","name":"Engineering"}]}
            """)));

        var result = await _host.RunAsync("connection", "discover", Id, "--include-personal");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"GET /api/v1/connections/{Id}/discover?includePersonal=true", _host.Sent.Single());
        Assert.Contains("Buckets:", result.Output);
        Assert.Contains("  logs", result.Output);
        Assert.Contains("  ENG  Engineering", result.Output);
    }

    [Fact]
    public async Task ForgetHostKey_DeletesTheSavedKey()
    {
        _host.Routes(($"DELETE /api/v1/connections/{Id}/host-key", CommandHost.Json(Connection)));

        var result = await _host.RunAsync("connection", "forget-host-key", Id);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Forgot the host key for 'docs-bucket'.", result.Output);
    }

    [Fact]
    public async Task ReplaceSecret_SendsTheKeyFromItsFile()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "-----BEGIN OPENSSH PRIVATE KEY-----");
        try
        {
            _host.Routes(($"PUT /api/v1/connections/{Id}/secret", CommandHost.Json(Connection)));

            var result = await _host.RunAsync("connection", "replace-secret", Id, "--private-key-file", file, "--passphrase", "pw");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"privateKey\":\"-----BEGIN OPENSSH PRIVATE KEY-----\"", _host.Bodies.Single());
            Assert.Contains("\"passphrase\":\"pw\"", _host.Bodies.Single());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ReplaceSecret_WithoutASecret_IsRefused()
    {
        var result = await _host.RunAsync("connection", "replace-secret", Id);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_host.Sent);
    }
}
