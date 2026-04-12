using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Org;

public static class OrgListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("list", "List organizations you belong to.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var (skip, _) = await OrgHelpers.CheckEditionAsync(services, ct);
            if (skip) return CliExitCodes.Success;

            var apiClient = services.GetRequiredService<ApiClient>();
            var configStore = services.GetRequiredService<ConfigStore>();

            var orgs = await apiClient.GetAsync<List<Organization>>("/api/v1/organizations", ct);

            if (orgs is null || orgs.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("You are not a member of any organizations.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            var currentSlug = configStore.Load().DefaultOrgSlug;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Organizations ({orgs.Count}):");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var org in orgs.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isCurrent = string.Equals(org.Slug, currentSlug, StringComparison.OrdinalIgnoreCase);

                if (isCurrent)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  * ");
                }
                else
                {
                    Console.Write("    ");
                }
                Console.Write(org.Name);
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  ({org.Slug})");
                Console.ResetColor();
            }

            if (currentSlug is not null)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  * = current default");
                Console.ResetColor();
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
