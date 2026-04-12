using System.CommandLine;
using System.Text.Json;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Files;

public static class FilesListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var containerOption = new Option<string>("--container")
        {
            Description = "Container name to list files from.",
            Required = true,
        };

        var pathOption = new Option<string>("--path")
        {
            Description = "Folder path inside the container. Defaults to '/'.",
            DefaultValueFactory = _ => "/",
        };

        var cmd = new Command("list", "List files and folders in a container.");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(pathOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var containerName = parseResult.GetRequiredValue(containerOption);
            var path = parseResult.GetValue(pathOption) ?? "/";

            var apiClient = services.GetRequiredService<ApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(apiClient, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            var url = $"/api/containers/{containerId}/files?path={Uri.EscapeDataString(path)}";
            var result = await apiClient.GetAsync<JsonDocument>(url, ct);
            if (result is null)
                throw new CliServerError("Unexpected response from server.");

            var root = result.RootElement;

            var hasFolders = root.TryGetProperty("folders", out var folders)
                && folders.ValueKind == JsonValueKind.Array
                && folders.GetArrayLength() > 0;
            var hasFiles = root.TryGetProperty("files", out var files)
                && files.ValueKind == JsonValueKind.Array
                && files.GetArrayLength() > 0;

            if (hasFolders)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Folders:");
                Console.ResetColor();
                foreach (var folder in folders.EnumerateArray())
                {
                    var name = folder.TryGetProperty("name", out var n) ? n.GetString() : "?";
                    var count = folder.TryGetProperty("documentCount", out var dc)
                        ? dc.GetInt32().ToString()
                        : (folder.TryGetProperty("fileCount", out var fc) ? fc.GetInt32().ToString() : "?");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"  {name}/");
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  ({count} files)");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }

            if (hasFiles)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Files:");
                Console.ResetColor();
                foreach (var file in files.EnumerateArray())
                {
                    var name = file.TryGetProperty("fileName", out var fn) ? fn.GetString() : "?";
                    var id = file.TryGetProperty("id", out var fid) ? fid.GetString() : "?";
                    var status = file.TryGetProperty("status", out var s) ? s.GetString() : "?";

                    var statusColor = status switch
                    {
                        "Ready" => ConsoleColor.Green,
                        "Processing" or "Pending" => ConsoleColor.Yellow,
                        "Failed" => ConsoleColor.Red,
                        _ => ConsoleColor.DarkGray,
                    };

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"  {name}");
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  [{id}]");
                    Console.ResetColor();
                    Console.Write("  ");
                    Console.ForegroundColor = statusColor;
                    Console.WriteLine(status);
                    Console.ResetColor();
                }
            }

            if (!hasFolders && !hasFiles)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No files or folders found at this path.");
                Console.ResetColor();
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
