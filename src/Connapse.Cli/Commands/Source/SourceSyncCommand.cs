using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Source;

/// <summary><c>source sync</c> and <c>source retry-failed</c>.</summary>
public static class SourceSyncCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var sourceArg = SourceHelpers.SourceArgument();
        var applyOption = new Option<bool>("--apply-withheld-deletions")
        {
            Description = "Also apply deletions held back because they'd remove a large share of the source.",
        };

        var cmd = new Command("sync", "Sync a source now instead of waiting for its next scheduled sync.");
        cmd.Arguments.Add(sourceArg);
        cmd.Options.Add(applyOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await SourceHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(sourceArg), ct);

            var result = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.Sources[id].Sync.PostAsync(
                r => r.QueryParameters.ApplyWithheldDeletions = parseResult.GetValue(applyOption) ? true : null, ct))
                ?? throw new CliServerError("The server returned no sync result.");

            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Sync failed: {result.Error}");
                Console.ResetColor();
                return CliExitCodes.UserError;
            }

            AdminHelpers.WriteSuccess($"Synced: {result.Upserted ?? 0} added or changed, {result.Deleted ?? 0} deleted.");
            if (result.WithheldDeletions is > 0)
                AdminHelpers.WriteWarning($"{result.WithheldDeletions} deletion(s) withheld; rerun with --apply-withheld-deletions to apply them.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateRetryFailed(IServiceProvider services, Option<string?> orgOption)
    {
        var sourceArg = SourceHelpers.SourceArgument();

        var cmd = new Command("retry-failed", "Queue every document of a source that failed to ingest for another try.");
        cmd.Arguments.Add(sourceArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await SourceHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(sourceArg), ct);

            var result = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.Sources[id].RetryFailed.PostAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Queued {result?.EnqueuedCount ?? 0} failed document(s) for another try.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
