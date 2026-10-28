using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Source;

public static class SourceScopeSchemaCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var providerArg = new Argument<string>("provider")
        {
            Description = $"The provider: {string.Join(", ", AdminHelpers.EnumNames<ConnectionProvider>())}.",
        };

        var cmd = new Command("scope-schema", "Show the JSON schema of each scope a provider's sources take.");
        cmd.Arguments.Add(providerArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var provider = AdminHelpers.ParseEnum<ConnectionProvider>(parseResult.GetRequiredValue(providerArg), "provider");
            var client = services.GetRequiredService<ConnapseApiClient>();

            var schema = await client.Api.V1.Sources.ScopeSchema[provider.ToString()].GetAsync(cancellationToken: ct)
                ?? throw new CliServerError("The server returned no schema.");

            foreach (var kind in schema.Kinds ?? [])
            {
                Console.WriteLine($"type: {kind.Type}");
                if (kind.Schema is not null)
                    Console.WriteLine(AdminHelpers.ToJson(kind.Schema));
                Console.WriteLine();
            }
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
