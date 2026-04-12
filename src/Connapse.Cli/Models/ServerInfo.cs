using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Response shape of GET /api/v1/info.
/// </summary>
public sealed record ServerInfo(
    [property: JsonPropertyName("edition")] string Edition,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("features")] List<string>? Features = null);
