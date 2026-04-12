using System.Net.Http;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        var services = BuildServiceProvider();

        // Temporary: prove the wiring works by resolving each service.
        // This entire Main method will be replaced in Phase 5 with the real
        // command tree.
        var credentialStore = services.GetRequiredService<CredentialStore>();
        var configStore = services.GetRequiredService<ConfigStore>();
        var orgResolver = services.GetRequiredService<OrgResolver>();
        var editionDetector = services.GetRequiredService<EditionDetector>();
        var apiClient = services.GetRequiredService<ApiClient>();

        Console.WriteLine("connapse CLI — Phase 3 services wired");
        Console.WriteLine($"  CredentialStore: {credentialStore.GetType().Name}");
        Console.WriteLine($"  ConfigStore:     {configStore.GetType().Name}");
        Console.WriteLine($"  OrgResolver:     {orgResolver.GetType().Name}");
        Console.WriteLine($"  EditionDetector: {editionDetector.GetType().Name}");
        Console.WriteLine($"  ApiClient:       {apiClient.GetType().Name}");
        return 0;
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var homeDirectory = HomeDirectoryResolver.GetHomeDirectory();

        services.AddSingleton(new CredentialStore(homeDirectory));
        services.AddSingleton(new ConfigStore(homeDirectory));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<OrgResolver>();
        services.AddSingleton<IOrgContextProvider, OrgContextProvider>();
        services.AddSingleton<OrgContextProvider>(sp =>
            (OrgContextProvider)sp.GetRequiredService<IOrgContextProvider>());

        services.AddSingleton(sp =>
        {
            var credStore = sp.GetRequiredService<CredentialStore>();
            var baseUrl = credStore.Load()?.ApiBaseUrl ?? "https://localhost:5001";
            var serverUri = new Uri(baseUrl);
            var isLocalhost = serverUri.Host is "localhost" or "127.0.0.1" or "::1";
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = isLocalhost
                    ? (_, _, _, _) => true
                    : null,
            };
            return new HttpClient(handler)
            {
                BaseAddress = serverUri,
                Timeout = TimeSpan.FromMinutes(10),
            };
        });

        services.AddSingleton<EditionDetector>();
        services.AddSingleton<IEditionProvider>(sp => sp.GetRequiredService<EditionDetector>());
        services.AddSingleton<ApiClient>();

        return services.BuildServiceProvider();
    }
}
