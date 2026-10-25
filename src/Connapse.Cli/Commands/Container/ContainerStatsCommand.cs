using System.CommandLine;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Container;

public static class ContainerStatsCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Name of the container to report on.",
        };

        var cmd = new Command("stats", "Show statistics for a container.");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var name = parseResult.GetRequiredValue(nameArg);
            var client = services.GetRequiredService<ConnapseApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(client, name, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{name}' not found.");

            Console.Write($"Fetching stats for '{name}'... ");

            var stats = await client.Api.Containers[containerId.Value].Stats.GetAsync(cancellationToken: ct);
            if (stats is null)
                throw new CliServerError("Unexpected response from server.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Documents:");
            Console.ResetColor();
            if (stats.Documents is { } docs)
            {
                Console.WriteLine($"  Total:      {docs.Total}");
                Console.WriteLine($"  Ready:      {docs.Ready}");
                Console.WriteLine($"  Processing: {docs.Processing}");
                Console.WriteLine($"  Failed:     {docs.Failed}");
            }
            Console.WriteLine();

            if (stats.TotalChunks is { } chunks)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Total Chunks: ");
                Console.ResetColor();
                Console.WriteLine(chunks);
            }

            if (stats.TotalSizeBytes is { } sizeBytes)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Storage Size: ");
                Console.ResetColor();
                Console.WriteLine(ContainerHelpers.FormatBytes(sizeBytes));
            }

            // One line per embedding model the container's vectors use; a container mid re-embed has two.
            if (stats.EmbeddingModels is { Count: > 0 } models)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Embedding:");
                Console.ResetColor();
                foreach (var model in models)
                    Console.WriteLine($"  Model:      {model.ModelId} ({model.Dimensions} dimensions, {model.VectorCount} vectors)");
            }

            if (stats.LastIndexedAt is { } lastIndexed)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Last Indexed: ");
                Console.ResetColor();
                Console.WriteLine(lastIndexed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
