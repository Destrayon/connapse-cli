using System.CommandLine;
using Connapse.Cli.Commands;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli;

internal static class Program
{
    // Global options — declared at module scope so command builders can
    // reference them when reading parsed values in their SetAction handlers.
    internal static readonly Option<string?> OrgOption = new("--org")
    {
        Description = "Organization slug to target for this command. " +
                      "Overrides CONNAPSE_ORG and the default org in config.json. " +
                      "Ignored when the server is self-hosted.",
        Recursive = true,
    };

    internal static readonly Option<bool> VerboseOption = new("--verbose")
    {
        Description = "Print full stack traces for unexpected errors.",
        Recursive = true,
    };

    public static async Task<int> Main(string[] args)
    {
        await using var services = BuildServiceProvider();

        var root = RootCommandBuilder.Build(services, OrgOption, VerboseOption);
        var parseResult = root.Parse(args);

        var config = new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
        };

        var verbose = parseResult.GetValue(VerboseOption);

        try
        {
            return await parseResult.InvokeAsync(config);
        }
        catch (Exception ex)
        {
            return CliRuntime.HandleException(ex, verbose);
        }
    }

    private static ServiceProvider BuildServiceProvider()
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

        services.AddConnapseServerClients();

        services.AddSingleton<EditionDetector>();
        services.AddSingleton<IEditionProvider>(sp => sp.GetRequiredService<EditionDetector>());
        services.AddSingleton<ServerFeatures>();
        services.AddSingleton<ApiClient>();
        // The generated client, sent through ApiClient so it shares its token, org header and errors.
        services.AddSingleton(sp => ApiClientHandler.CreateClient(sp.GetRequiredService<ApiClient>()));
        services.AddSingleton<OAuthBrowserFlow>();
        services.AddSingleton<IConsolePrompt, ConsolePrompt>();
        services.AddSingleton<PostLoginOrgSelector>();
        services.AddSingleton<UpdateChecker>();

        return services.BuildServiceProvider();
    }
}
