using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Response shape of GET /api/v1/auth/me. Matches the submodule's
/// Connapse.Core.MeResponse record.
/// </summary>
public sealed record MeResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("roles")] List<string> Roles,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt);
