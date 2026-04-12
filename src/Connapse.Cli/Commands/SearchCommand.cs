using System.CommandLine;
using System.Globalization;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

public static class SearchCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var queryArg = new Argument<string>("query")
        {
            Description = "The search query.",
        };

        var containerOption = new Option<string>("--container")
        {
            Description = "Container name to search in.",
            Required = true,
        };

        var modeOption = new Option<string>("--mode")
        {
            Description = "Search mode: Hybrid, Semantic, or Keyword. Defaults to Hybrid.",
            DefaultValueFactory = _ => "Hybrid",
        };

        var topOption = new Option<int>("--top")
        {
            Description = "Maximum number of results to return. Defaults to 10.",
            DefaultValueFactory = _ => 10,
        };

        var pathOption = new Option<string?>("--path")
        {
            Description = "Restrict results to files under this folder.",
        };

        var minScoreOption = new Option<double?>("--min-score")
        {
            Description = "Filter hits below this score (0.0 – 1.0).",
        };

        var cmd = new Command("search", "Search inside a container.");
        cmd.Arguments.Add(queryArg);
        cmd.Options.Add(containerOption);
        cmd.Options.Add(modeOption);
        cmd.Options.Add(topOption);
        cmd.Options.Add(pathOption);
        cmd.Options.Add(minScoreOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var query = parseResult.GetRequiredValue(queryArg);
            var containerName = parseResult.GetRequiredValue(containerOption);
            var mode = parseResult.GetValue(modeOption) ?? "Hybrid";
            var topK = parseResult.GetValue(topOption);
            var folderPath = parseResult.GetValue(pathOption);
            var minScore = parseResult.GetValue(minScoreOption);

            if (topK <= 0)
                throw new CliValidationError("--top must be a positive integer.");

            if (minScore.HasValue && (minScore.Value < 0.0 || minScore.Value > 1.0))
                throw new CliValidationError("--min-score must be between 0.0 and 1.0.");

            var apiClient = services.GetRequiredService<ApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(apiClient, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            Console.WriteLine($"Searching: \"{query}\"");
            Console.WriteLine($"Container: {containerName} | Mode: {mode} | Top: {topK}");
            if (!string.IsNullOrWhiteSpace(folderPath))
                Console.WriteLine($"Path filter: {folderPath}");
            if (minScore.HasValue)
                Console.WriteLine($"Min score: {minScore.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            Console.WriteLine();

            var url = $"/api/containers/{containerId}/search" +
                      $"?q={Uri.EscapeDataString(query)}" +
                      $"&mode={Uri.EscapeDataString(mode)}" +
                      $"&topK={topK}";
            if (!string.IsNullOrWhiteSpace(folderPath))
                url += $"&path={Uri.EscapeDataString(folderPath)}";
            if (minScore.HasValue)
                url += $"&minScore={minScore.Value.ToString("0.###", CultureInfo.InvariantCulture)}";

            var result = await apiClient.GetAsync<SearchResult>(url, ct);

            if (result is null || result.Hits.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No results found.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Found {result.TotalMatches} results in {result.Duration.TotalMilliseconds:F0}ms");
            Console.ResetColor();
            Console.WriteLine();

            for (int i = 0; i < result.Hits.Count; i++)
            {
                var hit = result.Hits[i];

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"[{i + 1}] Score: {hit.Score:F3}");
                Console.ResetColor();

                if (hit.Metadata.TryGetValue("FileName", out var fileName))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  | {fileName}");
                    Console.ResetColor();
                }

                if (hit.Metadata.TryGetValue("ChunkIndex", out var chunkIdx))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" (chunk {chunkIdx})");
                    Console.ResetColor();
                }
                Console.WriteLine();

                var content = hit.Content;
                if (content.Length > 300)
                    content = content[..297] + "...";
                Console.WriteLine($"    {content}");
                Console.WriteLine();
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
