using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Services;

/// <summary>
/// Registers the named clients that talk to the Connapse server. The address comes from the saved
/// sign-in, else from config.json (which logout keeps), else the local default. A request sent while
/// signed out stops before it leaves the machine with "Not logged in" (exit 2), rather than going to
/// whatever server the address points at.
/// </summary>
public static class ServerHttpClients
{
    public const string DefaultAddress = "https://localhost:5001";

    public static IServiceCollection AddConnapseServerClients(this IServiceCollection services)
    {
        foreach (var (name, followRedirects) in new[] { (HttpClientNames.ConnapseServer, true), (HttpClientNames.ConnapseServerNoRedirect, false) })
        {
            services.AddHttpClient(name, (sp, client) =>
            {
                client.BaseAddress = new Uri(Address(sp));
                client.Timeout = TimeSpan.FromMinutes(10);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var serverUri = new Uri(Address(sp));
                var isLocalhost = serverUri.Host is "localhost" or "127.0.0.1" or "::1";
                return new HttpClientHandler
                {
                    AllowAutoRedirect = followRedirects,
                    ServerCertificateCustomValidationCallback = isLocalhost
                        ? (_, _, _, _) => true
                        : null,
                };
            })
            .AddHttpMessageHandler(sp => new RequireSignInHandler(sp.GetRequiredService<CredentialStore>()));
        }

        return services;
    }

    /// <summary>The server to talk to: the signed-in one, else the one config.json remembers, else the default.</summary>
    public static string Address(CredentialStore credStore, ConfigStore configStore) =>
        credStore.Load()?.ApiBaseUrl ?? configStore.Load().ApiBaseUrl ?? DefaultAddress;

    private static string Address(IServiceProvider sp) =>
        Address(sp.GetRequiredService<CredentialStore>(), sp.GetRequiredService<ConfigStore>());

    /// <summary>
    /// Refuses to send without a saved sign-in. Checked per request, not when the handler is built,
    /// because the factory pools handlers and login saves credentials partway through its run.
    /// </summary>
    private sealed class RequireSignInHandler(CredentialStore credStore) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (credStore.Load() is null)
                throw new CliAuthError("Not logged in.");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
