using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client.Models;

namespace Connapse.Cli.Commands.Memory;

/// <summary>The options the memory commands share, the hint shown when the server refuses, and how a fact is printed.</summary>
internal static class MemoryHelpers
{
    /// <summary>The caller's own personal space, which the server makes on first use.</summary>
    public const string PersonalSpace = "my-memory";

    public static Option<string> SpaceOption(string description) => new("--space")
    {
        Description = description,
        DefaultValueFactory = _ => PersonalSpace,
    };

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
