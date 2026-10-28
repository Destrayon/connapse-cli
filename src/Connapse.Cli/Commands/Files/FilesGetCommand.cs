using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
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
            Description = "File ID (GUID) or file path inside the container, such as docs/guide.md.",
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
                : await ResolveFileIdByPathAsync(client, containerId.Value, fileRef, ct)
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

    /// <summary>
    /// Finds a file by its path in the container. The server lists only what sits directly in one
    /// folder, so this lists the file's own folder, every page of it, and matches the full path.
    /// An exact match wins; otherwise a single match ignoring case is used, and several are an error.
    /// </summary>
    private static async Task<string?> ResolveFileIdByPathAsync(
        ConnapseApiClient client,
        Guid containerId,
        string path,
        CancellationToken ct)
    {
        var normalized = "/" + path.Replace('\\', '/').Trim().TrimStart('/');
        var folder = normalized[..(normalized.LastIndexOf('/') + 1)];

        var entries = await Paging.ReadAllAsync<BrowseEntry>(async (skip, take) =>
        {
            var page = await client.Api.Containers[containerId].Files.GetAsync(r =>
            {
                r.QueryParameters.Path = folder;
                r.QueryParameters.Skip = skip;
                r.QueryParameters.Take = take;
            }, ct);
            return (page?.Items ?? [], page?.HasMore ?? false);
        });

        var files = entries.Where(e => e.IsFolder != true && e.Id is not null).ToList();

        var exact = files.FirstOrDefault(e => string.Equals(e.Path, normalized, StringComparison.Ordinal));
        if (exact is not null)
            return exact.Id;

        var caseless = files.Where(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        return caseless.Count switch
        {
            0 => null,
            1 => caseless[0].Id,
            _ => throw new CliValidationError($"More than one file matches '{path}' when case is ignored; give its exact path or ID."),
        };
    }
}
