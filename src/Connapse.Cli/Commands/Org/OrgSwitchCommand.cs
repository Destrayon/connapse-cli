using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Org;

public static class OrgSwitchCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var slugArg = new Argument<string>("slug")
        {
            Description = "The organization slug to switch to.",
        };

        var cmd = new Command("switch", "Set the default organization for subsequent commands.");
        cmd.Arguments.Add(slugArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var (skip, _) = await OrgHelpers.CheckEditionAsync(services, ct);
            if (skip) return CliExitCodes.UserError;

            var requestedSlug = parseResult.GetRequiredValue(slugArg);

            var apiClient = services.GetRequiredService<ApiClient>();
            var configStore = services.GetRequiredService<ConfigStore>();

            var orgs = await apiClient.GetAsync<List<Organization>>("/api/v1/organizations", ct);

            var match = orgs?.FirstOrDefault(o =>
                string.Equals(o.Slug, requestedSlug, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                throw new CliValidationError(
                    $"You are not a member of '{requestedSlug}'. " +
                    "Run 'connapse org list' to see organizations you belong to.");
            }

            configStore.SetDefaultOrg(match.Slug);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Switched to {match.Name} ({match.Slug}).");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
