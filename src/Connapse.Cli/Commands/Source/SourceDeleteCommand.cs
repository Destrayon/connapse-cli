using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Source;

public static class SourceDeleteCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var sourceArg = SourceHelpers.SourceArgument();

        var cmd = new Command("delete", "Delete a source and its indexed documents. The data it read from is untouched.");
        cmd.Arguments.Add(sourceArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await SourceHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(sourceArg), ct);

            // The server deletes only when told the name, so the CLI reads it back and sends it as ConfirmName; there is no --yes flag.
            var source = await client.Api.Sources[id].GetAsync(cancellationToken: ct)
                ?? throw new CliServerError("The server returned no source.");

            await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.Sources[id].DeleteAsync(
                r => r.QueryParameters.ConfirmName = source.Name, ct));

            AdminHelpers.WriteSuccess($"Deleted source '{source.Name}'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
