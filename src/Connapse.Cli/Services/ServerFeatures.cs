using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// What the configured server supports (CON-129). Servers from 0.4 on describe themselves at
/// GET /api/v1/info; older ones don't have that route. A feature the server lacks is found when a
/// request reaches no route, which <see cref="IsMissingRouteAsync"/> tells apart from a missing record.
/// </summary>
public sealed partial class ServerFeatures(IHttpClientFactory httpClientFactory)
{
    private Task<ServerInfo?>? _info;

    /// <summary>
    /// The server's /api/v1/info, read once per run. Null when the server doesn't have the route,
    /// because it predates it. Throws <see cref="HttpRequestException"/> when the server can't be
    /// reached or answers with anything else.
    /// </summary>
    public Task<ServerInfo?> GetInfoAsync(CancellationToken ct = default) => _info ??= ReadInfoAsync(ct);

    private async Task<ServerInfo?> ReadInfoAsync(CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(HttpClientNames.ConnapseServer);
        using var response = await client.GetAsync("/api/v1/info", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        try
        {
            return await response.Content.ReadFromJsonAsync<ServerInfo>(cancellationToken: ct)
                ?? throw new HttpRequestException("The server's /api/v1/info was empty.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new HttpRequestException("The server's /api/v1/info isn't the JSON the CLI expects.", ex);
        }
    }

    /// <summary>
    /// Whether a reply means the server has no such route. A 405 always does: routing knows the path
    /// but not the method. A 404 with an empty body may also be an endpoint's own "not found", so the
    /// path is asked for with OPTIONS: routing answers a path it knows with 405, and one it doesn't
    /// with 404.
    /// </summary>
    public async Task<bool> IsMissingRouteAsync(
        HttpStatusCode status, string body, Func<CancellationToken, Task<HttpResponseMessage>> askOptions, CancellationToken ct)
    {
        if (status == HttpStatusCode.MethodNotAllowed)
            return true;
        if (status != HttpStatusCode.NotFound || !string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var probe = await askOptions(ct);
            return probe.StatusCode == HttpStatusCode.NotFound;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>The error for a request the server has no route for, naming the feature and the server.</summary>
    public async Task<ServerFeatureMissingException> MissingAsync(HttpMethod method, string path, CancellationToken ct)
    {
        string endpoint = $"{method.Method} {Template(path)}";
        string? feature = FeatureFor(path);
        string what = feature is null ? $"{endpoint}, which this command needs" : $"{feature} (it has no {endpoint})";
        string server;
        try
        {
            server = Describe(await GetInfoAsync(ct)) + " ";
        }
        catch (HttpRequestException)
        {
            server = "";
        }
        return new ServerFeatureMissingException(
            feature ?? endpoint,
            $"This server doesn't support {what}. {server}Upgrade the server to use this command.");
    }

    /// <summary>One sentence on which server this is, for messages and <c>connapse version</c>.</summary>
    public static string Describe(ServerInfo? info) => $"It runs {VersionLabel(info)}.";

    /// <summary>The server's version as a phrase: "Connapse 0.4.0", or what's known of an older one.</summary>
    public static string VersionLabel(ServerInfo? info) => info is null
        ? "Connapse 0.3.x or earlier (it doesn't report its version)"
        : $"Connapse {ShortVersion(info.Version)}";

    /// <summary>The version without its build metadata ("0.4.0+abc123" is "0.4.0").</summary>
    public static string ShortVersion(string version)
    {
        int plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }

    /// <summary>The path with its query dropped and IDs shown as {id}, so the message names the route.</summary>
    public static string Template(string path)
    {
        int query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];
        return GuidSegment().Replace(path, "{id}");
    }

    // Longest prefix first. A path not listed here is named by its route instead.
    private static readonly (string Prefix, string Feature)[] Features =
    [
        ("/api/v1/sources", "sources"),
        ("/api/sources", "sources"),
        ("/api/v1/connections", "connections"),
        ("/api/v1/providers", "identity providers"),
        ("/api/v1/atlassian", "Atlassian"),
        ("/api/v1/audit", "the audit log"),
        ("/api/v1/invites", "invites"),
        ("/api/v1/oauth-clients", "OAuth clients"),
        ("/api/v1/auth/me", "signed-in user details"),
        ("/api/v1/agents", "agents"),
        ("/api/v1/settings", "settings by category"),
    ];

    /// <summary>The feature a path belongs to, or null when it isn't one the CLI names.</summary>
    public static string? FeatureFor(string path)
    {
        string template = Template(path);
        if (template.EndsWith("/summaries", StringComparison.OrdinalIgnoreCase) || template.Contains("/summaries/", StringComparison.OrdinalIgnoreCase))
            return "document summaries";
        if (template.EndsWith("/retry-failed", StringComparison.OrdinalIgnoreCase) || template.EndsWith("/retry", StringComparison.OrdinalIgnoreCase))
            return "retrying failed files";
        if (template.EndsWith("/files/bulk-delete", StringComparison.OrdinalIgnoreCase))
            return "deleting files in bulk";

        foreach (var (prefix, feature) in Features)
        {
            if (template.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || template.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return feature;
        }
        return null;
    }

    [GeneratedRegex("(?<=/)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?=/|$)")]
    private static partial Regex GuidSegment();
}

/// <summary>
/// Thrown when a command needs something the server doesn't have, because it is older than the CLI.
/// Exits with code 1 and prints the message, which names the feature and the server's version.
/// </summary>
public sealed class ServerFeatureMissingException(string feature, string message) : CliUserError(message)
{
    /// <summary>The feature's name, or the route when the CLI has no name for it.</summary>
    public string Feature { get; } = feature;
}
