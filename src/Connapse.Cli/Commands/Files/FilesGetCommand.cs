using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
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

            var client = services.GetRequiredService<ConnapseApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(client, containerName, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{containerName}' not found.");

            var fileId = Guid.TryParse(fileRef, out _)
                ? fileRef
                : await ResolveFileIdByNameAsync(client, containerId.Value, fileRef, ct)
                    ?? throw new CliValidationError($"File '{fileRef}' not found in container '{containerName}'.");

            try
            {
                var document = await client.Api.Containers[containerId.Value].Files[fileId].Content.GetAsync(cancellationToken: ct);
                if (document is null)
                    throw new CliServerError("Unexpected response from server.");

                Console.WriteLine(document.Content);
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CliValidationError($"File '{fileRef}' not found in container '{containerName}'.");
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }

    private static async Task<string?> ResolveFileIdByNameAsync(
        ConnapseApiClient client,
        Guid containerId,
        string nameOrPath,
        CancellationToken ct)
    {
        var normalized = nameOrPath.StartsWith('/') ? nameOrPath : "/" + nameOrPath;
        var listing = await client.Api.Containers[containerId].Files.GetAsync(r => r.QueryParameters.Path = "/", ct);

        return listing?.Items?
            .Where(item => item.IsFolder != true)
            .FirstOrDefault(item => string.Equals(item.Name, nameOrPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase))?
            .Id;
    }
}
