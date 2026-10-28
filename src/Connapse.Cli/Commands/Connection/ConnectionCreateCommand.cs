using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionCreateCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name") { Description = "A name for the connection." };
        var providerOption = new Option<string>("--provider")
        {
            Description = $"The provider: {string.Join(", ", AdminHelpers.EnumNames<ConnectionProvider>())}.",
            Required = true,
        };
        var configOptions = new JsonObjectOptions("config", "The provider's settings.");
        var secrets = new SecretOptions();

        var cmd = new Command("create", "Create a connection: where sources read from, and the credential to read with.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(providerOption);
        configOptions.AddTo(cmd);
        secrets.AddTo(cmd);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var provider = AdminHelpers.ParseEnum<ConnectionProvider>(parseResult.GetRequiredValue(providerOption), "--provider");
            var config = configOptions.Read(parseResult);
            var (token, privateKey, passphrase) = secrets.Read(parseResult);
            var client = services.GetRequiredService<ConnapseApiClient>();

            var created = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections.PostAsync(
                new CreateConnectionApiRequest
                {
                    Name = parseResult.GetRequiredValue(nameArg),
                    Provider = provider,
                    Config = config,
                    Token = token,
                    PrivateKey = privateKey,
                    Passphrase = passphrase,
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no connection.");

            AdminHelpers.WriteSuccess($"Created connection '{created.Connection?.Name}' ({created.Connection?.Id}).");
            if (!string.IsNullOrEmpty(created.Note))
                AdminHelpers.WriteWarning(created.Note);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
