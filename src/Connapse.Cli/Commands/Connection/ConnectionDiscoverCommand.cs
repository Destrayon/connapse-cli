using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionDiscoverCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();
        var personalOption = new Option<bool>("--include-personal")
        {
            Description = "Atlassian: include personal spaces.",
        };
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON." };

        var cmd = new Command("discover", "List what a connection can reach, to pick a source's scope from.");
        cmd.Arguments.Add(connectionArg);
        cmd.Options.Add(personalOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var found = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections[id].Discover.GetAsync(
                r => r.QueryParameters.IncludePersonal = parseResult.GetValue(personalOption) ? true : null, ct))
                ?? throw new CliServerError("The server returned no discovery result.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(found));
                return Exit(found);
            }

            Console.WriteLine($"Outcome: {found.Outcome}");
            if (!string.IsNullOrEmpty(found.Message))
                Console.WriteLine(found.Message);

            Section("Buckets", found.Buckets);
            Section("Containers", found.Containers);
            Section("Storage accounts", found.StorageAccounts?.Select(a => $"{a.Name} ({a.ResourceGroup})"));
            Section("Installations", found.Installations?.Select(i => $"{i.AccountLogin} ({i.Id})"));
            Section("Spaces", found.Spaces?.Select(s => $"{s.Key}  {s.Name}"));
            Section("Projects", found.Projects?.Select(p => $"{p.Key}  {p.Name}"));
            Section("Custom providers", found.CustomProviders?.Select(p => $"{p.Name}  {p.BaseUrl} ({p.Id})"));
            if (!string.IsNullOrEmpty(found.InstallUrl))
                Console.WriteLine($"Install: {found.InstallUrl}");
            if (!string.IsNullOrEmpty(found.TroubleshootingUrl))
                Console.WriteLine($"Troubleshooting: {found.TroubleshootingUrl}");
            return Exit(found);
        }));

        return cmd;
    }

    /// <summary>Only a listing is a success; NotConfigured, Denied and Failed exit 1 so scripts don't read them as empty.</summary>
    private static int Exit(Connapse.Client.Models.ConnectionDiscovery found) =>
        found.Outcome == Connapse.Client.Models.DiscoveryOutcome.Listed ? CliExitCodes.Success : CliExitCodes.UserError;

    private static void Section(string title, IEnumerable<string?>? items)
    {
        var list = items?.Where(i => !string.IsNullOrEmpty(i)).ToList();
        if (list is null || list.Count == 0)
            return;
        Console.WriteLine($"{title}:");
        foreach (var item in list)
            Console.WriteLine($"  {item}");
    }
}
