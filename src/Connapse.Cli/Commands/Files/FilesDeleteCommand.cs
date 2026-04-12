using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Files;

public static class FilesDeleteCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var containerOption = new Option<string>("--container")
        {
            Description = "Container name the file belongs to.",
            Required = true,
        };

        var fileOption = new Option<string>("--file")
        {
            Description = "File ID (GUID) to delete.",
            Required = true,
        };

        var cmd = new Command("delete", "Delete a file from a container.");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(fileOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var containerName = parseResult.GetRequiredValue(containerOption);
            var fileId = parseResult.GetRequiredValue(fileOption);

            if (!Guid.TryParse(fileId, out _))
                throw new CliValidationError($"'{fileId}' is not a valid file ID (expected GUID format).");

            var apiClient = services.GetRequiredService<ApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(apiClient, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            Console.Write($"Deleting file {fileId}... ");

            try
            {
                await apiClient.DeleteAsync($"/api/containers/{containerId}/files/{fileId}", ct);
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Not found.");
                Console.ResetColor();
                return CliExitCodes.UserError;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Deleted");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
