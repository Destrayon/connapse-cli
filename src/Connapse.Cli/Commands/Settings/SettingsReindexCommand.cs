using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Settings;

/// <summary><c>settings reindex</c>, <c>reindex-status</c>, <c>embedding-models</c> and <c>clear-summaries</c>.</summary>
public static class SettingsReindexCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var forceOption = new Option<bool>("--force") { Description = "Re-embed every document, not only those whose settings changed." };
        var containerOption = new Option<string?>("--container") { Description = "Only this container, by name or ID." };

        var cmd = new Command("reindex", "Re-embed documents in the background after embedding or chunking settings change.");
        cmd.Options.Add(forceOption);
        cmd.Options.Add(containerOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            string? containerId = null;
            if (parseResult.GetValue(containerOption) is { } container)
            {
                containerId = (await ContainerHelpers.ResolveIdAsync(client, container, ct))?.ToString()
                    ?? throw new CliValidationError($"No container named '{container}'.");
            }

            var started = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope, () => client.Api.Settings.Reindex.PostAsync(
                new ReindexTriggerRequest { ContainerId = containerId, Force = parseResult.GetValue(forceOption) },
                cancellationToken: ct));

            AdminHelpers.WriteSuccess(started?.Message ?? "Reindex started.");
            Console.WriteLine("Run 'connapse settings reindex-status' to follow it.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateStatus(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("reindex-status", "Show how far a reindex has got.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var status = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
                () => client.Api.Settings.Reindex.Status.GetAsync(cancellationToken: ct))
                ?? throw new CliServerError("The server returned no reindex status.");

            Console.WriteLine($"Status:       {status.Status}");
            Console.WriteLine($"Queue depth:  {status.QueueDepth ?? 0}");
            Console.WriteLine($"Started:      {AdminHelpers.Date(status.StartedAt)}");
            Console.WriteLine($"Completed:    {AdminHelpers.Date(status.CompletedAt)}");
            if (!string.IsNullOrEmpty(status.LastError))
                Console.WriteLine($"Last error:   {status.LastError}");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateEmbeddingModels(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("embedding-models", "List the embedding models that have vectors stored, across all containers.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var models = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
                () => client.Api.Settings.EmbeddingModels.GetAsync(cancellationToken: ct))
                ?? throw new CliServerError("The server returned no models.");

            Console.WriteLine($"Current model: {models.CurrentModel}");
            var list = models.Models ?? [];
            if (list.Count == 0)
            {
                Console.WriteLine("No vectors stored yet.");
                return CliExitCodes.Success;
            }

            Console.WriteLine($"{"MODEL",-50} {"DIMS",6} {"VECTORS",10}");
            foreach (var m in list)
                Console.WriteLine($"{m.ModelId + (m.IsCurrent == true ? " (current)" : ""),-50} {m.Dimensions ?? 0,6} {m.VectorCount ?? 0,10}");
            if (models.HasLegacyVectors == true)
                AdminHelpers.WriteWarning("Some vectors were made by another model; 'connapse settings reindex' re-embeds them.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateClearSummaries(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("clear-summaries",
            "Clear every container and document summary; the next sweep writes them again. Embeddings and documents are kept.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var cleared = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
                () => client.Api.V1.Settings.Summaries.Clear.PostAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Cleared {cleared?.Containers ?? 0} container and {cleared?.Documents ?? 0} document summaries.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
