namespace Connapse.Cli.Models;

/// <summary>
/// Represents stored user credentials in ~/.connapse/credentials.json.
/// </summary>
public sealed record CliCredentials(
    string ApiKey,
    string ApiBaseUrl,
    string UserEmail,
    string? RefreshToken = null,
    DateTime? ExpiresAt = null);
