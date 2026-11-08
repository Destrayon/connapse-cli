using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Memory;

/// <summary><c>connapse memory recall</c>: GET /api/memory/facts (memory.recall).</summary>
public static class MemoryRecallCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var queryArg = new Argument<string?>("query")
        {
            Description = "Text to look for in the facts. Leave it out to match by --subject and --attribute alone.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var spaceOption = new Option<string?>("--space")
        {
            Description = "Recall from this space only, by name or ID. Defaults to every space you can see.",
        };
        var subjectOption = new Option<string?>("--subject") { Description = "Only facts about this subject." };
        var attributeOption = new Option<string?>("--attribute") { Description = "Only facts about this attribute." };
        var asOfOption = new Option<string?>("--as-of")
        {
            Description = "Facts that were true at this ISO 8601 date or time, instead of now.",
        };
        var historyOption = new Option<bool>("--history") { Description = "Include versions that corrections replaced." };
        var limitOption = new Option<int?>("--limit") { Description = "How many facts to return. The server defaults to 10." };
        var cursorOption = new Option<string?>("--cursor")
        {
            Description = "The next page of a one-space recall, from the previous result.",
        };
        var jsonOption = MemoryHelpers.JsonOption();

        var cmd = new Command("recall", "Recall the facts that are true now, or at a given time.");
        cmd.Arguments.Add(queryArg);
        cmd.Options.Add(spaceOption);
        cmd.Options.Add(subjectOption);
        cmd.Options.Add(attributeOption);
        cmd.Options.Add(asOfOption);
        cmd.Options.Add(historyOption);
        cmd.Options.Add(limitOption);
        cmd.Options.Add(cursorOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var recalled = await client.Api.Memory.Facts.GetAsync(r =>
            {
                r.QueryParameters.Q = parseResult.GetValue(queryArg);
                r.QueryParameters.Space = parseResult.GetValue(spaceOption);
                r.QueryParameters.Subject = parseResult.GetValue(subjectOption);
                r.QueryParameters.Attribute = parseResult.GetValue(attributeOption);
                r.QueryParameters.AsOf = parseResult.GetValue(asOfOption);
                r.QueryParameters.IncludeHistory = parseResult.GetValue(historyOption) ? true : null;
                r.QueryParameters.Limit = parseResult.GetValue(limitOption);
                r.QueryParameters.Cursor = parseResult.GetValue(cursorOption);
            }, ct) ?? throw new CliServerError("The server returned no recall result.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(recalled));
                return CliExitCodes.Success;
            }

            var facts = recalled.Facts ?? [];
            if (facts.Count == 0)
                Console.WriteLine("No facts found.");

            var spaceNames = (recalled.Spaces ?? [])
                .Where(s => Guid.TryParse(s.Id, out _))
                .ToDictionary(s => Guid.Parse(s.Id!), s => s.Name);
            foreach (var recalledFact in facts)
            {
                if (recalledFact.Fact is not { } fact)
                    continue;
                var space = fact.ContainerId is { } id ? spaceNames.GetValueOrDefault(id) : null;
                MemoryHelpers.Print(fact, space, recalledFact.Sources);
            }

            if (recalled.SkippedSpaces is > 0)
                AdminHelpers.WriteWarning(
                    $"{recalled.SkippedSpaces} space(s) were not searched; pass --space to recall from one of them.");
            MemoryHelpers.PrintNextCursor(recalled.NextCursor);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
