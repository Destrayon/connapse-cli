using System.Net;
using System.Text.Json;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

/// <summary><c>connapse code</c> reads the server's code index through /api/code (CC-14).</summary>
[Collection(ConsoleCollection.Name)]
public sealed class CodeCommandTests : IDisposable
{
    private const string Definition = """
        {"symbol":"Ledger.Post","kind":"method","language":"csharp","path":"/src/Ledger.cs","startLine":12,"endLine":30,
        "signature":"public void Post(Entry entry)","documentId":"d1"}
        """;
    private const string File = """
        {"path":"/src/Billing.cs","documentId":"d2","score":0.9,"reason":"calls it","lines":[{"line":44,"text":"ledger.Post(entry);"}]}
        """;

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Symbols_SendsTheQueryAndPrintsEachDefinition()
    {
        _host.Routes(("GET /api/code/symbols", CommandHost.Json($$"""
            {"notice":"1 file could not be read.","definitions":[{{Definition}}],
            "uses":[{"symbol":"Entry","kind":"class","path":"/src/Entry.cs","line":3,"type":"constructs","confidence":1}],
            "files":[{{File}}]}
            """)));

        var result = await _host.RunAsync("code", "symbols", "Ledger.Post", "--container", "my repo",
            "--match", "Words", "--follow", "--limit", "5");

        Assert.Equal(0, result.ExitCode);
        var sent = _host.Sent.Single();
        Assert.StartsWith("GET /api/code/symbols?", sent);
        Assert.Contains("containerId=my%20repo", sent);
        Assert.Contains("query=Ledger.Post", sent);
        Assert.Contains("match=words", sent);
        Assert.Contains("follow=true", sent);
        Assert.Contains("limit=5", sent);
        Assert.Contains("1 file could not be read.", result.Output);
        Assert.Contains("method Ledger.Post  /src/Ledger.cs:12-30 (csharp)", result.Output);
        Assert.Contains("public void Post(Entry entry)", result.Output);
        Assert.Contains("constructs class Entry  /src/Entry.cs:3", result.Output);
        Assert.Contains("/src/Billing.cs  (calls it)", result.Output);
        Assert.Contains("44: ledger.Post(entry);", result.Output);
    }

    [Fact]
    public async Task Symbols_WithoutOptions_SendsOnlyTheContainerAndQuery()
    {
        _host.Routes(("GET /api/code/symbols", CommandHost.Json("""{"notice":"","definitions":[],"uses":null,"files":[]}""")));

        var result = await _host.RunAsync("code", "symbols", "Post", "--container", "repo");

        Assert.Equal(0, result.ExitCode);
        var sent = _host.Sent.Single();
        Assert.DoesNotContain("match=", sent);
        Assert.DoesNotContain("follow=", sent);
        Assert.DoesNotContain("limit=", sent);
        Assert.Contains("No definitions found.", result.Output);
    }

    [Theory]
    [InlineData("symbols", "Post", "--match", "fuzzy")]
    [InlineData("symbols", "Post", "--limit", "101")]
    [InlineData("references", "Post", "--kind", "imports")]
    [InlineData("impact", "Post", "--depth", "4")]
    [InlineData("map", "--max-files", "0")]
    public async Task ABadOption_IsRefusedBeforeAnyRequest(params string[] args)
    {
        var result = await _host.RunAsync(["code", .. args, "--container", "repo"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task WithoutContainer_IsAParseError()
    {
        var result = await _host.RunAsync("code", "map");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task References_SendsTheKindAndListsTheFiles()
    {
        _host.Routes(("GET /api/code/references", CommandHost.Json($$"""
            {"notice":"","definitions":[{{Definition}}],"files":[{{File}}]}
            """)));

        var result = await _host.RunAsync("code", "references", "Ledger.Post", "--container", "repo", "--kind", "calls", "--limit", "50");

        Assert.Equal(0, result.ExitCode);
        var sent = _host.Sent.Single();
        Assert.StartsWith("GET /api/code/references?", sent);
        Assert.Contains("symbol=Ledger.Post", sent);
        Assert.Contains("kind=calls", sent);
        Assert.Contains("limit=50", sent);
        Assert.Contains("Used in:", result.Output);
        Assert.Contains("/src/Billing.cs  (calls it)", result.Output);
    }

    [Fact]
    public async Task Impact_ListsAffectedSymbolsNearestFirstAndTheirTests()
    {
        _host.Routes(("GET /api/code/impact", CommandHost.Json($$"""
            {"notice":"","definitions":[{{Definition}}],
            "affected":[{"symbol":"LedgerTests.Posts","kind":"method","path":"/tests/LedgerTests.cs","hops":2,"confidence":0.8,"isTest":true},
                        {"symbol":"Billing.Charge","kind":"method","path":"/src/Billing.cs","hops":1,"confidence":1,"isTest":false}],
            "files":[],"tests":["/tests/LedgerTests.cs"]}
            """)));

        var result = await _host.RunAsync("code", "impact", "Ledger.Post", "--container", "repo", "--depth", "3");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("depth=3", _host.Sent.Single());
        int charge = result.Output.IndexOf("1 hop  method Billing.Charge  /src/Billing.cs", StringComparison.Ordinal);
        int test = result.Output.IndexOf("2 hops  method LedgerTests.Posts  /tests/LedgerTests.cs  [test]", StringComparison.Ordinal);
        Assert.True(charge >= 0 && test > charge, result.Output);
        Assert.Contains("Tests:", result.Output);
    }

    [Fact]
    public async Task Map_SendsThePathAndPrintsEachFilesSymbols()
    {
        _host.Routes(("GET /api/code/map", CommandHost.Json("""
            {"notice":"","totalFiles":120,"files":[{"path":"/src/Ledger.cs","documentId":"d1","rank":0.4,
            "symbols":[{"symbol":"Ledger","kind":"class","line":5,"signature":null,"referencedBy":14}]}]}
            """)));

        var result = await _host.RunAsync("code", "map", "--container", "repo", "--path", "/src", "--max-files", "10");

        Assert.Equal(0, result.ExitCode);
        var sent = _host.Sent.Single();
        Assert.Contains("path=%2Fsrc", sent);
        Assert.Contains("maxFiles=10", sent);
        Assert.Contains("1 of 120 indexed files", result.Output);
        Assert.Contains("5: class Ledger  used by 14 files", result.Output);
    }

    [Fact]
    public async Task Json_PrintsTheServersAnswer()
    {
        _host.Routes(("GET /api/code/references", CommandHost.Json($$"""
            {"notice":"","definitions":[{{Definition}}],"files":[]}
            """)));

        var result = await _host.RunAsync("code", "references", "Ledger.Post", "--container", "repo", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("Ledger.Post", doc.RootElement.GetProperty("definitions")[0].GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task ANotIndexedContainer_ShowsTheServersProblem()
    {
        _host.Routes(("GET /api/code/map", CommandHost.Json(
            """{"title":"Conflict","status":409,"detail":"Container 'notes' has no indexed code."}""", HttpStatusCode.Conflict)));

        var result = await _host.RunAsync("code", "map", "--container", "notes");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Container 'notes' has no indexed code.", result.Error);
    }
}
