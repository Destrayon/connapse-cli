using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Connapse.Cli.Commands.Approval;
using Connapse.Cli.Commands.Container;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Files;

public static partial class FilesDeleteCommand
{
    /// <summary>The most files the server's <c>delete_file</c> takes in one call.</summary>
    internal const int MaxFiles = 100;

    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var containerOption = new Option<string>("--container")
        {
            Description = "Container name the file belongs to.",
            Required = true,
        };

        var fileOption = new Option<string[]>("--file")
        {
            Description = $"File ID (GUID) to delete. Give it more than once to delete up to {MaxFiles} files in one request.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };

        var cmd = new Command("delete", "Delete one or more files from a container. When the server asks for approval, the delete waits for its owner (exit code 3).");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(fileOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var containerName = parseResult.GetRequiredValue(containerOption);
            var fileIds = parseResult.GetRequiredValue(fileOption).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var fileId in fileIds)
            {
                if (!Guid.TryParse(fileId, out _))
                    throw new CliValidationError($"'{fileId}' is not a valid file ID (expected GUID format).");
            }
            if (fileIds.Count > MaxFiles)
                throw new CliValidationError($"At most {MaxFiles} files can be deleted at once; {fileIds.Count} were given.");

            // One file goes as delete_file's fileId, several as its fileIds list (Connapse 0.4.2, CON-344), so the
            // server holds them as one batch. The container goes as the user named it.
            var arguments = new Dictionary<string, string> { ["containerId"] = containerName };
            if (fileIds.Count == 1)
                arguments["fileId"] = fileIds[0];
            else
                arguments["fileIds"] = JsonSerializer.Serialize(fileIds);

            var result = await ApprovalHelpers.CallToolAsync(services, "delete_file", arguments, ct);
            if (result is null)
                return await DeleteThroughRestAsync(services, containerName, fileIds, ct);
            if (result.IsHeld)
                return ApprovalHelpers.WriteHeld(services, result, json: false);

            if (fileIds.Count == 1)
            {
                WriteDeleted($"Deleted file {fileIds[0]}.");
                return CliExitCodes.Success;
            }

            // The server answers "Deleted <n> of <m> file(s)." and lists any failures under it.
            Console.WriteLine(result.Text);
            var counts = DeletedCounts().Match(result.Text);
            return counts.Success && counts.Groups[1].Value == counts.Groups[2].Value
                ? CliExitCodes.Success
                : CliExitCodes.UserError;
        }));

        return cmd;
    }

    /// <summary>An Editor's sign-in: the approval route refuses it, so each file goes through the REST delete, unheld.</summary>
    private static async Task<int> DeleteThroughRestAsync(IServiceProvider services, string containerName, List<string> fileIds, CancellationToken ct)
    {
        var client = services.GetRequiredService<ConnapseApiClient>();
        var containerId = await ContainerHelpers.ResolveIdAsync(client, containerName, ct)
            ?? throw new CliValidationError($"Container '{containerName}' not found.");

        var missing = new List<string>();
        foreach (var fileId in fileIds)
        {
            try
            {
                await client.Api.Containers[containerId].Files[fileId].DeleteAsync(cancellationToken: ct);
                WriteDeleted($"Deleted file {fileId}.");
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                missing.Add(fileId);
            }
        }

        if (missing.Count == fileIds.Count && missing.Count == 1)
            throw new CliValidationError($"File '{missing[0]}' not found in container '{containerName}'.");
        if (missing.Count > 0)
        {
            foreach (var fileId in missing)
                Console.Error.WriteLine($"File '{fileId}' not found in container '{containerName}'.");
            return CliExitCodes.UserError;
        }
        return CliExitCodes.Success;
    }

    private static void WriteDeleted(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    [GeneratedRegex(@"Deleted (\d+) of (\d+) file")]
    private static partial Regex DeletedCounts();
}
