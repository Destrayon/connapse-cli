using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Connapse.Cli.Commands.Source;

public static class SourceUpdateCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var sourceArg = SourceHelpers.SourceArgument();
        var nameOption = new Option<string?>("--name") { Description = "A new name." };
        var descriptionOption = new Option<string?>("--description") { Description = "A new description." };
        var scopeOptions = new JsonObjectOptions("scope", "A new scope, with its \"type\".");
        var intervalOption = new Option<int?>("--sync-interval") { Description = "Seconds between syncs." };
        var enableOption = new Option<bool>("--enable") { Description = "Start syncing the source again." };
        var disableOption = new Option<bool>("--disable") { Description = "Stop syncing the source; its documents stay." };

        var cmd = new Command("update", "Change a source's name, description, scope, sync interval, or whether it syncs.");
        cmd.Arguments.Add(sourceArg);
        cmd.Options.Add(nameOption);
        cmd.Options.Add(descriptionOption);
        scopeOptions.AddTo(cmd);
        cmd.Options.Add(intervalOption);
        cmd.Options.Add(enableOption);
        cmd.Options.Add(disableOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            // PATCH takes only the fields being changed, so the body is built from those given.
            var fields = new Dictionary<string, UntypedNode>();
            if (parseResult.GetValue(nameOption) is { } name)
                fields["name"] = new UntypedString(name);
            if (parseResult.GetValue(descriptionOption) is { } description)
                fields["description"] = new UntypedString(description);
            if (scopeOptions.Read(parseResult) is { } scope)
                fields["scope"] = scope;
            if (parseResult.GetValue(intervalOption) is { } interval)
                fields["syncIntervalSeconds"] = new UntypedInteger(interval);

            var enable = parseResult.GetValue(enableOption);
            var disable = parseResult.GetValue(disableOption);
            if (enable && disable)
                throw new CliValidationError("Pass --enable or --disable, not both.");
            if (enable || disable)
                fields["enabled"] = new UntypedBoolean(enable);

            if (fields.Count == 0)
                throw new CliValidationError("Nothing to update; pass --name, --description, --scope, --sync-interval, --enable or --disable.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await SourceHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(sourceArg), ct);

            var updated = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.Sources[id].PatchAsync(new UntypedObject(fields), cancellationToken: ct))
                ?? throw new CliServerError("The server returned no source.");

            AdminHelpers.WriteSuccess($"Updated source '{updated.Name}'.");
            if (!string.IsNullOrEmpty(updated.Warning))
                AdminHelpers.WriteWarning(updated.Warning);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
