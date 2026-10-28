using System.CommandLine;
using Connapse.Cli.Commands.Source;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for source operations under <c>connapse source</c>. Reading needs
/// <c>knowledge:read</c>; changing a source needs the Owner or Admin role and <c>admin:sources</c>.
/// </summary>
public static class SourceCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var source = new Command("source", "Manage sources: what each connection indexes.");

        source.Subcommands.Add(SourceListCommand.Create(services, orgOption));
        source.Subcommands.Add(SourceGetCommand.Create(services, orgOption));
        source.Subcommands.Add(SourceGetCommand.CreateStats(services, orgOption));
        source.Subcommands.Add(SourceCreateCommand.Create(services, orgOption));
        source.Subcommands.Add(SourceUpdateCommand.Create(services, orgOption));
        source.Subcommands.Add(SourceDeleteCommand.Create(services, orgOption));
        source.Subcommands.Add(SourceSyncCommand.Create(services, orgOption));
        source.Subcommands.Add(SourceSyncCommand.CreateRetryFailed(services, orgOption));
        source.Subcommands.Add(SourceScopeSchemaCommand.Create(services, orgOption));

        source.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse source <list|get|stats|create|update|delete|sync|retry-failed|scope-schema> [options]");
            return Task.FromResult(0);
        });

        return source;
    }
}
