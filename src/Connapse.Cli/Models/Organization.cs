using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Response shape item from GET /api/v1/organizations.
/// Matches Connapse.Cloud/Endpoints/OrganizationEndpoints.cs.
/// </summary>
public sealed record Organization(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt);
