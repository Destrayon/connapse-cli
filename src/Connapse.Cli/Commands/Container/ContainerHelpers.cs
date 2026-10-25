using System.Net;
using Connapse.Cli.Services;
using Connapse.Client;

namespace Connapse.Cli.Commands.Container;

/// <summary>
/// Shared utilities for container commands: name → ID resolution and
/// byte formatting.
/// </summary>
internal static class ContainerHelpers
{
    /// <summary>
    /// Resolves a container name to its ID by calling
    /// <c>GET /api/containers/by-name/{name}</c>. If the argument already
    /// parses as a GUID, it's returned unchanged (caller may pass either).
    /// Returns null on 404 so the caller can produce a friendly message.
    /// </summary>
    public static async Task<Guid?> ResolveIdAsync(
        ConnapseApiClient client,
        string nameOrId,
        CancellationToken ct)
    {
        if (Guid.TryParse(nameOrId, out var id))
            return id;

        try
        {
            var info = await client.Api.Containers.ByName[nameOrId.ToLowerInvariant()].GetAsync(cancellationToken: ct);
            return Guid.TryParse(info?.Id, out var found) ? found : null;
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Formats a byte count as a human-readable string (e.g. "1.23 MB").
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
