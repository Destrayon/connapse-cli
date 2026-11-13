using System.CommandLine;
using Connapse.Cli.Commands.Approval;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Container;

public static class ContainerDeleteCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Name of the container to delete.",
        };

        var cmd = new Command("delete", "Delete an empty knowledge container. When the server asks for approval, the delete waits for its owner (exit code 3).");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var name = parseResult.GetRequiredValue(nameArg);
            var result = await ApprovalHelpers.CallToolAsync(services, "container_delete",
                new Dictionary<string, string> { ["containerId"] = name }, ct);
            if (result is null)
            {
                // An Editor's sign-in: the approval route refuses it, so the delete goes through REST, unheld.
                var client = services.GetRequiredService<ConnapseApiClient>();
                var containerId = await ContainerHelpers.ResolveIdAsync(client, name, ct)
                    ?? throw new CliValidationError($"Container '{name}' not found.");
                await client.Api.Containers[containerId].DeleteAsync(cancellationToken: ct);
            }
            else if (result.IsHeld)
                return ApprovalHelpers.WriteHeld(services, result, json: false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Deleted container '{name}'.");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
