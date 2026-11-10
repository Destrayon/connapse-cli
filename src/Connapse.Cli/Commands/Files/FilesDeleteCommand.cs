using System.CommandLine;
using Connapse.Cli.Commands.Approval;
using Connapse.Cli.Services;

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

        var cmd = new Command("delete", "Delete a file from a container. When the server asks for approval, the delete waits for its owner (exit code 3).");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(fileOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var containerName = parseResult.GetRequiredValue(containerOption);
            var fileId = parseResult.GetRequiredValue(fileOption);

            if (!Guid.TryParse(fileId, out _))
                throw new CliValidationError($"'{fileId}' is not a valid file ID (expected GUID format).");

            // The container goes as the user named it, so running the same command again after approval matches.
            var result = await ApprovalHelpers.CallToolAsync(services, "delete_file",
                new Dictionary<string, string> { ["containerId"] = containerName, ["fileId"] = fileId }, ct);
            if (result.HeldRequestId is { } held)
                return ApprovalHelpers.WriteHeld(services, held, json: false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Deleted file {fileId}.");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
