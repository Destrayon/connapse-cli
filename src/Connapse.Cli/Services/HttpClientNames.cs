namespace Connapse.Cli.Services;

/// <summary>
/// Constants for named <see cref="IHttpClientFactory"/> clients.
/// Using named clients instead of typed clients lets ApiClient and
/// EditionDetector share a single pre-configured client without coupling
/// their types to each other.
/// </summary>
public static class HttpClientNames
{
    /// <summary>
    /// The default Connapse server HTTP client. BaseAddress is recomputed
    /// on every CreateClient() call from the current CredentialStore state.
    /// </summary>
    public const string ConnapseServer = "connapse-server";
}
