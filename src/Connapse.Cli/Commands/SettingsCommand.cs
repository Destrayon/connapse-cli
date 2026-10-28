using System.CommandLine;
using Connapse.Cli.Commands.Settings;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for server settings under <c>connapse settings</c>. Every subcommand needs the
/// Owner or Admin role, and a token needs <c>admin:settings</c>.
/// </summary>
public static class SettingsCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var settings = new Command("settings", "Manage server settings: embedding, chunking, search, LLM and upload.");

        settings.Subcommands.Add(SettingsGetCommand.Create(services, orgOption));
        settings.Subcommands.Add(SettingsSetCommand.Create(services, orgOption));
        settings.Subcommands.Add(SettingsSetCommand.CreateReset(services, orgOption));
        settings.Subcommands.Add(SettingsTestCommand.Create(services, orgOption));
        settings.Subcommands.Add(SettingsReindexCommand.Create(services, orgOption));
        settings.Subcommands.Add(SettingsReindexCommand.CreateStatus(services, orgOption));
        settings.Subcommands.Add(SettingsReindexCommand.CreateEmbeddingModels(services, orgOption));
        settings.Subcommands.Add(SettingsReindexCommand.CreateClearSummaries(services, orgOption));

        settings.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse settings <get|set|reset|test|reindex|reindex-status|embedding-models|clear-summaries> [options]");
            return Task.FromResult(0);
        });

        return settings;
    }
}
