using System.Text.Json.Serialization;

namespace Connapse.Cli.Models;

/// <summary>
/// Request body for POST /api/containers/{id}/reindex.
/// </summary>
public sealed record ReindexRequest(
    [property: JsonPropertyName("force")] bool Force,
    [property: JsonPropertyName("detectSettingsChanges")] bool DetectSettingsChanges);

/// <summary>
/// Response shape of POST /api/containers/{id}/reindex.
/// </summary>
public sealed record ReindexResult(
    [property: JsonPropertyName("batchId")] string? BatchId,
    [property: JsonPropertyName("totalDocuments")] int TotalDocuments,
    [property: JsonPropertyName("enqueuedCount")] int EnqueuedCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount,
    [property: JsonPropertyName("failedCount")] int FailedCount,
    [property: JsonPropertyName("reasonCounts")] Dictionary<string, int>? ReasonCounts,
    [property: JsonPropertyName("message")] string? Message);
