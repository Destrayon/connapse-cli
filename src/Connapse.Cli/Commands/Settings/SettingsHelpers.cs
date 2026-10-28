using System.CommandLine;
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

    public static async Task<JsonObject> GetAsync(ApiClient api, string category, CancellationToken ct)
    {
        using var response = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
            () => api.SendRawAsync(HttpMethod.Get, Path(category), content: null, ct));
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(text) as JsonObject
            ?? throw new CliServerError($"The server's '{category}' settings aren't a JSON object.");
    }

    public static async Task<JsonObject> PutAsync(ApiClient api, string category, JsonObject settings, CancellationToken ct)
    {
        using var content = new StringContent(settings.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await AdminHelpers.AdminAsync(AdminHelpers.SettingsScope,
            () => api.SendRawAsync(HttpMethod.Put, Path(category), content, ct));
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
