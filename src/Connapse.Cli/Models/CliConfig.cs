namespace Connapse.Cli.Models;

/// <summary>
/// Non-sensitive preferences stored in ~/.connapse/config.json.
/// Kept separate from credentials so the secrets file can have tighter permissions.
/// </summary>
public sealed record CliConfig
{
    public string? ApiBaseUrl { get; init; }
    public string? DefaultOrgSlug { get; init; }
    public ServerEdition? Edition { get; init; }
    public DateTime? EditionCheckedAt { get; init; }
}
