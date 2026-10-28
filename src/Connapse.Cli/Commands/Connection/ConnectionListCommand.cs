using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var providerOption = new Option<string?>("--provider")
        {
            Description = $"Only this provider's connections: {string.Join(", ", AdminHelpers.EnumNames<ConnectionProvider>())}.",
        };
        var skipOption = new Option<int>("--skip") { Description = "Connections to skip.", DefaultValueFactory = _ => 0 };
        var takeOption = new Option<int>("--take") { Description = "Connections to show (1-100).", DefaultValueFactory = _ => 50 };
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON." };

        var cmd = new Command("list", "List connections.");
        cmd.Options.Add(providerOption);
        cmd.Options.Add(skipOption);
        cmd.Options.Add(takeOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var provider = parseResult.GetValue(providerOption) is { } p
                ? AdminHelpers.ParseEnum<ConnectionProvider>(p, "--provider")
                : (ConnectionProvider?)null;
            var client = services.GetRequiredService<ConnapseApiClient>();

            var page = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections.GetAsync(r =>
            {
                r.QueryParameters.Skip = parseResult.GetValue(skipOption);
                r.QueryParameters.Take = parseResult.GetValue(takeOption);
                r.QueryParameters.Provider = provider;
            }, ct)) ?? throw new CliServerError("The server returned no connections page.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(page));
                return CliExitCodes.Success;
            }

            var items = page.Items ?? [];
            if (items.Count == 0)
            {
                Console.WriteLine("No connections.");
                return CliExitCodes.Success;
            }

            Console.WriteLine($"{"NAME",-30} {"PROVIDER",-10} {"SOURCES",7} {"SYNC",-7} ID");
            foreach (var c in items)
                Console.WriteLine($"{c.Name,-30} {c.Provider,-10} {c.SourceCount ?? 0,7} {(c.SyncPaused == true ? "paused" : "running"),-7} {c.Id}");
            if (page.HasMore == true)
                Console.WriteLine($"Showing {items.Count} of {page.TotalCount}; use --skip to see more.");

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
