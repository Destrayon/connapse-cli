using Connapse.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Connapse.Cli.Services;

/// <summary>
/// The generated client's transport: every request it builds is sent through <see cref="ApiClient"/>,
/// so the token, the org header, the refresh on a 401 and the CLI's errors are the same for both.
/// </summary>
public sealed class ApiClientHandler(ApiClient api) : HttpMessageHandler
{
    /// <summary>
    /// The base URL the generated client builds its requests on. Only their path and query are sent,
    /// to the server the CLI is configured for, so this never leaves the process.
    /// </summary>
    public const string BaseUrl = "https://connapse.invalid";

    /// <summary>The generated client, sending through <paramref name="api"/>.</summary>
    public static ConnapseApiClient CreateClient(ApiClient api) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: CreateHttpClient(api))
        {
            BaseUrl = BaseUrl,
        });

    /// <summary>
    /// No timeout of its own: the server client <see cref="ApiClient"/> sends through has the configured
    /// one, and HttpClient's default 100 seconds would cut a long search or reindex short.
    /// </summary>
    public static HttpClient CreateHttpClient(ApiClient api) =>
        new(new ApiClientHandler(api)) { Timeout = Timeout.InfiniteTimeSpan };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        api.ForwardAsync(request, cancellationToken);
}
