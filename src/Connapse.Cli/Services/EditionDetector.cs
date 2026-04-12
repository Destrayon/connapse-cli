using System.Net;
using System.Net.Http.Json;
using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// Abstraction over edition detection so consumers (e.g. ApiClient) don't
/// depend on the concrete detector and its HTTP cache logic.
/// </summary>
public interface IEditionProvider
{
    Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default);
}

/// <summary>
/// Detects whether the server is Cloud or self-hosted by calling
/// GET /api/v1/info. Caches the result in config.json and re-checks
/// after 7 days.
///
/// Uses its own HttpClient (not ApiClient) to break the circular dependency
/// between ApiClient and EditionDetector.
/// </summary>
public sealed class EditionDetector : IEditionProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient;
    private readonly ConfigStore _configStore;
    private readonly IClock _clock;

    public EditionDetector(HttpClient httpClient, ConfigStore configStore, IClock clock)
    {
        _httpClient = httpClient;
        _configStore = configStore;
        _clock = clock;
    }

    public async Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var config = _configStore.Load();

        if (!forceRefresh &&
            config.Edition.HasValue &&
            config.Edition.Value != ServerEdition.Unknown &&
            config.EditionCheckedAt.HasValue &&
            _clock.UtcNow - config.EditionCheckedAt.Value < CacheLifetime)
        {
            return config.Edition.Value;
        }

        ServerEdition edition;
        try
        {
            using var response = await _httpClient.GetAsync("/api/v1/info", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                edition = ServerEdition.SelfHosted;
            }
            else if (response.IsSuccessStatusCode)
            {
                var info = await response.Content.ReadFromJsonAsync<ServerInfo>(cancellationToken: ct);
                edition = info?.Edition?.ToLowerInvariant() switch
                {
                    "cloud" => ServerEdition.Cloud,
                    "self-hosted" => ServerEdition.SelfHosted,
                    _ => ServerEdition.Unknown,
                };
            }
            else
            {
                // Server is reachable but returned an unexpected status. Don't
                // cache — we'll try again next invocation.
                return ServerEdition.Unknown;
            }
        }
        catch (HttpRequestException)
        {
            // Can't reach the server. Don't cache.
            return ServerEdition.Unknown;
        }

        _configStore.Save(_configStore.Load() with
        {
            Edition = edition,
            EditionCheckedAt = _clock.UtcNow,
        });

        return edition;
    }
}
