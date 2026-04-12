using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Request body for POST /api/v1/auth/pats.
/// </summary>
public sealed record PatCreateRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("scopes")] string[]? Scopes,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt);

/// <summary>
/// Response shape of POST /api/v1/auth/pats. The plaintext Token is only
/// included in the create response — list responses expose TokenPrefix only.
/// </summary>
public sealed record PatCreateResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("scopes")] string[] Scopes,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt);

/// <summary>
/// Response shape item from GET /api/v1/auth/pats.
/// </summary>
public sealed record PatListItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tokenPrefix")] string TokenPrefix,
    [property: JsonPropertyName("scopes")] string[] Scopes,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt,
    [property: JsonPropertyName("lastUsedAt")] DateTime? LastUsedAt,
    [property: JsonPropertyName("isRevoked")] bool IsRevoked);
