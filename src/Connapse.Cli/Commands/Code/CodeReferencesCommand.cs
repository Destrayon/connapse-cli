using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Code;

/// <summary><c>connapse code references</c>: GET /api/code/references (code.references).</summary>
public static class CodeReferencesCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var symbolArg = new Argument<string>("symbol") { Description = "The symbol whose uses to find, such as Ledger.Post." };
        var containerOption = CodeHelpers.ContainerOption();
        var kindOption = new Option<string?>("--kind")
        {
            Description = "calls for callers only, inherits for subtypes only, or all (the default) for every use and mention.",
        };
        var limitOption = CodeHelpers.LimitOption("files", 200, 20);
        var jsonOption = CodeHelpers.JsonOption();

        var cmd = new Command("references", "List the files that use a symbol, best evidence first.");
        cmd.Arguments.Add(symbolArg);
        cmd.Options.Add(containerOption);
        cmd.Options.Add(kindOption);
        cmd.Options.Add(limitOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var kind = CodeHelpers.OneOf(parseResult.GetValue(kindOption), "--kind", "calls", "inherits", "all");
            var limit = CodeHelpers.InRange(parseResult.GetValue(limitOption), "--limit", 1, 200);

            var client = services.GetRequiredService<ConnapseApiClient>();
            var answer = await client.Api.Code.References.GetAsync(r =>
            {
                r.QueryParameters.ContainerId = parseResult.GetRequiredValue(containerOption);
                r.QueryParameters.Symbol = parseResult.GetRequiredValue(symbolArg);
                r.QueryParameters.Kind = kind;
                r.QueryParameters.Limit = limit;
            }, ct) ?? throw new CliServerError("The server returned no answer.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(answer));
                return CliExitCodes.Success;
            }

            CodeHelpers.PrintNotice(answer.Notice);
            CodeHelpers.PrintDefinitions(answer.Definitions, "No definition found.");
            if (answer.Files is not { Count: > 0 })
                Console.WriteLine("No files use it.");
            CodeHelpers.PrintFiles(answer.Files, "Used in:");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
