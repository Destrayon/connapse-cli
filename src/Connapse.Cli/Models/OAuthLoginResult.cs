namespace Connapse.Cli.Models;

/// <summary>
/// Outcome of a successful OAuth browser login.
/// </summary>
public sealed record OAuthLoginResult(
    string ServerUrl,
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);
