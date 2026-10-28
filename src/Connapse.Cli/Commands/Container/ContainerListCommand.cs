using System.CommandLine;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Container;

public static class ContainerListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("list", "List knowledge containers.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();

            var containers = await Paging.ReadAllAsync<Connapse.Client.Models.Container>(async (skip, take) =>
            {
                var page = await client.Api.Containers.GetAsync(r =>
                {
                    r.QueryParameters.Skip = skip;
                    r.QueryParameters.Take = take;
                }, ct);
                return (page?.Items ?? [], page?.HasMore ?? false);
            });

            if (containers.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No containers found.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Found {containers.Count} container(s)");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var c in containers)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {c.Name}");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  ({c.DocumentCount} files)");
                if (!string.IsNullOrEmpty(c.Description))
                    Console.Write($"  — {c.Description}");
                Console.WriteLine();
                Console.ResetColor();
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
