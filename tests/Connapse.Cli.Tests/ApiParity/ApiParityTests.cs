using System.CommandLine;
using System.Text.Json;
using Connapse.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Tests.ApiParity;

/// <summary>
/// CC-13: the CLI keeps up with the API. Every route in spec/openapi.json either has the CLI command
/// that calls it in cli-routes.json, or a reason it has none: under notForCli when it is never a CLI
/// job, or in known-cli-gaps.json when the command is still missing. Modelled on the server's
/// capability manifest test (Destrayon/Connapse #791). A ticket that adds a missing command moves its
/// routes from known-cli-gaps.json to commands.
/// </summary>
public class ApiParityTests
{
    [Fact]
    public void EveryApiRoute_HasACliCommandOrAListedReason()
    {
        var problems = ApiParity.Check(ApiParity.SpecRoutes(File.ReadAllText(ApiParity.PathOf("spec", "openapi.json"))),
            ApiParity.LoadManifest(), ApiParity.LoadKnownGaps(), ApiParity.CommandPaths());

        if (problems.Count > 0)
            Assert.Fail(string.Join("\n", problems));
    }

    [Fact]
    public void ARouteAddedToTheSpec_WithNoCommandOrReason_FailsTheCheck()
    {
        var spec = JsonDocument.Parse(File.ReadAllText(ApiParity.PathOf("spec", "openapi.json"))).RootElement;
        var paths = spec.GetProperty("paths").EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);
        paths["/api/v1/widgets/{id}"] = new { post = new { operationId = "widgets.create" } };
        string withFakeRoute = JsonSerializer.Serialize(new { openapi = "3.0.1", paths });

        var problems = ApiParity.Check(ApiParity.SpecRoutes(withFakeRoute),
            ApiParity.LoadManifest(), ApiParity.LoadKnownGaps(), ApiParity.CommandPaths());

        var problem = Assert.Single(problems);
        Assert.Contains("POST /api/v1/widgets/{id}", problem);
        Assert.Contains("no CLI command", problem);
    }

    [Fact]
    public void AListedCommandThatDoesNotExist_FailsTheCheck()
    {
        var manifest = new ApiParity.Manifest(
            new Dictionary<string, string> { ["GET /api/x"] = "x list", ["GET /api/y"] = "api" },
            new Dictionary<string, string>());

        var problems = ApiParity.Check(["GET /api/x", "GET /api/y"], manifest, new Dictionary<string, string>(), ["api", "y list"]);

        Assert.Contains(problems, p => p.Contains("GET /api/x") && p.Contains("'connapse x list'"));
        Assert.Contains(problems, p => p.Contains("GET /api/y") && p.Contains("connapse api"));
    }

    [Fact]
    public void AStaleDuplicatedOrUnexplainedEntry_FailsTheCheck()
    {
        var manifest = new ApiParity.Manifest(
            new Dictionary<string, string> { ["GET /api/x"] = "x list", ["GET /api/gone"] = "x list" },
            new Dictionary<string, string> { ["GET /health"] = " " });
        var gaps = new Dictionary<string, string> { ["GET /api/x"] = "No command yet." };

        var problems = ApiParity.Check(["GET /api/x", "GET /health"], manifest, gaps, ["x list"]);

        Assert.Contains(problems, p => p.Contains("GET /api/gone") && p.Contains("not in the API spec"));
        Assert.Contains(problems, p => p.Contains("GET /api/x") && p.Contains("more than one"));
        Assert.Contains(problems, p => p.Contains("GET /health") && p.Contains("no reason"));
    }
}

/// <summary>Reads the API spec, the CLI's route manifest and its command tree, and compares them.</summary>
public static class ApiParity
{
    public sealed record Manifest(IReadOnlyDictionary<string, string> Commands, IReadOnlyDictionary<string, string> NotForCli);

    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete"];

    /// <summary>Every operation in an OpenAPI document, as "METHOD /path" with the path exactly as the spec writes it.</summary>
    public static IReadOnlyList<string> SpecRoutes(string openApiJson)
    {
        using var doc = JsonDocument.Parse(openApiJson);
        return doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(op => Methods.Contains(op.Name, StringComparer.OrdinalIgnoreCase))
                .Select(op => $"{op.Name.ToUpperInvariant()} {path.Name}"))
            .ToList();
    }

    /// <summary>
    /// What is wrong between the spec and the lists: a route in none of them, a route in more than
    /// one, an entry for a route the spec no longer has, a reason left empty, or a command that
    /// isn't in the CLI. Empty when the CLI and the API agree.
    /// </summary>
    public static IReadOnlyList<string> Check(
        IReadOnlyCollection<string> specRoutes, Manifest manifest, IReadOnlyDictionary<string, string> knownGaps,
        IReadOnlyCollection<string> commandPaths)
    {
        var problems = new List<string>();
        var spec = specRoutes.ToHashSet(StringComparer.Ordinal);
        var commands = commandPaths.ToHashSet(StringComparer.Ordinal);
        var lists = new (string Name, IReadOnlyDictionary<string, string> Entries)[]
        {
            ("commands in cli-routes.json", manifest.Commands),
            ("notForCli in cli-routes.json", manifest.NotForCli),
            ("known-cli-gaps.json", knownGaps),
        };

        foreach (string route in spec.Order(StringComparer.Ordinal))
        {
            var listedIn = lists.Where(l => l.Entries.ContainsKey(route)).Select(l => l.Name).ToList();
            if (listedIn.Count == 0)
                problems.Add($"{route}: no CLI command and no reason. Add a command and list it under commands in cli-routes.json, " +
                             "or say why there is none under notForCli (never a CLI job) or in known-cli-gaps.json (not written yet).");
            else if (listedIn.Count > 1)
                problems.Add($"{route}: listed in more than one place ({string.Join(", ", listedIn)}); keep one.");
        }

        foreach (var (name, entries) in lists)
            foreach (string route in entries.Keys.Where(r => !spec.Contains(r)).Order(StringComparer.Ordinal))
                problems.Add($"{route}: listed in {name} but not in the API spec. Delete the entry, or fix it to match the spec's route.");

        foreach (var (name, entries) in lists.Skip(1))
            foreach (var (route, reason) in entries.Where(e => string.IsNullOrWhiteSpace(e.Value)))
                problems.Add($"{route}: listed in {name} with no reason. Say why the CLI has no command for it.");

        foreach (var (route, command) in manifest.Commands)
        {
            if (command == "api")
                problems.Add($"{route}: 'connapse api' reaches every route, so it doesn't count as this route's command. " +
                             "Name a real command, or list the route in known-cli-gaps.json.");
            else if (!commands.Contains(command))
                problems.Add($"{route}: cli-routes.json names 'connapse {command}', which the CLI doesn't have.");
        }

        return problems;
    }

    /// <summary>Every command in the CLI's real tree, as its space-separated path without "connapse".</summary>
    public static IReadOnlyList<string> CommandPaths()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var root = RootCommandBuilder.Build(services, new Option<string?>("--org"), new Option<bool>("--verbose"));
        var paths = new List<string>();
        void Walk(Command command, string prefix)
        {
            foreach (var sub in command.Subcommands)
            {
                string path = prefix.Length == 0 ? sub.Name : $"{prefix} {sub.Name}";
                paths.Add(path);
                Walk(sub, path);
            }
        }
        Walk(root, "");
        return paths;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false,
    };

    public static Manifest LoadManifest() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(PathOf("tests", "Connapse.Cli.Tests", "ApiParity", "cli-routes.json")), Json)!;

    public static IReadOnlyDictionary<string, string> LoadKnownGaps() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PathOf("tests", "Connapse.Cli.Tests", "ApiParity", "known-cli-gaps.json")), Json)!;

    /// <summary>A file in the repository, found from the test output by walking up to Connapse.Cli.slnx.</summary>
    public static string PathOf(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Connapse.Cli.slnx")))
                return Path.Combine([dir.FullName, .. parts]);
        throw new InvalidOperationException("Connapse.Cli.slnx not found above the test output.");
    }
}
