using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionGetCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON, with the connection's config." };

        var cmd = new Command("get", "Show a connection.");
        cmd.Arguments.Add(connectionArg);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var connection = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.V1.Connections[id].GetAsync(cancellationToken: ct))
                ?? throw new CliServerError("The server returned no connection.");

            if (parseResult.GetValue(jsonOption))
                Console.WriteLine(AdminHelpers.ToJson(connection));
            else
                ConnectionHelpers.Print(connection);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
