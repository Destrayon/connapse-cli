using System.CommandLine;
using Connapse.Cli.Commands.Files;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for file operations under <c>connapse files</c>.
/// </summary>
public static class FilesCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var files = new Command("files", "Browse and manage files inside a container.");

        files.Subcommands.Add(FilesListCommand.Create(services, orgOption));
        files.Subcommands.Add(FilesDeleteCommand.Create(services, orgOption));
        files.Subcommands.Add(FilesGetCommand.Create(services, orgOption));

        files.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse files <list|delete|get> [options]");
            return Task.FromResult(0);
        });

        return files;
    }
}
