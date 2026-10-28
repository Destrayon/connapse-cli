using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

/// <summary><c>connection replace-secret</c> and <c>connection forget-host-key</c>.</summary>
public static class ConnectionSecretCommands
{
    public static Command CreateReplaceSecret(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();
        var secrets = new SecretOptions();

        var cmd = new Command("replace-secret", "Replace a connection's stored secret. Only an Owner may.");
        cmd.Arguments.Add(connectionArg);
        secrets.AddTo(cmd);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var (token, privateKey, passphrase) = secrets.Read(parseResult);
            if (token is null && privateKey is null)
                throw new CliValidationError("Pass the new secret with --token, --token-stdin or --private-key-file.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var updated = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections[id].Secret.PutAsync(
                new ReplaceConnectionSecretApiRequest { Token = token, PrivateKey = privateKey, Passphrase = passphrase },
                cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Replaced the secret for '{updated?.Name}'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateForgetHostKey(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();

        var cmd = new Command("forget-host-key", "SFTP: forget the server's saved host key, so the next sync trusts the one it sees.");
        cmd.Arguments.Add(connectionArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var updated = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.V1.Connections[id].HostKey.DeleteAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Forgot the host key for '{updated?.Name}'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
