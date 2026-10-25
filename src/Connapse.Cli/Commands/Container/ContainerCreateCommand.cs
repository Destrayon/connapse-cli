using System.CommandLine;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Container;

public static class ContainerCreateCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Name of the container to create.",
        };

        var descriptionOption = new Option<string?>("--description")
        {
            Description = "Optional human-readable description.",
        };

        var cmd = new Command("create", "Create a new knowledge container.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(descriptionOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var name = parseResult.GetRequiredValue(nameArg);
            var description = parseResult.GetValue(descriptionOption);

            var client = services.GetRequiredService<ConnapseApiClient>();

            Console.Write($"Creating container '{name}'... ");

            var result = await client.Api.Containers.PostAsync(
                new CreateContainerApiRequest { Name = name, Description = description },
                cancellationToken: ct);

            if (result is null)
                throw new CliServerError("Unexpected response from server.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Created");
            Console.ResetColor();
            Console.WriteLine($"  ID:   {result.Id}");
            Console.WriteLine($"  Name: {result.Name}");

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
