using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionTestCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();
        var configOptions = new JsonObjectOptions("config", "Try these settings instead of the saved ones.");
        var probeOption = new Option<string?>("--probe-target")
        {
            Description = "A bucket, container, repository or folder to try reading.",
        };
        var secrets = new SecretOptions();

        var cmd = new Command("test", "Check that a connection can sign in and read. Exits 1 if it can't.");
        cmd.Arguments.Add(connectionArg);
        configOptions.AddTo(cmd);
        cmd.Options.Add(probeOption);
        secrets.AddTo(cmd);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var config = configOptions.Read(parseResult);
            var (token, privateKey, passphrase) = secrets.Read(parseResult);
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var result = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections[id].Test.PostAsync(
                new TestConnectionApiRequest
                {
                    Config = config,
                    ProbeTarget = parseResult.GetValue(probeOption),
                    Token = token,
                    PrivateKey = privateKey,
                    Passphrase = passphrase,
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no test result.");

            if (result.Success != true)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {result.Message}");
                Console.ResetColor();
                return CliExitCodes.UserError;
            }

            if (result.Warning == true)
            {
                AdminHelpers.WriteWarning($"Connected, with a warning: {result.Message}");
                if (!string.IsNullOrEmpty(result.WarningUrl))
                    Console.WriteLine($"  {result.WarningUrl}");
            }
            else
            {
                AdminHelpers.WriteSuccess($"Connected. {result.Message}".TrimEnd());
            }
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
