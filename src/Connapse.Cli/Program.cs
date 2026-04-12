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

        Console.WriteLine("connapse CLI — foundation services wired");
        Console.WriteLine($"  CredentialStore: {credentialStore.GetType().Name}");
        Console.WriteLine($"  ConfigStore:     {configStore.GetType().Name}");
        Console.WriteLine($"  OrgResolver:     {orgResolver.GetType().Name}");
        Console.WriteLine($"  Config loaded:   {configStore.Load() is not null}");
        return 0;
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var homeDirectory = HomeDirectoryResolver.GetHomeDirectory();

        services.AddSingleton(new CredentialStore(homeDirectory));
        services.AddSingleton(new ConfigStore(homeDirectory));
        services.AddSingleton<OrgResolver>();

        return services.BuildServiceProvider();
    }
}
