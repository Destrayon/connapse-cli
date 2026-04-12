using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Matches the server's ContainerInfo DTO from /api/containers endpoints.
/// </summary>
public sealed record ContainerInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt,
    [property: JsonPropertyName("documentCount")] int DocumentCount);

/// <summary>
/// Paged wrapper for GET /api/containers?skip=&amp;take=.
/// </summary>
public sealed record PagedContainerResponse(
    [property: JsonPropertyName("items")] List<ContainerInfo> Items,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("hasMore")] bool HasMore);
