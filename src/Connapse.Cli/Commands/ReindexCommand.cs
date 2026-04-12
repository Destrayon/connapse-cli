using System.CommandLine;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

public static class ReindexCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var containerOption = new Option<string>("--container")
        {
            Description = "Container name to reindex.",
            Required = true,
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Reindex every document, ignoring content hashes.",
        };

        var noDetectChangesOption = new Option<bool>("--no-detect-changes")
        {
            Description = "Skip the settings-change detection pass.",
        };

        var cmd = new Command("reindex", "Trigger a reindex of a container.");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(forceOption);
        cmd.Options.Add(noDetectChangesOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var containerName = parseResult.GetRequiredValue(containerOption);
            var force = parseResult.GetValue(forceOption);
            var detectChanges = !parseResult.GetValue(noDetectChangesOption);

            var apiClient = services.GetRequiredService<ApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(apiClient, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            Console.WriteLine($"Triggering reindex for container '{containerName}'...");
            if (force)
                Console.WriteLine("Mode: Force (ignoring content hashes)");
            if (!detectChanges)
                Console.WriteLine("Settings change detection: disabled");
            Console.WriteLine();

            var request = new ReindexRequest(force, detectChanges);
            var result = await apiClient.PostAsync<ReindexRequest, ReindexResult>(
                $"/api/containers/{containerId}/reindex", request, ct);

            if (result is null)
                throw new CliServerError("Unexpected response from server.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Reindex complete");
            Console.ResetColor();
            Console.WriteLine($"  Total documents: {result.TotalDocuments}");
            Console.WriteLine($"  Enqueued:        {result.EnqueuedCount}");
            Console.WriteLine($"  Skipped:         {result.SkippedCount}");
            Console.WriteLine($"  Failed:          {result.FailedCount}");

            if (result.ReasonCounts is { Count: > 0 })
            {
                Console.WriteLine("  Reasons:");
                foreach (var kvp in result.ReasonCounts)
                    Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
            }

            if (result.EnqueuedCount > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Reindexing is processing in the background.");
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
