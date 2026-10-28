using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Source;

public static class SourceListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var skipOption = new Option<int>("--skip") { Description = "Sources to skip.", DefaultValueFactory = _ => 0 };
        var takeOption = new Option<int>("--take") { Description = "Sources to show (1-100).", DefaultValueFactory = _ => 50 };
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON." };

        var cmd = new Command("list", "List sources and their sync state.");
        cmd.Options.Add(skipOption);
        cmd.Options.Add(takeOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var page = await client.Api.Sources.GetAsync(r =>
            {
                r.QueryParameters.Skip = parseResult.GetValue(skipOption);
                r.QueryParameters.Take = parseResult.GetValue(takeOption);
            }, ct) ?? throw new CliServerError("The server returned no sources page.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(page));
                return CliExitCodes.Success;
            }

            var items = page.Items ?? [];
            if (items.Count == 0)
            {
                Console.WriteLine("No sources.");
                return CliExitCodes.Success;
            }

            Console.WriteLine($"{"NAME",-30} {"KIND",-12} {"DOCS",6} {"LAST SYNC",-10} ID");
            foreach (var s in items)
                Console.WriteLine($"{s.Name,-30} {s.Kind,-12} {s.DocumentCount ?? 0,6} {s.LastSyncStatus,-10} {s.Id}");
            if (page.HasMore == true)
                Console.WriteLine($"Showing {items.Count} of {page.TotalCount}; use --skip to see more.");

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
