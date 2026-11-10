using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Commands.Approval;
using Connapse.Cli.Services;

namespace Connapse.Cli.Commands.Memory;

/// <summary><c>connapse memory forget</c>: the memory_forget tool on /mcp, so the server can hold it for the owner's approval.</summary>
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

            bool json = parseResult.GetValue(jsonOption);
            var result = await ApprovalHelpers.CallToolAsync(services, "memory_forget",
                new Dictionary<string, string> { ["factId"] = id }, ct);
            if (result.HeldRequestId is { } held)
                return ApprovalHelpers.WriteHeld(services, held, json);

            // The tool says "Forgot fact '<id>' (<n> version(s) deleted)."; --json keeps the REST route's {"deleted": n}.
            var count = Regex.Match(result.Text, @"\((\d+) version");
            int deleted = count.Success ? int.Parse(count.Groups[1].Value) : 0;
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { deleted }));
                return CliExitCodes.Success;
            }

            AdminHelpers.WriteSuccess($"Forgot fact {id} ({deleted} version(s) deleted).");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
