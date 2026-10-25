using System.CommandLine;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
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

            var client = services.GetRequiredService<ConnapseApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(client, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            var result = await client.Api.Containers[containerId.Value].Files.GetAsync(r => r.QueryParameters.Path = path, ct);
            if (result is null)
                throw new CliServerError("Unexpected response from server.");

            if (result.Items is not { Count: > 0 } items)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No files or folders found at this path.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            var folderEntries = items.Where(i => i.IsFolder == true).ToList();
            var fileEntries = items.Where(i => i.IsFolder != true).ToList();

            if (folderEntries.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Folders:");
                Console.ResetColor();
                foreach (var folder in folderEntries)
                {
                    var name = folder.Name ?? "?";
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  {name}/");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }

            if (fileEntries.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Files:");
                Console.ResetColor();
                foreach (var file in fileEntries)
                {
                    var name = file.Name ?? "?";
                    var id = file.Id ?? "?";
                    var status = file.Status ?? "?";

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

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
