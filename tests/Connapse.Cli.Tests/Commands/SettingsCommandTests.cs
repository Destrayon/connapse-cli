using System.Net;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class SettingsCommandTests : IDisposable
{
    private const string Chunking = """{"strategy":"Semantic","maxChunkSize":512,"overlap":64,"futureField":"kept"}""";

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Get_PrintsTheCategoryAsJson()
    {
        _host.Routes(("GET /api/settings/chunking", CommandHost.Json(Chunking)));

        var result = await _host.RunAsync("settings", "get", "chunking");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"maxChunkSize\": 512", result.Output);
    }

    [Fact]
    public async Task Get_Forbidden_NamesTheRoleAndScope()
    {
        _host.Routes(("GET /api/settings/chunking", CommandHost.Json(
            """{"title":"Forbidden","status":403,"detail":"Not allowed."}""", HttpStatusCode.Forbidden)));

        var result = await _host.RunAsync("settings", "get", "chunking");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("'admin:settings'", result.Error);
    }

    [Fact]
    public async Task Set_MergesThePairsIntoWhatIsSavedAndKeepsEveryOtherField()
    {
        _host.Routes(
            ("GET /api/settings/chunking", CommandHost.Json(Chunking)),
            ("PUT /api/settings/chunking", CommandHost.Json("""{"success":true,"message":"Settings for 'chunking' updated successfully"}""")));

        var result = await _host.RunAsync("settings", "set", "chunking", "maxchunksize=1024", "strategy=Recursive");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            """{"strategy":"Recursive","maxChunkSize":1024,"overlap":64,"futureField":"kept"}""",
            _host.Bodies.Single());
        Assert.Contains("updated successfully", result.Output);
    }

    [Fact]
    public async Task Set_AKeyTheServerDidNotReturn_WarnsAndSendsIt()
    {
        _host.Routes(
            ("GET /api/settings/chunking", CommandHost.Json(Chunking)),
            ("PUT /api/settings/chunking", CommandHost.Json("""{"success":true}""")));

        var result = await _host.RunAsync("settings", "set", "chunking", "newKnob=true");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"newKnob\":true", _host.Bodies.Single());
        Assert.Contains("'newKnob' isn't among", result.Output);
    }

    [Fact]
    public async Task Set_WithSettings_ReplacesTheCategoryWithoutReadingIt()
    {
        _host.Routes(("PUT /api/settings/search", CommandHost.Json("""{"success":true}""")));

        var result = await _host.RunAsync("settings", "set", "search", "--settings", """{"topK":20}""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("PUT /api/settings/search", _host.Sent.Single());
        Assert.Equal("""{"topK":20}""", _host.Bodies.Single());
    }

    [Fact]
    public async Task Set_NewEmbeddingModel_WarnsAboutOldVectors()
    {
        _host.Routes(
            ("GET /api/settings/embedding", CommandHost.Json("""{"provider":"Ollama","model":"a"}""")),
            ("PUT /api/settings/embedding", CommandHost.Json(
                """{"success":true,"legacyVectorsExist":true,"legacyVectorCount":900,"legacyModels":[]}""")));

        var result = await _host.RunAsync("settings", "set", "embedding", "model=b");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("900 vector(s)", result.Output);
    }

    [Fact]
    public async Task Set_APairWithoutAnEquals_IsAValidationError()
    {
        _host.Routes(("GET /api/settings/chunking", CommandHost.Json(Chunking)));

        var result = await _host.RunAsync("settings", "set", "chunking", "maxChunkSize");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("isn't key=value", result.Error);
    }

    [Fact]
    public async Task Set_Nothing_IsAValidationError()
    {
        var result = await _host.RunAsync("settings", "set", "chunking");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Nothing to set", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Reset_DeletesTheCategory()
    {
        _host.Routes(("DELETE /api/v1/settings/search", CommandHost.Status(HttpStatusCode.NoContent)));

        var result = await _host.RunAsync("settings", "reset", "search");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Reset the 'search' settings", result.Output);
    }

    [Fact]
    public async Task Test_WithoutSettings_TestsTheSavedOnes()
    {
        _host.Routes(
            ("GET /api/settings/embedding", CommandHost.Json("""{"provider":"Ollama","baseUrl":"http://ollama:11434"}""")),
            ("POST /api/settings/test-connection", CommandHost.Json("""{"success":true,"message":"Ollama is up"}""")));

        var result = await _host.RunAsync("settings", "test", "embedding");

        Assert.Equal(0, result.ExitCode);
        var body = _host.Bodies.Single();
        Assert.Contains("\"category\":\"embedding\"", body);
        Assert.Contains("\"baseUrl\":\"http://ollama:11434\"", body);
        Assert.Contains("Ollama is up", result.Output);
    }

    [Fact]
    public async Task Test_AFailure_ExitsOne()
    {
        _host.Routes(("POST /api/settings/test-connection", CommandHost.Json("""{"success":false,"message":"refused"}""")));

        var result = await _host.RunAsync("settings", "test", "minio", "--settings", """{"endpoint":"x"}""");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Couldn't reach it: refused", result.Output);
    }

    [Fact]
    public async Task Test_NothingSavedAndNoSettings_AsksForThem()
    {
        _host.Routes();

        var result = await _host.RunAsync("settings", "test", "crossencoder");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--settings or --settings-file", result.Error);
    }

    [Fact]
    public async Task Reindex_SendsForceAndTheContainer()
    {
        const string containerId = "7b1c9e2a-0000-4000-8000-0000000000e1";
        _host.Routes(("POST /api/settings/reindex", CommandHost.Json("""{"success":true,"message":"Reindex started"}""")));

        var result = await _host.RunAsync("settings", "reindex", "--force", "--container", containerId);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($$"""{"containerId":"{{containerId}}","force":true}""", _host.Bodies.Single());
        Assert.Contains("Reindex started", result.Output);
    }

    [Fact]
    public async Task ReindexStatus_PrintsTheQueue()
    {
        _host.Routes(("GET /api/settings/reindex/status", CommandHost.Json(
            """{"status":"Running","isActive":true,"queueDepth":42,"startedAt":"2026-10-28T10:00:00Z"}""")));

        var result = await _host.RunAsync("settings", "reindex-status");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Status:       Running", result.Output);
        Assert.Contains("Queue depth:  42", result.Output);
    }

    [Fact]
    public async Task EmbeddingModels_MarksTheCurrentOne()
    {
        _host.Routes(("GET /api/settings/embedding-models", CommandHost.Json("""
            {"currentModel":"ollama:b","hasLegacyVectors":true,"models":[
            {"modelId":"ollama:a","dimensions":768,"vectorCount":900,"isCurrent":false},
            {"modelId":"ollama:b","dimensions":1024,"vectorCount":10,"isCurrent":true}]}
            """)));

        var result = await _host.RunAsync("settings", "embedding-models");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ollama:b (current)", result.Output);
        Assert.Contains("another model", result.Output);
    }

    [Fact]
    public async Task ClearSummaries_PrintsTheCounts()
    {
        _host.Routes(("POST /api/v1/settings/summaries/clear", CommandHost.Json("""{"containers":3,"documents":120}""")));

        var result = await _host.RunAsync("settings", "clear-summaries");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Cleared 3 container and 120 document summaries.", result.Output);
    }
}
