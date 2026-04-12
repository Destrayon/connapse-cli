using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Container;

public static class ContainerListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("list", "List knowledge containers.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var apiClient = services.GetRequiredService<ApiClient>();

            var paged = await apiClient.GetAsync<PagedContainerResponse>(
                "/api/containers?skip=0&take=100", ct);

            var containers = paged?.Items;

            if (containers is null || containers.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No containers found.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(paged!.HasMore
                ? $"Showing {containers.Count} of {paged.TotalCount} container(s)"
                : $"Found {containers.Count} container(s)");
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
