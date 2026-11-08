using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Memory;

/// <summary><c>connapse memory forget</c>: DELETE /api/memory/facts/{id} (memory.forget).</summary>
public static class MemoryForgetCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var idArg = new Argument<string>("fact-id") { Description = "The fact's ID, as save, recall and list print it." };
        var jsonOption = MemoryHelpers.JsonOption();

        var cmd = new Command("forget",
            "Delete a fact for good, with every version of it that corrections made.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var id = parseResult.GetRequiredValue(idArg);
            if (!Guid.TryParse(id, out _))
                throw new CliValidationError($"'{id}' is not a fact ID; 'connapse memory list' or 'recall' shows them.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            var forgotten = await MemoryHelpers.WriteAsync(() =>
                client.Api.Memory.Facts[id].DeleteAsync(cancellationToken: ct))
                ?? throw new CliServerError("The server returned no result.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(forgotten));
                return CliExitCodes.Success;
            }

            AdminHelpers.WriteSuccess($"Forgot fact {id} ({forgotten.Deleted ?? 0} version(s) deleted).");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
