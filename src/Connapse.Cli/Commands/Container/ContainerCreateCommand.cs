using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Container;

public static class ContainerCreateCommand
{
    private sealed record CreateRequest(string Name, string? Description);

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

            var apiClient = services.GetRequiredService<ApiClient>();

            Console.Write($"Creating container '{name}'... ");

            var result = await apiClient.PostAsync<CreateRequest, ContainerInfo>(
                "/api/containers",
                new CreateRequest(name, description),
                ct);

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
