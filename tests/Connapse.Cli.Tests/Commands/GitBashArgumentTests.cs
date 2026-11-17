using System.Net;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

/// <summary>
/// Arguments as Git Bash on Windows hands them over: <c>@file</c> bodies, and server folders it has
/// already rewritten into Windows paths.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class GitBashArgumentTests : IDisposable
{
    private const string Rewritten = "C:/Program Files/Git/multi";

    private readonly CommandHost _host = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "connapse-cli-gitbash-" + Guid.NewGuid().ToString("N"));

    public GitBashArgumentTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    [Fact]
    public async Task Api_DataAtFile_SendsTheWholeFileAsTheBody()
    {
        // Spaces and line breaks are what a response file would have split into separate arguments.
        const string json = "{\n  \"name\": \"two words\",\n  \"tags\": [\"a b\", \"c\"]\n}\n";
        var file = Path.Combine(_folder, "body.json");
        File.WriteAllText(file, json);
        _host.Respond(_ => MockHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        var result = await _host.RunAsync("api", "POST", "/api/containers", "-d", "@" + file);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(json, Assert.Single(_host.Bodies));
    }

    [Fact]
    public async Task Upload_DestinationRewrittenByGitBash_IsRefusedWithTheFix()
    {
        var file = Path.Combine(_folder, "note.md");
        File.WriteAllText(file, "# note");

        var result = await _host.RunAsync("upload", file, "--container", "docs", "--destination", Rewritten);

        Assert.Equal(CliExitCodes.UserError, result.ExitCode);
        Assert.Contains("--destination", result.Error);
        Assert.Contains("MSYS_NO_PATHCONV=1", result.Error);
        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("files", "list", "--container", "docs", "--path", Rewritten)]
    [InlineData("search", "query", "--container", "docs", "--path", Rewritten)]
    public async Task FolderOptions_RewrittenByGitBash_AreRefusedWithTheFix(params string[] args)
    {
        var result = await _host.RunAsync(args);

        Assert.Equal(CliExitCodes.UserError, result.ExitCode);
        Assert.Contains("MSYS_NO_PATHCONV=1", result.Error);
        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("upload", "--destination")]
    [InlineData("files", "--path")]
    public async Task Refusal_QuotesTheUsersOwnPathAndItsRewrite(string command, string option)
    {
        const string rewrittenCode = "C:/Program Files/Git/code";
        var file = Path.Combine(_folder, "note.md");
        File.WriteAllText(file, "# note");
        string[] args = command == "upload"
            ? ["upload", file, "--container", "docs", "--destination", rewrittenCode]
            : ["files", "list", "--container", "docs", "--path", rewrittenCode];

        var result = await _host.RunAsync(args);

        Assert.Equal(CliExitCodes.UserError, result.ExitCode);
        Assert.Contains(option, result.Error);
        Assert.Contains("Git Bash rewrote '/code' into 'C:/Program Files/Git/code'", result.Error);
        Assert.Contains("MSYS_NO_PATHCONV=1", result.Error);
        Assert.DoesNotContain("/notes", result.Error);
        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("C:/Program Files/Git/code", "/code")]
    [InlineData("C:/Program Files/Git/code/sub", "/code/sub")]
    [InlineData("C:/Program Files/Git/", "/")]
    [InlineData(@"c:\program files\git\Code", "/Code")]
    [InlineData("D:/Tools/Git/code", "/code")]
    [InlineData(@"C:\Users\me\notes", null)]
    [InlineData("C:/Program Files/GitHub/code", null)]
    public void OriginalOf_StripsTheGitFolder(string value, string? expected) =>
        Assert.Equal(expected, GitBashPaths.OriginalOf(value, ["C:/Program Files/Git", @"D:\Tools\Git\"]));

    [Fact]
    public void Explain_WithoutAKnownGitFolder_QuotesTheValueOnly()
    {
        var message = GitBashPaths.Explain("E:/elsewhere/code", ["C:/Program Files/Git"]);

        Assert.Contains("'E:/elsewhere/code'", message);
        Assert.DoesNotContain("/notes", message);
        Assert.Contains("MSYS_NO_PATHCONV=1", message);
    }

    [Theory]
    [InlineData("C:/Program Files/Git/multi", true)]
    [InlineData(@"C:\Users\me\notes", true)]
    [InlineData("/multi", false)]
    [InlineData("multi/notes", false)]
    [InlineData("/", false)]
    public void LooksRewritten_SpotsDriveLetterPaths(string value, bool expected) =>
        Assert.Equal(expected, GitBashPaths.LooksRewritten(value));
}
