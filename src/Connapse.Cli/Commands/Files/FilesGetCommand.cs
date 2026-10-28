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
    /// folder, so this lists the file's own folder, every page of it, and matches the name. The
    /// server matches folders case-sensitively, so when that folder holds no match, the folders are
    /// walked from the root, each name matched ignoring case. At every step an exact name wins;
    /// otherwise a single match ignoring case is used, and several are an error.
    /// </summary>
    private static async Task<string?> ResolveFileIdByPathAsync(
        ConnapseApiClient client,
        Guid containerId,
        string path,
        CancellationToken ct)
    {
        var segments = Segments(path);
        if (segments.Count == 0)
            return null;

        var fileName = segments[^1];
        var folderNames = segments[..^1];
        var folder = "/" + string.Concat(folderNames.Select(s => s + "/"));

        var file = Pick(Files(await ListFolderAsync(client, containerId, folder, ct)), fileName, path);
        if (file is not null)
            return file.Id;

        var walked = "/";
        foreach (var name in folderNames)
        {
            var subfolder = Pick((await ListFolderAsync(client, containerId, walked, ct)).Where(e => e.IsFolder == true), name, path);
            if (subfolder?.Path is null)
                return null;
            walked = subfolder.Path.EndsWith('/') ? subfolder.Path : subfolder.Path + "/";
        }

        if (string.Equals(walked, folder, StringComparison.Ordinal))
            return null;

        return Pick(Files(await ListFolderAsync(client, containerId, walked, ct)), fileName, path)?.Id;
    }

    /// <summary>A path's segments, normalized as the server does: '\' is '/', and empty, '.' and '..' segments collapse.</summary>
    private static List<string> Segments(string path)
    {
        var stack = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Trim().Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(segment);
        }
        return stack;
    }

    private static IEnumerable<BrowseEntry> Files(List<BrowseEntry> entries) =>
        entries.Where(e => e.IsFolder != true && e.Id is not null);

    private static BrowseEntry? Pick(IEnumerable<BrowseEntry> entries, string name, string path)
    {
        var candidates = entries.ToList();
        var exact = candidates.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
        if (exact is not null)
            return exact;

        var caseless = candidates.Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return caseless.Count switch
        {
            0 => null,
            1 => caseless[0],
            _ => throw new CliValidationError($"More than one entry matches '{path}' when case is ignored; give its exact path or ID."),
        };
    }

    private static Task<List<BrowseEntry>> ListFolderAsync(ConnapseApiClient client, Guid containerId, string folder, CancellationToken ct) =>
        Paging.ReadAllAsync<BrowseEntry>(async (skip, take) =>
        {
            var page = await client.Api.Containers[containerId].Files.GetAsync(r =>
            {
                r.QueryParameters.Path = folder;
                r.QueryParameters.Skip = skip;
                r.QueryParameters.Take = take;
            }, ct);
            return (page?.Items ?? [], page?.HasMore ?? false);
        });
}
