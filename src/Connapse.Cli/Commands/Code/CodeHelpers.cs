using System.CommandLine;
using System.Globalization;
using Connapse.Cli.Services;
using Connapse.Client.Models;

namespace Connapse.Cli.Commands.Code;

/// <summary>The options the code commands share and how their answers are printed.</summary>
internal static class CodeHelpers
{
    public static Option<string> ContainerOption() => new("--container")
    {
        Description = "The container or source whose indexed code to read, by name or ID.",
        Required = true,
    };

    public static Option<bool> JsonOption() => new("--json") { Description = "Print the server's JSON." };

    public static Option<int?> LimitOption(string what, int max, int serverDefault) => new("--limit")
    {
        Description = $"How many {what} to return (1-{max}). The server defaults to {serverDefault}.",
    };

    /// <summary>Fails when <paramref name="value"/> is given and outside <paramref name="min"/>..<paramref name="max"/>.</summary>
    public static int? InRange(int? value, string optionName, int min, int max) =>
        value is { } v && (v < min || v > max)
            ? throw new CliValidationError($"{optionName} must be between {min} and {max}.")
            : value;

    /// <summary>Fails unless <paramref name="value"/> is one of <paramref name="allowed"/>; gives it in lower case.</summary>
    public static string? OneOf(string? value, string optionName, params string[] allowed)
    {
        if (value is null)
            return null;
        var lower = value.Trim().ToLowerInvariant();
        return allowed.Contains(lower)
            ? lower
            : throw new CliValidationError($"{optionName} must be {string.Join(", ", allowed[..^1])} or {allowed[^1]}, not '{value}'.");
    }

    /// <summary>The server's note on what the answer covers, such as files it could not read.</summary>
    public static void PrintNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(notice);
        Console.ResetColor();
    }

    public static void PrintHeading(string heading)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(heading);
        Console.ResetColor();
    }

    /// <summary>A definition in two lines: kind, name and where it is, then its signature.</summary>
    public static void PrintDefinition(CodeDefinitionShown definition)
    {
        string lines = definition.EndLine is { } end && end != definition.StartLine
            ? $"{definition.StartLine}-{end}"
            : $"{definition.StartLine}";
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{definition.Kind} {definition.Symbol}");
        Console.ResetColor();
        Console.WriteLine($"  {definition.Path}:{lines}{(string.IsNullOrEmpty(definition.Language) ? "" : $" ({definition.Language})")}");
        if (!string.IsNullOrWhiteSpace(definition.Signature))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {definition.Signature}");
            Console.ResetColor();
        }
    }

    /// <summary>A file and why it was shown, then its matching lines.</summary>
    public static void PrintFile(CodeFileShown file)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(file.Path);
        Console.ResetColor();
        Console.WriteLine(string.IsNullOrEmpty(file.Reason) ? "" : $"  ({file.Reason})");
        foreach (var line in file.Lines ?? [])
            Console.WriteLine(line.Line is { } n ? $"  {n,5}: {line.Text}" : $"         {line.Text}");
    }

    public static void PrintFiles(IReadOnlyList<CodeFileShown>? files, string heading)
    {
        if (files is not { Count: > 0 })
            return;
        PrintHeading(heading);
        foreach (var file in files)
            PrintFile(file);
    }

    public static void PrintDefinitions(IReadOnlyList<CodeDefinitionShown>? definitions, string none)
    {
        if (definitions is not { Count: > 0 })
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(none);
            Console.ResetColor();
            return;
        }
        foreach (var definition in definitions)
            PrintDefinition(definition);
    }

    public static string Confidence(double? confidence) =>
        confidence is { } c ? c.ToString("0.##", CultureInfo.InvariantCulture) : "-";
}
