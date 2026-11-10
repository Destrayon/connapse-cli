using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

/// <summary>
/// <c>connapse setup claude-code</c>: prints the Claude Code auto mode settings that tell its classifier the signed-in
/// Connapse server is the user's own service and <c>connapse</c> is the user's own CLI (CC-10). Without them the
/// classifier can take a container create or an upload for a change to shared infrastructure and deny it. It prints
/// rather than writes, because they go in the user's own Claude Code settings, which are the user's to change.
/// </summary>
public static class SetupCommand
{
    public static Command Create(IServiceProvider services)
    {
        var setup = new Command("setup", "Set up tools that use the Connapse CLI.");
        setup.Subcommands.Add(ClaudeCode(services));
        setup.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse setup claude-code");
            return Task.FromResult(0);
        });
        return setup;
    }

    private static Command ClaudeCode(IServiceProvider services)
    {
        var cmd = new Command("claude-code",
            "Print Claude Code auto mode settings that let agents use this CLI against the signed-in server. "
            + "Merge them into ~/.claude/settings.json.");

        cmd.SetAction((_, _) =>
        {
            string server = services.GetRequiredService<CredentialStore>().Load()?.ApiBaseUrl?.TrimEnd('/')
                ?? throw new CliAuthError("Not signed in, so there is no server to describe.");

            Console.Out.WriteLine(Settings(server).ToJsonString(JsonOptions));

            Console.Error.WriteLine();
            Console.Error.WriteLine("Merge this into your user settings, ~/.claude/settings.json; Claude Code ignores autoMode in a");
            Console.Error.WriteLine("project's settings. '$defaults' keeps the built-in rules. 'claude auto-mode config' shows the result.");
            Console.Error.WriteLine("The allow rule trusts CLI deletes because the server holds them for your approval. A personal access");
            Console.Error.WriteLine("token made with approval turned off is not held, so sign the CLI in with one that asks for approval.");
            return Task.FromResult(CliExitCodes.Success);
        });

        return cmd;
    }

    internal static JsonObject Settings(string server) => new()
    {
        ["autoMode"] = new JsonObject
        {
            ["environment"] = new JsonArray(
                "$defaults",
                $"Key internal services: the Connapse server at {server} is the user's own self-hosted knowledge base. "
                    + "Its containers and files belong to the user, and the user's agents routinely create containers and upload, "
                    + "update, list and search files there.",
                "Org-specific CLIs: `connapse` is the user's own CLI for that Connapse server. Its deletes (`connapse files delete`, "
                    + "`connapse container delete`, `connapse memory forget`) are held by the server until the user approves them in "
                    + "its web UI; a held delete exits with code 3 and changes nothing."),
            ["allow"] = new JsonArray(
                "$defaults",
                $"Connapse CLI: running `connapse` against the user's own server at {server} to check the sign-in, create containers, "
                    + "upload, list, get, search, update and delete files, and save, recall and forget memory facts is allowed, also when "
                    + "another agent asked for it, because deletes wait for the user's approval on the server. This does not cover "
                    + "`connapse api` calls that change data, `connapse auth`, `connapse agent`, `connapse settings`, `connapse org` or "
                    + "`connapse connection`, which stay under the usual rules."),
        },
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
