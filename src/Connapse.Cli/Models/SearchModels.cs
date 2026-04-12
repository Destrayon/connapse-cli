using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Response shape of GET /api/containers/{id}/search.
/// </summary>
public sealed record SearchResult(
    [property: JsonPropertyName("hits")] List<SearchHit> Hits,
    [property: JsonPropertyName("totalMatches")] int TotalMatches,
    [property: JsonPropertyName("duration")] TimeSpan Duration);

public sealed record SearchHit(
    [property: JsonPropertyName("chunkId")] string ChunkId,
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("score")] float Score,
    [property: JsonPropertyName("metadata")] Dictionary<string, string> Metadata);
