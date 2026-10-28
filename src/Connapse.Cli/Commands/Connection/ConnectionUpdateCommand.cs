using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionUpdateCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();
        var nameOption = new Option<string?>("--name") { Description = "A new name." };
        var configOptions = new JsonObjectOptions("config", "New provider settings.");
        var secrets = new SecretOptions();

        var cmd = new Command("update", "Rename a connection, change its settings, or give it a new secret.");
        cmd.Arguments.Add(connectionArg);
        cmd.Options.Add(nameOption);
        configOptions.AddTo(cmd);
        secrets.AddTo(cmd);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameOption);
            var config = configOptions.Read(parseResult);
            var (token, privateKey, passphrase) = secrets.Read(parseResult);
            if (name is null && config is null && token is null && privateKey is null && passphrase is null)
                throw new CliValidationError("Nothing to update; pass --name, --config or a secret.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var updated = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections[id].PatchAsync(
                new UpdateConnectionApiRequest
                {
                    Name = name,
                    Config = config,
                    Token = token,
                    PrivateKey = privateKey,
                    Passphrase = passphrase,
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no connection.");

            AdminHelpers.WriteSuccess($"Updated connection '{updated.Name}'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
