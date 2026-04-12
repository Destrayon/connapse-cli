using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
