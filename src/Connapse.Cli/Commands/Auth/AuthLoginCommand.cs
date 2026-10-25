using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

public static class AuthLoginCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var serverOption = new Option<string>("--server")
        {
            Description = "URL of the Connapse server to log in to.",
            Required = true,
        };
        serverOption.Aliases.Add("-s");

        var withTokenOption = new Option<bool>("--with-token")
        {
            Description = "Read an API token from stdin instead of opening a browser. " +
                          "Useful for CI, SSH sessions, or restricted networks.",
        };

        var login = new Command("login", "Authenticate with a Connapse server.");
        login.Options.Add(serverOption);
        login.Options.Add(withTokenOption);

        login.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var serverUrl = parseResult.GetRequiredValue(serverOption).TrimEnd('/');
            var withToken = parseResult.GetValue(withTokenOption);

            return withToken
                ? await RunTokenPasteAsync(services, serverUrl, ct)
                : await RunBrowserFlowAsync(services, serverUrl, ct);
        }));

        return login;
    }

    private static async Task<int> RunBrowserFlowAsync(
        IServiceProvider services, string serverUrl, CancellationToken ct)
    {
        var oauthFlow = services.GetRequiredService<OAuthBrowserFlow>();
        var credStore = services.GetRequiredService<CredentialStore>();
        var configStore = services.GetRequiredService<ConfigStore>();

        configStore.Save(configStore.Load() with { ApiBaseUrl = serverUrl });

        var result = await oauthFlow.LoginAsync(serverUrl, ct);

        credStore.Save(new CliCredentials(
            ApiKey: result.AccessToken,
            ApiBaseUrl: result.ServerUrl,
            UserEmail: "",
            RefreshToken: result.RefreshToken,
            ExpiresAt: result.ExpiresAt));

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Credentials stored in {CredentialPath()}");
        Console.ResetColor();

        var editionDetector = services.GetRequiredService<EditionDetector>();
        var edition = await editionDetector.GetEditionAsync(forceRefresh: true, ct);

        Console.WriteLine($"  Edition: {FormatEdition(edition)}");

        await FetchAndStoreIdentityAsync(services, ct);

        if (edition == ServerEdition.Cloud)
            await SelectDefaultOrgAsync(services, ct);

        return CliExitCodes.Success;
    }

    private static async Task<int> RunTokenPasteAsync(
        IServiceProvider services, string serverUrl, CancellationToken ct)
    {
        Console.WriteLine($"Logging in to {serverUrl} with a pasted token.");
        Console.Write("Paste token and press Enter: ");

        var token = await Console.In.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
            throw new CliValidationError("No token provided.");

        var credStore = services.GetRequiredService<CredentialStore>();
        var configStore = services.GetRequiredService<ConfigStore>();

        configStore.Save(configStore.Load() with { ApiBaseUrl = serverUrl });

        credStore.Save(new CliCredentials(
            ApiKey: token.Trim(),
            ApiBaseUrl: serverUrl,
            UserEmail: "",
            RefreshToken: null,
            ExpiresAt: null));

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Credentials stored in {CredentialPath()}");
        Console.ResetColor();

        var editionDetector = services.GetRequiredService<EditionDetector>();
        var edition = await editionDetector.GetEditionAsync(forceRefresh: true, ct);
        Console.WriteLine($"  Edition: {FormatEdition(edition)}");

        await FetchAndStoreIdentityAsync(services, ct);

        if (edition == ServerEdition.Cloud)
            await SelectDefaultOrgAsync(services, ct);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Authenticated.");
        Console.ResetColor();

        return CliExitCodes.Success;
    }

    private static async Task FetchAndStoreIdentityAsync(
        IServiceProvider services, CancellationToken ct)
    {
        var client = services.GetRequiredService<ConnapseApiClient>();
        var credStore = services.GetRequiredService<CredentialStore>();

        MeResponse? me;
        try
        {
            me = await client.Api.V1.Auth.Me.GetAsync(cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning: could not fetch identity ({ex.Message}).");
            Console.ResetColor();
            return;
        }

        if (me?.Email is null) return;

        var current = credStore.Load();
        if (current is null) return;

        credStore.Save(current with { UserEmail = me.Email });

        Console.WriteLine($"  Signed in as: {me.Email}" +
            (string.IsNullOrWhiteSpace(me.DisplayName) ? "" : $" ({me.DisplayName})"));
    }

    private static async Task SelectDefaultOrgAsync(
        IServiceProvider services, CancellationToken ct)
    {
        var apiClient = services.GetRequiredService<ApiClient>();
        var configStore = services.GetRequiredService<ConfigStore>();
        var selector = services.GetRequiredService<PostLoginOrgSelector>();

        List<Organization>? orgs;
        try
        {
            orgs = await apiClient.GetAsync<List<Organization>>("/api/v1/organizations", ct);
        }
        catch (ApiRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning: could not list organizations ({ex.Message}).");
            Console.ResetColor();
            return;
        }

        var result = selector.Select(orgs ?? new List<Organization>());

        if (result.SelectedSlug is not null)
            configStore.SetDefaultOrg(result.SelectedSlug);
    }

    private static string FormatEdition(ServerEdition edition) => edition switch
    {
        ServerEdition.Cloud => "Cloud",
        ServerEdition.SelfHosted => "Self-hosted",
        _ => "Unknown",
    };

    private static string CredentialPath() =>
        Path.Combine(HomeDirectoryResolver.GetHomeDirectory(), ".connapse", "credentials.json");
}
