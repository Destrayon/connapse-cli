using System.CommandLine;
using Connapse.Cli.Commands.Auth;

namespace Connapse.Cli.Commands;

/// <summary>
/// Central place where the CLI's command tree is assembled. Each Phase 5
/// task adds its subcommand here. The builder takes an
/// <see cref="IServiceProvider"/> so leaf command factories can resolve
/// their dependencies.
/// </summary>
public static class RootCommandBuilder
{
    public static RootCommand Build(
        IServiceProvider services,
        Option<string?> orgOption,
        Option<bool> verboseOption)
    {
        var root = new RootCommand("Connapse CLI — knowledge management from the terminal.");
        root.Options.Add(orgOption);
        root.Options.Add(verboseOption);

        // Default action: print help when invoked with no subcommand.
        root.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Connapse CLI — run 'connapse --help' to see available commands.");
            return Task.FromResult(0);
        });

        root.Subcommands.Add(AuthCommand.Create(services, orgOption));
        root.Subcommands.Add(OrgCommand.Create(services, orgOption));
        root.Subcommands.Add(ContainerCommand.Create(services, orgOption));
        root.Subcommands.Add(FilesCommand.Create(services, orgOption));
        root.Subcommands.Add(UploadCommand.Create(services, orgOption));

        // Phase 5 tasks add subcommands here:
        // root.Subcommands.Add(SearchCommand.Create(services, orgOption));
        // root.Subcommands.Add(ReindexCommand.Create(services, orgOption));
        // root.Subcommands.Add(VersionCommand.Create(services));
        // root.Subcommands.Add(UpdateCommand.Create(services));

        return root;
    }
}
