using System.CommandLine;
using System.Net;
using System.Text.Json;
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

            var fileId = Guid.TryParse(fileRef, out _)
                ? fileRef
                : await ResolveFileIdByNameAsync(apiClient, containerId, fileRef, ct)
                    ?? throw new CliValidationError($"File '{fileRef}' not found in container '{containerName}'.");

            var path = $"/api/containers/{containerId}/files/{fileId}/content";

            try
            {
                var document = await apiClient.GetAsync<JsonDocument>(path, ct);
                if (document is null)
                    throw new CliServerError("Unexpected response from server.");

                if (document.RootElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                    Console.WriteLine(contentElement.GetString());
                else
                    Console.WriteLine(document.RootElement.GetRawText());
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
        ApiClient apiClient,
        string containerId,
        string nameOrPath,
        CancellationToken ct)
    {
        var normalized = nameOrPath.StartsWith('/') ? nameOrPath : "/" + nameOrPath;
        var listPath = $"/api/containers/{containerId}/files?path=%2F";
        var document = await apiClient.GetAsync<JsonDocument>(listPath, ct);
        if (document is null) return null;
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in items.EnumerateArray())
        {
            var isFolder = item.TryGetProperty("isFolder", out var f) && f.ValueKind == JsonValueKind.True;
            if (isFolder) continue;

            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;

            if (string.Equals(name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return item.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
            }
        }

        return null;
    }
}
