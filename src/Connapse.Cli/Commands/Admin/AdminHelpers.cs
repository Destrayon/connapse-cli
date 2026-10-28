using System.CommandLine;
using System.Net;
using System.Text.Json;
using Connapse.Cli.Services;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Connapse.Cli.Commands.Admin;

/// <summary>
/// Shared by the admin commands (sources, connections, settings, agents): the token scope each area
/// needs, name-or-ID lookup, JSON input and output, and the hint shown when the server refuses.
/// </summary>
internal static class AdminHelpers
{
    public const string SourcesScope = "admin:sources";
    public const string UsersScope = "admin:users";
    public const string SettingsScope = "admin:settings";

    /// <summary>The page size used when a name is looked up by walking a list.</summary>
    internal const int LookupPageSize = 100;

    /// <summary>
    /// Runs an admin call. A 403 is rethrown with what the server requires: the Owner or Admin role,
    /// and for a token, the area's scope. Other errors pass through unchanged.
    /// </summary>
    public static async Task<T> AdminAsync<T>(string scope, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            throw Forbidden(ex, scope);
        }
    }

    /// <inheritdoc cref="AdminAsync{T}(string, Func{Task{T}})"/>
    public static async Task AdminAsync(string scope, Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            throw Forbidden(ex, scope);
        }
    }

    internal static ApiRequestException Forbidden(ApiRequestException ex, string scope) =>
        new(ex.StatusCode,
            $"{ex.Message}{Environment.NewLine}  This needs the Owner or Admin role; a token also needs the '{scope}' scope.",
            ex.ResponseBody, ex.Problem);

    /// <summary>
    /// Resolves a name or ID to an ID. A GUID is used as given; a name is matched exactly, ignoring
    /// case, against every page of the list. No match, or more than one, is a validation error.
    /// </summary>
    public static async Task<Guid> ResolveIdAsync<T>(
        string nameOrId,
        string kind,
        Func<int, int, Task<(IReadOnlyList<T> Items, bool HasMore)>> page,
        Func<T, string?> name,
        Func<T, Guid?> id)
    {
        if (Guid.TryParse(nameOrId, out var parsed))
            return parsed;

        var matches = new List<Guid>();
        for (int skip = 0; ; skip += LookupPageSize)
        {
            var (items, hasMore) = await page(skip, LookupPageSize);
            foreach (var item in items)
            {
                if (string.Equals(name(item), nameOrId, StringComparison.OrdinalIgnoreCase) && id(item) is { } found)
                    matches.Add(found);
            }
            if (!hasMore || items.Count == 0)
                break;
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliValidationError($"No {kind} named '{nameOrId}'."),
            _ => throw new CliValidationError($"More than one {kind} is named '{nameOrId}'; pass its ID instead."),
        };
    }

    /// <summary>The root node of a JSON document, for parsing into a generated model.</summary>
    public static JsonParseNode ParseJson(string json, string optionName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return new JsonParseNode(doc.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            throw new CliValidationError($"{optionName} is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>A JSON object as an untyped node; anything else is a validation error.</summary>
    public static UntypedNode ParseJsonObject(string json, string optionName)
    {
        var node = ParseJson(json, optionName);
        if (node.GetObjectValue(UntypedNode.CreateFromDiscriminatorValue) is not UntypedObject obj)
            throw new CliValidationError($"{optionName} must be a JSON object.");
        return obj;
    }

    /// <summary>A generated model as indented JSON, the way the server would send it.</summary>
    public static string ToJson(IParsable value)
    {
        using var writer = new JsonSerializationWriter();
        writer.WriteObjectValue(null, value);
        using var stream = writer.GetSerializedContent();
        using var doc = JsonDocument.Parse(stream);
        return JsonSerializer.Serialize(doc.RootElement, IndentedJson);
    }

    /// <summary>An untyped node as indented JSON.</summary>
    public static string ToJson(UntypedNode value)
    {
        using var writer = new JsonSerializationWriter();
        writer.WriteObjectValue(null, value);
        using var stream = writer.GetSerializedContent();
        using var doc = JsonDocument.Parse(stream);
        return JsonSerializer.Serialize(doc.RootElement, IndentedJson);
    }

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>A generated enum's value by name, ignoring case; the generator's null placeholder isn't one.</summary>
    public static T ParseEnum<T>(string value, string optionName) where T : struct, Enum
    {
        var names = EnumNames<T>();
        var match = names.FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new CliValidationError($"{optionName} must be one of: {string.Join(", ", names)}.");
        return Enum.Parse<T>(match);
    }

    public static IReadOnlyList<string> EnumNames<T>() where T : struct, Enum =>
        Enum.GetNames<T>().Where(n => !n.StartsWith("OpenapiJsonNullSentinel", StringComparison.Ordinal)).ToList();

    /// <summary>Reads a secret from stdin, as for --token-stdin; a trailing newline is dropped.</summary>
    public static string ReadSecretFromStdin(string optionName)
    {
        var value = Console.In.ReadToEnd().TrimEnd('\r', '\n');
        if (value.Length == 0)
            throw new CliValidationError($"{optionName}: nothing was read from stdin.");
        return value;
    }

    public static string ReadFile(string path, string optionName)
    {
        if (!File.Exists(path))
            throw new CliValidationError($"{optionName}: file '{path}' not found.");
        return File.ReadAllText(path);
    }

    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static string Date(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
}

/// <summary>
/// A JSON object given inline (<c>--config '{...}'</c>) or read from a file (<c>--config-file</c>).
/// A separate option, because System.CommandLine reads an <c>@path</c> argument as a response file.
/// </summary>
internal sealed class JsonObjectOptions
{
    public JsonObjectOptions(string name, string description)
    {
        Inline = new Option<string?>($"--{name}") { Description = $"{description} A JSON object." };
        File = new Option<string?>($"--{name}-file") { Description = $"A file holding --{name}'s JSON." };
    }

    public Option<string?> Inline { get; }
    public Option<string?> File { get; }

    public void AddTo(Command command)
    {
        command.Options.Add(Inline);
        command.Options.Add(File);
    }

    /// <summary>The object given, or null when neither option was.</summary>
    public UntypedNode? Read(ParseResult parseResult)
    {
        var inline = parseResult.GetValue(Inline);
        var path = parseResult.GetValue(File);
        if (inline is not null && path is not null)
            throw new CliValidationError($"Pass {Inline.Name} or {File.Name}, not both.");
        if (inline is not null)
            return AdminHelpers.ParseJsonObject(inline, Inline.Name);
        if (path is not null)
            return AdminHelpers.ParseJsonObject(AdminHelpers.ReadFile(path, File.Name), File.Name);
        return null;
    }
}
