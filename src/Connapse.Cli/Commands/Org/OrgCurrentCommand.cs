using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Org;

public static class OrgCurrentCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("current", "Show the current default organization.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var (skip, _) = await OrgHelpers.CheckEditionAsync(services, ct);
            if (skip) return CliExitCodes.Success;

            var configStore = services.GetRequiredService<ConfigStore>();
            var config = configStore.Load();

            if (string.IsNullOrWhiteSpace(config.DefaultOrgSlug))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No default organization set.");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Run 'connapse org list' to see available organizations,");
                Console.WriteLine("  then 'connapse org switch <slug>' to set a default.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            Console.WriteLine(config.DefaultOrgSlug);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
