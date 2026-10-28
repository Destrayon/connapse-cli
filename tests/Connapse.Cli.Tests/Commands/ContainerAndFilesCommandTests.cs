using System.Net;
using System.Web;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class ContainerAndFilesCommandTests : IDisposable
{
    private const string ContainerId = "7b1c9e2a-0000-4000-8000-0000000000a1";
    private const string FileId = "7b1c9e2a-0000-4000-8000-0000000000f1";

    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string Page(IEnumerable<string> items, bool hasMore) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":0,"hasMore":{{(hasMore ? "true" : "false")}}}""";

    private static string Container(int n) =>
        $$"""{"id":"7b1c9e2a-0000-4000-8000-{{n:D12}}","name":"c-{{n:D3}}","documentCount":{{n}}}""";

    private static string File(string path, string id = FileId) =>
        $$"""{"name":"{{path[(path.LastIndexOf('/') + 1)..]}}","path":"{{path}}","isFolder":false,"status":"Ready","id":"{{id}}"}""";

    private static string Folder(string path) =>
        $$"""{"name":"{{path.TrimEnd('/')[(path.TrimEnd('/').LastIndexOf('/') + 1)..]}}","path":"{{path}}","isFolder":true,"id":"{{Guid.NewGuid()}}"}""";

    private static HttpResponseMessage Ok(string json) => MockHttpMessageHandler.Json(HttpStatusCode.OK, json);

    private static HttpResponseMessage NotFound() =>
        MockHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"title":"Not found","status":404}""");

    /// <summary>Serves a list as the server does: the requested skip/take slice, and whether more follow.</summary>
    private static HttpResponseMessage Slice(HttpRequestMessage request, IReadOnlyList<string> all)
    {
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        int skip = int.Parse(query["skip"] ?? "0");
        int take = int.Parse(query["take"] ?? "50");
        return Ok(Page(all.Skip(skip).Take(take), skip + take < all.Count));
    }

    private void ServeFiles(Dictionary<string, List<string>> byFolder)
    {
        _host.Respond(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/containers/by-name/docs")
                return Ok($$"""{"id":"{{ContainerId}}","name":"docs"}""");
            if (path == $"/api/containers/{ContainerId}/files")
            {
                var folder = HttpUtility.ParseQueryString(request.RequestUri.Query)["path"] ?? "/";
                return Slice(request, byFolder.GetValueOrDefault(folder) ?? []);
            }
            if (path == $"/api/containers/{ContainerId}/files/{FileId}/content")
                return Ok($$"""{"documentId":"{{FileId}}","content":"hello from the file"}""");
            return NotFound();
        });
    }

    [Fact]
    public async Task ContainerList_MoreThanOnePage_PrintsEveryContainer()
    {
        var all = Enumerable.Range(1, 450).Select(Container).ToList();
        _host.Respond(request => request.RequestUri!.AbsolutePath == "/api/containers" ? Slice(request, all) : NotFound());

        var result = await _host.RunAsync("container", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Found 450 container(s)", result.Output);
        Assert.Contains("c-001", result.Output);
        Assert.Contains("c-201", result.Output);
        Assert.Contains("c-450", result.Output);
        Assert.Equal(
            ["GET /api/containers?skip=0&take=200", "GET /api/containers?skip=200&take=200", "GET /api/containers?skip=400&take=200"],
            _host.Sent);
    }

    [Fact]
    public async Task ContainerList_None_SaysSo()
    {
        _host.Respond(_ => Ok(Page([], hasMore: false)));

        var result = await _host.RunAsync("container", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No containers found.", result.Output);
    }

    [Fact]
    public async Task FilesGet_APathInASubfolder_ListsThatFolderAndPrintsTheFile()
    {
        ServeFiles(new()
        {
            ["/"] = [Folder("/guides/")],
            ["/guides/setup/"] = [File("/guides/setup/other.md", Guid.NewGuid().ToString()), File("/guides/setup/install.md")],
        });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "guides/setup/install.md");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the file", result.Output);
        Assert.Contains($"GET /api/containers/{ContainerId}/files?path=%2Fguides%2Fsetup%2F&skip=0&take=200", _host.Sent);
    }

    [Fact]
    public async Task FilesGet_AtTheRootPastTheFirstPage_IsFound()
    {
        var root = Enumerable.Range(1, 250).Select(i => File($"/f-{i:D3}.txt", Guid.NewGuid().ToString())).ToList();
        root.Add(File("/zz-last.txt"));
        ServeFiles(new() { ["/"] = root });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "/zz-last.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the file", result.Output);
    }

    [Fact]
    public async Task FilesGet_BackslashesAndOtherCase_StillMatch()
    {
        ServeFiles(new() { ["/Guides/"] = [File("/Guides/Install.md")] });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", @"Guides\install.MD");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the file", result.Output);
    }

    [Fact]
    public async Task FilesGet_TwoFilesDifferingOnlyInCase_PrefersTheExactPath()
    {
        ServeFiles(new() { ["/a/"] = [File("/a/README.md", Guid.NewGuid().ToString()), File("/a/readme.md")] });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "a/readme.md");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the file", result.Output);
    }

    [Fact]
    public async Task FilesGet_AmbiguousIgnoringCase_AsksForTheExactPath()
    {
        ServeFiles(new() { ["/a/"] = [File("/a/README.md", Guid.NewGuid().ToString()), File("/a/Readme.md")] });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "a/readme.md");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("More than one entry matches 'a/readme.md'", result.Error);
    }

    [Fact]
    public async Task FilesGet_DotSegmentsAndDoubledSlashes_CollapseAsTheServerDoes()
    {
        ServeFiles(new() { ["/guides/"] = [File("/guides/install.md")] });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "guides/./old/..//install.md");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the file", result.Output);
    }

    [Fact]
    public async Task FilesGet_FolderCaseDiffers_WalksTheFoldersToFindIt()
    {
        // The server matches the folder case-sensitively: '/guides/setup/' lists nothing here.
        ServeFiles(new()
        {
            ["/"] = [Folder("/Guides/")],
            ["/Guides/"] = [Folder("/Guides/Setup/")],
            ["/Guides/Setup/"] = [File("/Guides/Setup/Install.md")],
        });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "guides/setup/install.md");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from the file", result.Output);
    }

    [Fact]
    public async Task FilesGet_FolderMissing_IsNotFound()
    {
        ServeFiles(new() { ["/"] = [Folder("/guides/")] });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "other/install.md");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("File 'other/install.md' not found in container 'docs'.", result.Error);
    }

    [Fact]
    public async Task FilesGet_ANameThatIsOnlyInASubfolder_IsNotFoundAtTheRoot()
    {
        ServeFiles(new() { ["/"] = [Folder("/guides/")], ["/guides/"] = [File("/guides/install.md")] });

        var result = await _host.RunAsync("files", "get", "--container", "docs", "--file", "install.md");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("File 'install.md' not found in container 'docs'.", result.Error);
    }
}
