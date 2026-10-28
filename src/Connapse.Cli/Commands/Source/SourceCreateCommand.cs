using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Commands.Connection;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Source;

public static class SourceCreateCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name") { Description = "A name for the source." };
        var connectionOption = new Option<string>("--connection")
        {
            Description = "The connection to read through, by name or ID.",
            Required = true,
        };
        var scopeOptions = new JsonObjectOptions("scope",
            $"What to index, with its \"type\" ({SourceHelpers.ScopeTypes}); 'source scope-schema' shows the fields.");
        var descriptionOption = new Option<string?>("--description") { Description = "A description." };
        var intervalOption = new Option<int?>("--sync-interval") { Description = "Seconds between syncs." };

        var cmd = new Command("create", "Create a source: what a connection indexes.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(connectionOption);
        scopeOptions.AddTo(cmd);
        cmd.Options.Add(descriptionOption);
        cmd.Options.Add(intervalOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var scopeJson = scopeOptions.ReadText(parseResult)
                ?? throw new CliValidationError("Pass the source's scope with --scope or --scope-file.");
            var scope = SourceHelpers.ParseScope(scopeJson, scopeOptions.Inline.Name);

            var client = services.GetRequiredService<ConnapseApiClient>();
            var connectionId = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionOption), ct);

            var created = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.Sources.PostAsync(
                new CreateSourceApiRequest
                {
                    Name = parseResult.GetRequiredValue(nameArg),
                    Description = parseResult.GetValue(descriptionOption),
                    ConnectionId = connectionId,
                    Scope = scope,
                    SyncIntervalSeconds = parseResult.GetValue(intervalOption),
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no source.");

            AdminHelpers.WriteSuccess($"Created source '{created.Name}' ({created.Id}).");
            if (!string.IsNullOrEmpty(created.Warning))
                AdminHelpers.WriteWarning(created.Warning);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
