using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Memory;

/// <summary><c>connapse memory timeline</c>: GET /api/memory/timeline (memory.timeline).</summary>
public static class MemoryTimelineCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var queryArg = new Argument<string?>("query")
        {
            Description = "Text to look for in the facts. Leave it out to list them all.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var spaceOption = MemoryHelpers.SpaceOption("Read this space only, by name or ID. Defaults to every space you can see.");
        var subjectOption = new Option<string?>("--subject") { Description = "Only facts about this subject." };
        var attributeOption = new Option<string?>("--attribute") { Description = "Only facts about this attribute." };
        var fromOption = new Option<string?>("--from")
        {
            Description = "Only facts that started or ended at or after this ISO 8601 date or time.",
        };
        var toOption = new Option<string?>("--to")
        {
            Description = "Only facts that started or ended at or before this ISO 8601 date or time.",
        };
        var limitOption = new Option<int?>("--limit") { Description = "How many facts to return." };
        var cursorOption = new Option<string?>("--cursor")
        {
            Description = "The next (older) page of a one-space timeline, from the previous result.",
        };
        var jsonOption = MemoryHelpers.JsonOption();

        var cmd = new Command("timeline", "List facts in date order, including ended and corrected ones.");
        cmd.Arguments.Add(queryArg);
        cmd.Options.Add(spaceOption);
        cmd.Options.Add(subjectOption);
        cmd.Options.Add(attributeOption);
        cmd.Options.Add(fromOption);
        cmd.Options.Add(toOption);
        cmd.Options.Add(limitOption);
        cmd.Options.Add(cursorOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var timeline = await client.Api.Memory.Timeline.GetAsync(r =>
            {
                r.QueryParameters.Q = parseResult.GetValue(queryArg);
                r.QueryParameters.Space = parseResult.GetValue(spaceOption);
                r.QueryParameters.Subject = parseResult.GetValue(subjectOption);
                r.QueryParameters.Attribute = parseResult.GetValue(attributeOption);
                r.QueryParameters.From = parseResult.GetValue(fromOption);
                r.QueryParameters.To = parseResult.GetValue(toOption);
                r.QueryParameters.Limit = parseResult.GetValue(limitOption);
                r.QueryParameters.Cursor = parseResult.GetValue(cursorOption);
            }, ct) ?? throw new CliServerError("The server returned no timeline.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(timeline));
                return CliExitCodes.Success;
            }

            var facts = timeline.Facts ?? [];
            if (facts.Count == 0)
                Console.WriteLine("No facts found.");

            var spaceNames = (timeline.Spaces ?? [])
                .Where(s => Guid.TryParse(s.Id, out _))
                .ToDictionary(s => Guid.Parse(s.Id!), s => s.Name);
            foreach (var shown in facts)
            {
                if (shown.Fact is not { } fact)
                    continue;
                var space = fact.ContainerId is { } id ? spaceNames.GetValueOrDefault(id) : null;
                MemoryHelpers.Print(fact, space, shown.Sources);
            }

            if (timeline.SkippedSpaces is > 0)
                AdminHelpers.WriteWarning(
                    $"{timeline.SkippedSpaces} space(s) were not read; pass --space to read one of them.");
            MemoryHelpers.PrintNextCursor(timeline.NextCursor);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
