using System.Text.Json;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public sealed class SetupCommandTests : IDisposable
{
    private readonly CommandHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ClaudeCode_PrintsAutoModeSettingsForTheSignedInServer()
    {
        var result = await _host.RunAsync("setup", "claude-code");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(_host.Sent);
        using var doc = JsonDocument.Parse(result.Output);
        var autoMode = doc.RootElement.GetProperty("autoMode");

        var environment = autoMode.GetProperty("environment").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal("$defaults", environment[0]);
        Assert.Contains(environment, e => e.StartsWith("Key internal services:", StringComparison.Ordinal) && e.Contains("https://server.example"));
        Assert.Contains(environment, e => e.StartsWith("Org-specific CLIs:", StringComparison.Ordinal) && e.Contains("held by the server"));

        var allow = autoMode.GetProperty("allow").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal("$defaults", allow[0]);
        Assert.Contains("does not cover `connapse api` calls that change data", allow[1]);
    }

    [Fact]
    public async Task ClaudeCode_SaysWhereTheSettingsGo()
    {
        var result = await _host.RunAsync("setup", "claude-code");

        Assert.Contains("~/.claude/settings.json", result.Error);
        Assert.Contains("approval turned off", result.Error);
    }
}
