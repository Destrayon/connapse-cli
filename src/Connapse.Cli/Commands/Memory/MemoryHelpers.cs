using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;

namespace Connapse.Cli.Commands.Memory;

/// <summary>The options the memory commands share, the hint shown when the server refuses, and how a fact is printed.</summary>
internal static class MemoryHelpers
{
    public static Option<string?> SpaceOption(string description) => new("--space") { Description = description };

    /// <summary>
    /// The space <paramref name="spaceOption"/> names. The server has no default space, so when it is missing
    /// this fails before the command's own request, naming the spaces the caller can see when that lookup works.
    /// </summary>
    public static async Task<string> RequireSpaceAsync(
        ParseResult parseResult, Option<string?> spaceOption, ConnapseApiClient client, CancellationToken ct)
    {
        var space = parseResult.GetValue(spaceOption);
        if (!string.IsNullOrWhiteSpace(space))
            return space;

        var message = "Pass --space <name-or-id> to say which memory space to use.";
        try
        {
            var page = await client.Api.Memory.Spaces.GetAsync(r => r.QueryParameters.Limit = 20, ct);
            var names = (page?.Items ?? []).Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            message += names.Count > 0
                ? $"{Environment.NewLine}  Spaces you can see: {string.Join(", ", names)}"
                : $"{Environment.NewLine}  You can't see any memory spaces yet.";
            if (!string.IsNullOrEmpty(page?.NextCursor))
                message += $"{Environment.NewLine}  More exist; 'connapse api GET /api/memory/spaces' lists them all.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The list is only a hint; the missing option is the error.
        }

        throw new CliValidationError(message);
    }

    public static Option<bool> JsonOption() => new("--json") { Description = "Print the server's JSON." };

    /// <summary>
    /// Runs a write. A 403 is rethrown with what the server requires: the Editor role, and for a token the
    /// knowledge:write scope. Other errors pass through unchanged.
    /// </summary>
    public static async Task<T> WriteAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new ApiRequestException(ex.StatusCode,
                $"{ex.Message}{Environment.NewLine}  Saving or forgetting a fact needs the Editor role; a token also needs the 'knowledge:write' scope.",
                ex.ResponseBody, ex.Problem);
        }
    }

    /// <summary>
    /// One fact in two lines: what it says, then its ID, space, dates and sources. <paramref name="space"/> is
    /// the name of the space it is in, when known.
    /// </summary>
    public static void Print(MemoryFact fact, string? space, IReadOnlyList<ShownMemorySource>? sources = null)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{fact.Subject} / {fact.Attribute} = {fact.Value}");
        Console.ResetColor();

        var details = new List<string> { $"{fact.Id}" };
        if (!string.IsNullOrEmpty(space))
            details.Add(space);
        if (fact.ValidFrom is not null || fact.ValidTo is not null)
            details.Add($"valid {AdminHelpers.Date(fact.ValidFrom)} to {AdminHelpers.Date(fact.ValidTo)}");
        details.Add($"recorded {AdminHelpers.Date(fact.RecordedAt)}");
        if (fact.SupersededAt is not null)
            details.Add($"superseded {AdminHelpers.Date(fact.SupersededAt)}");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {string.Join(" · ", details)}");
        if (!string.IsNullOrEmpty(fact.Reason))
            Console.WriteLine($"  Reason: {fact.Reason}");
        var cited = sources is { Count: > 0 }
            ? sources.Select(s => string.IsNullOrEmpty(s.Title) ? s.Ref : $"{s.Title} ({s.Ref})")
            : fact.Sources?.Select(s => s.Ref) ?? [];
        var citedList = cited.Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (citedList.Count > 0)
            Console.WriteLine($"  Sources: {string.Join(", ", citedList)}");
        Console.ResetColor();
    }

    /// <summary>The hint that prints the next page, when there is one.</summary>
    public static void PrintNextCursor(string? cursor)
    {
        if (!string.IsNullOrEmpty(cursor))
            Console.WriteLine($"More facts: pass --cursor {cursor}");
    }
}
