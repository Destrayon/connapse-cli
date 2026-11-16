using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Code;

/// <summary><c>connapse code impact</c>: GET /api/code/impact (code.impact).</summary>
public static class CodeImpactCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var symbolArg = new Argument<string>("symbol") { Description = "The symbol you plan to change, such as Ledger.Post." };
        var containerOption = CodeHelpers.ContainerOption();
        var depthOption = new Option<int?>("--depth")
        {
            Description = "How many hops of callers and subtypes to follow (1-3). The server defaults to 2.",
        };
        var limitOption = CodeHelpers.LimitOption("symbols and files", 200, 30);
        var jsonOption = CodeHelpers.JsonOption();

        var cmd = new Command("impact",
            "Show what a change to a symbol could affect: what calls or inherits it, directly or through others, and which tests cover those.");
        cmd.Arguments.Add(symbolArg);
        cmd.Options.Add(containerOption);
        cmd.Options.Add(depthOption);
        cmd.Options.Add(limitOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var depth = CodeHelpers.InRange(parseResult.GetValue(depthOption), "--depth", 1, 3);
            var limit = CodeHelpers.InRange(parseResult.GetValue(limitOption), "--limit", 1, 200);

            var client = services.GetRequiredService<ConnapseApiClient>();
            var answer = await client.Api.Code.Impact.GetAsync(r =>
            {
                r.QueryParameters.ContainerId = parseResult.GetRequiredValue(containerOption);
                r.QueryParameters.Symbol = parseResult.GetRequiredValue(symbolArg);
                r.QueryParameters.Depth = depth;
                r.QueryParameters.Limit = limit;
            }, ct) ?? throw new CliServerError("The server returned no answer.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(answer));
                return CliExitCodes.Success;
            }

            CodeHelpers.PrintNotice(answer.Notice);
            CodeHelpers.PrintDefinitions(answer.Definitions, "No definition found.");
            CodeHelpers.PrintHeading("Affected:");
            if (answer.Affected is not { Count: > 0 } affected)
                Console.WriteLine("  Nothing the code index links to it.");
            else
                foreach (var item in affected.OrderBy(a => a.Hops))
                    Console.WriteLine($"  {item.Hops} hop{(item.Hops == 1 ? "" : "s")}  {item.Kind} {item.Symbol}  {item.Path}" +
                                      $"{(item.IsTest == true ? "  [test]" : "")}  (confidence {CodeHelpers.Confidence(item.Confidence)})");
            if (answer.Tests is { Count: > 0 } tests)
            {
                CodeHelpers.PrintHeading("Tests:");
                foreach (var test in tests)
                    Console.WriteLine($"  {test}");
            }
            CodeHelpers.PrintFiles(answer.Files, "Files:");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
