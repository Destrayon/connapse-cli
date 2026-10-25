using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Connapse.Cli.Tests.Server;

/// <summary>
/// Runs only when tests/server/run-route-test.sh has started a real server and signed in; skipped
/// otherwise, so ordinary CI stays fast.
/// </summary>
public sealed class ServerRoutesFactAttribute : FactAttribute
{
    public ServerRoutesFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CONNAPSE_ROUTE_TEST_HOME") is null)
            Skip = "Needs a running server: tests/server/run-route-test.sh";
    }
}

/// <summary>
/// Every route in the server's capability list (CON-21), sent through the real <c>connapse api</c>
/// binary as the seeded admin. A route counts as reached when the server answers it differently from
/// a route that doesn't exist: each method is first sent to a made-up path, and a reply with that
/// probe's status, content type and body (or a 405) means routing found nothing. A 400, 403 or a
/// "not found" about the made-up id still proves the route is there.
/// </summary>
[Trait("Category", "ServerRoutes")]
public sealed class CapabilityRoutesTests
{

    private sealed record Reply(int Exit, string Status, string Body);

    [ServerRoutesFact]
    public async Task Every_capability_route_is_reached_through_connapse_api()
    {
        string home = Environment.GetEnvironmentVariable("CONNAPSE_ROUTE_TEST_HOME")!;
        string cli = Environment.GetEnvironmentVariable("CONNAPSE_ROUTE_TEST_CLI")!;
        string listPath = Environment.GetEnvironmentVariable("CONNAPSE_ROUTE_TEST_CAPABILITIES")!;
        using var list = JsonDocument.Parse(await File.ReadAllTextAsync(listPath));
        var routes = list.RootElement.GetProperty("capabilities").EnumerateArray()
            .SelectMany(c => c.GetProperty("routes").EnumerateArray().Select(r => (Id: c.GetProperty("id").GetString()!, Route: r.GetString()!)))
            .ToList();
        Assert.NotEmpty(routes);

        // Routes the server lists as not built yet (its own known-capability-gaps.json, beside the list)
        // aren't asked for: the server's CapabilityManifestTests already holds that list to the code.
        using var gaps = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(listPath)!, "known-capability-gaps.json")));
        var unbuilt = gaps.RootElement.EnumerateObject().Select(g => g.Name).Where(n => n.StartsWith("route ", StringComparison.Ordinal)).ToHashSet();
        routes = routes.Where(r => !unbuilt.Contains($"route {r.Id}: missing {r.Route}")).ToList();
        Assert.NotEmpty(routes);

        const string probePath = "/api/cli-route-test-no-such-route";
        var probes = new Dictionary<string, Reply>();

        async Task<(bool Reached, Reply Reply)> Reach(string method, string path)
        {
            if (!probes.TryGetValue(method, out var probe))
                probes[method] = probe = await Send(cli, home, method, probePath);

            var reply = await Send(cli, home, method, path);
            bool unrouted = reply.Status.StartsWith("HTTP 405", StringComparison.Ordinal)
                || (reply.Status == probe.Status && Normalize(reply.Body, path) == Normalize(probe.Body, probePath));
            // A route's own bare "not found" (no such id) reads like routing's. Routing answers a path it
            // knows with 405 for a method it doesn't map, and this method would itself have had that 405
            // if it weren't mapped, so a 405 to OPTIONS shows this route is there.
            if (unrouted && !reply.Status.StartsWith("HTTP 405", StringComparison.Ordinal))
                unrouted = !(await Send(cli, home, "OPTIONS", path)).Status.StartsWith("HTTP 405", StringComparison.Ordinal);
            // A 401 means the sign-in, not the route, failed, and a CLI that didn't run reached nothing.
            bool failed = reply.Status.StartsWith("HTTP 401", StringComparison.Ordinal) || reply.Status.StartsWith("cli failed", StringComparison.Ordinal);
            return (!unrouted && !failed, reply);
        }

        // Canaries the rule must reject on every run: a made-up path, and a real path (/health is GET
        // only) asked with a method it doesn't map. If either counts as reached, the rule proves nothing.
        foreach (var (method, path) in new[] { ("GET", probePath + "/" + Guid.NewGuid()), ("PATCH", "/health") })
            Assert.False((await Reach(method, path)).Reached, $"the canary {method} {path} counted as reached");

        var missed = new List<string>();
        foreach (var (id, route) in routes)
        {
            var (reached, reply) = await Reach(route[..route.IndexOf(' ')], Fill(route[(route.IndexOf(' ') + 1)..]));
            if (!reached)
                missed.Add($"{id}: {route} -> {reply.Status}");
        }
        Assert.True(probes.Values.All(p => p.Status.StartsWith("HTTP 4", StringComparison.Ordinal)),
            "a made-up route didn't answer 4xx, so 'reached' can't be told apart: " + string.Join("; ", probes.Select(p => $"{p.Key} {p.Value.Status}")));

        Assert.True(missed.Count == 0, $"{missed.Count} of {routes.Count} routes weren't reached:\n" + string.Join("\n", missed));
    }

    /// <summary>A reply without what changes per request: the path it echoes and any trace id.</summary>
    private static string Normalize(string body, string path) =>
        Regex.Replace(body.Replace(path, ""), "\"traceId\"\\s*:\\s*\"[^\"]*\"", "");

    /// <summary>A path for a route template: a fixed GUID, number or word for each parameter.</summary>
    private static string Fill(string template) => Regex.Replace(template, @"\{\*?(\w+)(?::(\w+))?\??\}", m => m.Groups[2].Value switch
    {
        "guid" => "00000000-0000-0000-0000-00000000c121",
        "int" or "long" => "1",
        _ => "cli-route-test",
    });

    private static async Task<Reply> Send(string cli, string home, string method, string path)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (string arg in new[] { cli, "api", method, path })
            start.ArgumentList.Add(arg);
        if (method is "POST" or "PUT" or "PATCH")
        {
            start.ArgumentList.Add("--data");
            start.ArgumentList.Add("{}");
        }
        start.Environment["USERPROFILE"] = home;
        start.Environment["HOME"] = home;

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string errors = await stderr;
        string status = errors.Split('\n').FirstOrDefault(l => l.StartsWith("HTTP ", StringComparison.Ordinal))?.Trim()
            ?? (process.ExitCode == 0 ? "2xx" : $"cli failed ({process.ExitCode}): {errors.Trim()}");
        return new Reply(process.ExitCode, status, await stdout);
    }
}
