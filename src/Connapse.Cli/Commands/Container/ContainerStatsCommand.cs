using System.CommandLine;
using System.Text.Json;
using Connapse.Cli.Services;
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
            var apiClient = services.GetRequiredService<ApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(apiClient, name, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{name}' not found.");

            Console.Write($"Fetching stats for '{name}'... ");

            var stats = await apiClient.GetAsync<JsonDocument>(
                $"/api/containers/{containerId}/stats", ct);
            if (stats is null)
                throw new CliServerError("Unexpected response from server.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK");
            Console.ResetColor();
            Console.WriteLine();

            var root = stats.RootElement;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Documents:");
            Console.ResetColor();
            if (root.TryGetProperty("documents", out var docs))
            {
                if (docs.TryGetProperty("total", out var total))
                    Console.WriteLine($"  Total:      {total.GetInt32()}");
                if (docs.TryGetProperty("ready", out var ready))
                    Console.WriteLine($"  Ready:      {ready.GetInt32()}");
                if (docs.TryGetProperty("processing", out var processing))
                    Console.WriteLine($"  Processing: {processing.GetInt32()}");
                if (docs.TryGetProperty("failed", out var failed))
                    Console.WriteLine($"  Failed:     {failed.GetInt32()}");
            }
            Console.WriteLine();

            if (root.TryGetProperty("totalChunks", out var chunks))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Total Chunks: ");
                Console.ResetColor();
                Console.WriteLine(chunks.GetInt32());
            }

            if (root.TryGetProperty("totalSizeBytes", out var sizeBytes))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Storage Size: ");
                Console.ResetColor();
                Console.WriteLine(ContainerHelpers.FormatBytes(sizeBytes.GetInt64()));
            }

            if (root.TryGetProperty("embedding", out var embedding))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Embedding:");
                Console.ResetColor();
                if (embedding.TryGetProperty("modelId", out var modelId) && modelId.ValueKind != JsonValueKind.Null)
                    Console.WriteLine($"  Model:      {modelId.GetString()}");
                if (embedding.TryGetProperty("dimensions", out var dims) && dims.ValueKind != JsonValueKind.Null)
                    Console.WriteLine($"  Dimensions: {dims.GetInt32()}");
            }

            if (root.TryGetProperty("lastIndexedAt", out var lastIndexed) && lastIndexed.ValueKind != JsonValueKind.Null)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Last Indexed: ");
                Console.ResetColor();
                if (DateTime.TryParse(lastIndexed.GetString(), out var dt))
                    Console.WriteLine(dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                else
                    Console.WriteLine(lastIndexed.GetString());
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
