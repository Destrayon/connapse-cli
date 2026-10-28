using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Source;

/// <summary><c>source get</c> and <c>source stats</c>.</summary>
public static class SourceGetCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var sourceArg = SourceHelpers.SourceArgument();
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON." };

        var cmd = new Command("get", "Show a source's settings and sync state.");
        cmd.Arguments.Add(sourceArg);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await SourceHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(sourceArg), ct);

            var source = await client.Api.Sources[id].GetAsync(cancellationToken: ct)
                ?? throw new CliServerError("The server returned no source.");

            if (parseResult.GetValue(jsonOption))
                Console.WriteLine(AdminHelpers.ToJson(source));
            else
                SourceHelpers.Print(source);
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateStats(IServiceProvider services, Option<string?> orgOption)
    {
        var sourceArg = SourceHelpers.SourceArgument();

        var cmd = new Command("stats", "Show a source's document counts, chunks and size.");
        cmd.Arguments.Add(sourceArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await SourceHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(sourceArg), ct);

            var stats = await client.Api.Sources[id].Stats.GetAsync(cancellationToken: ct)
                ?? throw new CliServerError("The server returned no stats.");

            var docs = stats.Documents;
            Console.WriteLine($"Source: {stats.SourceName}");
            Console.WriteLine($"  Documents:     {docs?.Total ?? 0} ({docs?.Ready ?? 0} ready, {docs?.Processing ?? 0} processing, {docs?.Failed ?? 0} failed)");
            Console.WriteLine($"  Chunks:        {stats.TotalChunks ?? 0}");
            Console.WriteLine($"  Size:          {ContainerHelpers.FormatBytes(stats.TotalSizeBytes ?? 0)}");
            Console.WriteLine($"  Last indexed:  {AdminHelpers.Date(stats.LastIndexedAt)}");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
