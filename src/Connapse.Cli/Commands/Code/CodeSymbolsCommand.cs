using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Code;

/// <summary><c>connapse code symbols</c>: GET /api/code/symbols (code.findSymbol).</summary>
public static class CodeSymbolsCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var queryArg = new Argument<string>("query")
        {
            Description = "The symbol's name as far as you know it (Post, Ledger.Post), words in its name, or a description with --match meaning.",
        };
        var containerOption = CodeHelpers.ContainerOption();
        var matchOption = new Option<string?>("--match")
        {
            Description = "How to match the query: exact (the default), words (every word appears in the name) or meaning (a description).",
        };
        var followOption = new Option<bool>("--follow")
        {
            Description = "Also show what the first definition calls, constructs or inherits, and the files defining those.",
        };
        var limitOption = CodeHelpers.LimitOption("definitions", 100, 10);
        var jsonOption = CodeHelpers.JsonOption();

        var cmd = new Command("symbols", "Find where a function, method, class or other symbol is defined in indexed code.");
        cmd.Arguments.Add(queryArg);
        cmd.Options.Add(containerOption);
        cmd.Options.Add(matchOption);
        cmd.Options.Add(followOption);
        cmd.Options.Add(limitOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var match = CodeHelpers.OneOf(parseResult.GetValue(matchOption), "--match", "exact", "words", "meaning");
            var limit = CodeHelpers.InRange(parseResult.GetValue(limitOption), "--limit", 1, 100);

            var client = services.GetRequiredService<ConnapseApiClient>();
            var answer = await client.Api.Code.Symbols.GetAsync(r =>
            {
                r.QueryParameters.ContainerId = parseResult.GetRequiredValue(containerOption);
                r.QueryParameters.Query = parseResult.GetRequiredValue(queryArg);
                r.QueryParameters.Match = match;
                r.QueryParameters.Follow = parseResult.GetValue(followOption) ? true : null;
                r.QueryParameters.Limit = limit;
            }, ct) ?? throw new CliServerError("The server returned no answer.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(answer));
                return CliExitCodes.Success;
            }

            CodeHelpers.PrintNotice(answer.Notice);
            CodeHelpers.PrintDefinitions(answer.Definitions, "No definitions found.");
            if (answer.Uses is { Count: > 0 } uses)
            {
                CodeHelpers.PrintHeading("It uses:");
                foreach (var use in uses)
                    Console.WriteLine($"  {use.Type} {use.Kind} {use.Symbol}  {use.Path}:{use.Line}  (confidence {CodeHelpers.Confidence(use.Confidence)})");
            }
            CodeHelpers.PrintFiles(answer.Files, "Files:");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
