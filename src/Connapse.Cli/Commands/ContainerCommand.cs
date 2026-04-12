using System.CommandLine;
using Connapse.Cli.Commands.Container;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for container operations under <c>connapse container</c>.
/// </summary>
public static class ContainerCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var container = new Command("container", "Manage knowledge containers.");

        container.Subcommands.Add(ContainerCreateCommand.Create(services, orgOption));
        container.Subcommands.Add(ContainerListCommand.Create(services, orgOption));
        container.Subcommands.Add(ContainerDeleteCommand.Create(services, orgOption));
        container.Subcommands.Add(ContainerStatsCommand.Create(services, orgOption));

        container.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse container <create|list|delete|stats> [options]");
            return Task.FromResult(0);
        });

        return container;
    }
}
