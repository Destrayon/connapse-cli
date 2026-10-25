using System.CommandLine;
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

        var cmd = new Command("delete", "Delete a knowledge container.");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var name = parseResult.GetRequiredValue(nameArg);
            var client = services.GetRequiredService<ConnapseApiClient>();

            var containerId = await ContainerHelpers.ResolveIdAsync(client, name, ct);
            if (containerId is null)
                throw new CliValidationError($"Container '{name}' not found.");

            Console.Write($"Deleting container '{name}'... ");

            await client.Api.Containers[containerId.Value].DeleteAsync(cancellationToken: ct);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Deleted");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
