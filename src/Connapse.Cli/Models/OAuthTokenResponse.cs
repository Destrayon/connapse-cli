using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Standard OAuth 2.0 token endpoint response.
/// </summary>
public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string? Scope = null);
