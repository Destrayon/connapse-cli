using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Files;

public static class FilesGetCommand
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
            Description = "File ID (GUID) or file path inside the container.",
            Required = true,
        };

        var cmd = new Command("get", "Print the text content of a file.");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(fileOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var containerName = parseResult.GetRequiredValue(containerOption);
            var fileRef = parseResult.GetRequiredValue(fileOption);

            var apiClient = services.GetRequiredService<ApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(apiClient, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            var encodedRef = Guid.TryParse(fileRef, out _)
                ? fileRef
                : Uri.EscapeDataString(fileRef);

            var path = $"/api/containers/{containerId}/files/{encodedRef}/content";

            try
            {
                using var response = await apiClient.SendRawAsync(
                    HttpMethod.Get, path, content: null, ct);

                var body = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine(body);
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CliValidationError($"File '{fileRef}' not found in container '{containerName}'.");
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
