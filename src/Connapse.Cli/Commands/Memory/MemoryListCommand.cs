using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Memory;

/// <summary><c>connapse memory list</c>: GET /api/memory/spaces/{id}/facts (memory.list).</summary>
public static class MemoryListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var spaceOption = MemoryHelpers.SpaceOption(
            "Required: the space to list, by name or ID, e.g. 'team-notes'.");
        var queryOption = new Option<string?>("--query") { Description = "Only facts containing this text." };
        var subjectOption = new Option<string?>("--subject") { Description = "Only facts about this subject." };
        var statusOption = new Option<string?>("--status")
        {
            Description = "'current' (the default) for facts true now, or 'all' to include replaced versions.",
        };
        var sortOption = new Option<string?>("--sort")
        {
            Description = "'recorded' (newest first, the default), 'subject', 'validFrom', or 'relevance' (needs --query).",
        };
        var limitOption = new Option<int?>("--limit") { Description = "How many facts to show on this page." };
        var cursorOption = new Option<string?>("--cursor") { Description = "The next page, from the previous result." };
        var jsonOption = MemoryHelpers.JsonOption();

        var cmd = new Command("list", "List the facts in a memory space, a page at a time.");
        cmd.Options.Add(spaceOption);
        cmd.Options.Add(queryOption);
        cmd.Options.Add(subjectOption);
        cmd.Options.Add(statusOption);
        cmd.Options.Add(sortOption);
        cmd.Options.Add(limitOption);
        cmd.Options.Add(cursorOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var space = await MemoryHelpers.RequireSpaceAsync(parseResult, spaceOption, client, ct);
            var page = await client.Api.Memory.Spaces[space].Facts.GetAsync(r =>
            {
                r.QueryParameters.Q = parseResult.GetValue(queryOption);
                r.QueryParameters.Subject = parseResult.GetValue(subjectOption);
                r.QueryParameters.Status = parseResult.GetValue(statusOption);
                r.QueryParameters.Sort = parseResult.GetValue(sortOption);
                r.QueryParameters.Limit = parseResult.GetValue(limitOption);
                r.QueryParameters.Cursor = parseResult.GetValue(cursorOption);
            }, ct) ?? throw new CliServerError("The server returned no facts page.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(page));
                return CliExitCodes.Success;
            }

            var facts = page.Facts ?? [];
            if (facts.Count == 0)
                Console.WriteLine($"No facts in {space}.");
            foreach (var listed in facts)
            {
                if (listed.Fact is { } fact)
                    MemoryHelpers.Print(fact, space: null, listed.Sources);
            }

            MemoryHelpers.PrintNextCursor(page.NextCursor);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
