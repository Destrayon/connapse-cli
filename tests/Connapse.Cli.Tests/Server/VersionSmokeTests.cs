using System.Diagnostics;

namespace Connapse.Cli.Tests.Server;

/// <summary>
/// Runs only when tests/server/smoke/run-smoke-test.sh has started a released server and signed in;
/// skipped otherwise, so ordinary CI stays fast.
/// </summary>
public sealed class VersionSmokeFactAttribute : FactAttribute
{
    public VersionSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CONNAPSE_SMOKE_HOME") is null)
            Skip = "Needs a running server: tests/server/smoke/run-smoke-test.sh";
    }
}

/// <summary>
/// The CLI against a released server image, the current one and the one before it (CON-129). The
/// everyday commands work on both. A command whose feature the server predates fails with a message
/// naming that feature, never a raw status code or an unexpected error.
/// CONNAPSE_SMOKE_EXPECT_MISSING lists the features this server is known to lack, comma-separated.
/// </summary>
[Trait("Category", "ServerSmoke")]
public sealed class VersionSmokeTests
{
    private sealed record Run(int Exit, string Out, string Err);

    [VersionSmokeFact]
    public async Task Everyday_commands_work_and_missing_features_are_named()
    {
        string container = "smoke-" + Guid.NewGuid().ToString("N")[..8];
        var missing = (Environment.GetEnvironmentVariable("CONNAPSE_SMOKE_EXPECT_MISSING") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();
        string file = Path.Combine(Environment.GetEnvironmentVariable("CONNAPSE_SMOKE_HOME")!, "smoke.txt");
        await File.WriteAllTextAsync(file, "The smoke test checks that the CLI works against this server.");

        var version = await Ok("version");
        Assert.Contains("server http://", version.Out);

        var whoami = await Ok("auth", "whoami");
        Assert.Contains("Connected", whoami.Out);

        await Ok("container", "create", container);
        try
        {
            Assert.Contains(container, (await Ok("container", "list")).Out);
            await Ok("container", "stats", container);
            await Ok("upload", file, "--container", container);
            Assert.Contains("smoke.txt", (await Ok("files", "list", "--container", container)).Out);
            await Ok("search", "smoke", "--container", container, "--mode", "Keyword");
            await Ok("settings", "get", "chunking");
            await Ok("agent", "list");
            await Ok("auth", "pat", "list");

            await OkUnlessMissing("sources", missing, "source", "list");
            await OkUnlessMissing("connections", missing, "connection", "list");
        }
        finally
        {
            await Cli("container", "delete", container);
        }
    }

    /// <summary>A command that must work on every supported server.</summary>
    private static async Task<Run> Ok(params string[] args)
    {
        var run = await Cli(args);
        Assert.True(run.Exit == 0, $"connapse {string.Join(' ', args)} exited {run.Exit}: {run.Err}{run.Out}");
        return run;
    }

    /// <summary>
    /// A command that works when the server has <paramref name="feature"/>, and otherwise says so by
    /// name. Expected-missing features must fail that way, so a server that gained one is noticed.
    /// </summary>
    private static async Task OkUnlessMissing(string feature, HashSet<string> missing, params string[] args)
    {
        var run = await Cli(args);
        string command = $"connapse {string.Join(' ', args)}";
        if (!missing.Contains(feature))
        {
            Assert.True(run.Exit == 0, $"{command} exited {run.Exit}: {run.Err}{run.Out}");
            return;
        }

        Assert.Equal(1, run.Exit);
        Assert.Contains($"This server doesn't support {feature}", run.Err);
        Assert.Contains("Upgrade the server", run.Err);
        Assert.DoesNotContain("NotFound", run.Err);
        Assert.DoesNotContain("Unexpected error", run.Err);
    }

    private static async Task<Run> Cli(params string[] args)
    {
        string home = Environment.GetEnvironmentVariable("CONNAPSE_SMOKE_HOME")!;
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(Environment.GetEnvironmentVariable("CONNAPSE_SMOKE_CLI")!);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["HOME"] = home;
        psi.Environment["USERPROFILE"] = home;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return new Run(process.ExitCode, await stdout, await stderr);
    }
}
