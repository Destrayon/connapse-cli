using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;

namespace Connapse.Cli.Commands.Settings;

/// <summary>
/// Reads and writes a settings category as raw JSON. The generated client types these as an anyOf of
/// every category's model, and its intersection parse would drop fields none of them declare; a PUT
/// replaces the whole category, so a dropped field would quietly reset to its default.
/// </summary>
internal static class SettingsHelpers
{
    public const string Categories = "embedding, chunking, search, llm, upload";

    public static Argument<string> CategoryArgument() =>
        new("category") { Description = $"The settings category: {Categories}." };

    public static async Task<JsonObject> GetAsync(ApiClient api, string category, CancellationToken ct) =>
        (await ReadAsync(api, category, ct)).Settings;

    /// <summary>
    /// The settings and their version: the ETag the server sent, or null when it sent none (a server from before
    /// versioned saves) or only a weak one, which If-Match never matches (RFC 9110 §13.1.1).
    /// </summary>
    public static async Task<(JsonObject Settings, EntityTagHeaderValue? Version)> ReadAsync(ApiClient api, string category, CancellationToken ct)
    {
        using var response = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
            () => api.SendRawAsync(HttpMethod.Get, Path(category), content: null, ct));
        var text = await response.Content.ReadAsStringAsync(ct);
        var settings = JsonNode.Parse(text) as JsonObject
            ?? throw new CliServerError($"The server's '{category}' settings aren't a JSON object.");
        return (settings, response.Headers.ETag is { IsWeak: false } etag ? etag : null);
    }

    /// <summary>
    /// Saves the category. With <paramref name="version"/>, only if the settings still have it: the server
    /// answers 412 Precondition Failed when someone saved them since, and stores nothing.
    /// </summary>
    public static async Task<JsonObject> PutAsync(
        ApiClient api, string category, JsonObject settings, CancellationToken ct, EntityTagHeaderValue? version = null)
    {
        using var content = new StringContent(settings.ToJsonString(), Encoding.UTF8, "application/json");
        // HttpRequestHeaders has no public constructor; a message's own is the way to build one.
        using var conditions = new HttpRequestMessage();
        if (version is not null)
            conditions.Headers.IfMatch.Add(version);
        using var response = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
            () => api.SendRawAsync(HttpMethod.Put, Path(category), content, ct, conditions.Headers));
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonObject ?? [];
    }

    /// <summary>A value that reads as JSON (a number, true, null, an object) is sent as that; anything else as a string.</summary>
    public static JsonNode? ParseValue(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }

    public static string Indented(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static string Path(string category) => $"/api/settings/{Uri.EscapeDataString(category)}";
}
