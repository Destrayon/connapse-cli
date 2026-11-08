using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Tests.Commands;

/// <summary>
/// Two logins kept at once — Connapse Cloud in the default profile, a home server in "home" — and each
/// command picking one, wired the way Program.Main wires it: a fresh process per command, the profile
/// chosen from --profile, CONNAPSE_PROFILE or <c>auth switch</c> once the command line is parsed.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ProfileCommandTests : IDisposable
{
    private const string CloudToken = "cloud-access-token";
    private const string HomeToken = "home-access-token";

    private readonly string _tempDir;
    private readonly ProfileStore _profiles;
    private readonly MockHttpMessageHandler _handler = new();

    public ProfileCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid().ToString("N"));
        _profiles = new ProfileStore(Path.Combine(_tempDir, ".connapse"));
        CredentialStore.ForProfile(_profiles.Get("default")).Save(
            new CliCredentials(CloudToken, "https://www.connapse.com", "alice@example.com", ExpiresAt: DateTime.UtcNow.AddHours(1)));
        CredentialStore.ForProfile(_profiles.Get("home")).Save(
            new CliCredentials(HomeToken, "https://connapse.home.lan", "alice@home.lan", ExpiresAt: DateTime.UtcNow.AddHours(1)));
        _handler.Responder = _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
    }

    public void Dispose()
    {
        _handler.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Without_a_profile_commands_use_the_default_login()
    {
        await RunAsync(null, "container", "list");

        Assert.Equal($"Bearer {CloudToken}", _handler.Requests.Single().Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Profile_option_picks_the_other_login_for_one_command()
    {
        await RunAsync(null, "container", "list", "--profile", "home");
        await RunAsync(null, "container", "list");

        Assert.Equal(
            new[] { $"Bearer {HomeToken}", $"Bearer {CloudToken}" },
            _handler.Requests.Select(r => r.Headers.Authorization?.ToString()));
    }

    [Fact]
    public async Task Environment_variable_picks_the_login_and_the_option_still_wins()
    {
        await RunAsync("home", "container", "list");
        await RunAsync("home", "container", "list", "--profile", "default");

        Assert.Equal(
            new[] { $"Bearer {HomeToken}", $"Bearer {CloudToken}" },
            _handler.Requests.Select(r => r.Headers.Authorization?.ToString()));
    }

    [Fact]
    public async Task Switch_changes_the_login_later_commands_use_and_list_marks_it()
    {
        var switched = await RunAsync(null, "auth", "switch", "home");
        await RunAsync(null, "container", "list");
        var list = await RunAsync(null, "auth", "list");

        Assert.Equal(0, switched.ExitCode);
        Assert.Contains("profile 'home' (https://connapse.home.lan)", switched.Output);
        Assert.Equal($"Bearer {HomeToken}", _handler.Requests.Single().Headers.Authorization?.ToString());
        Assert.Contains("  default          https://www.connapse.com  alice@example.com", list.Output);
        Assert.Contains("* home             https://connapse.home.lan  alice@home.lan", list.Output);
    }

    [Fact]
    public async Task Switch_refuses_a_profile_with_no_login()
    {
        var result = await RunAsync(null, "auth", "switch", "work");

        Assert.Equal(CliExitCodes.UserError, result.ExitCode);
        Assert.Contains("connapse auth login --profile work --server <url>", result.Error);
        Assert.Null(_profiles.LoadActive());
    }

    [Fact]
    public async Task Logging_out_of_one_profile_keeps_the_other()
    {
        var result = await RunAsync(null, "auth", "logout", "--profile", "home");

        Assert.Contains("Logged out of profile 'home'", result.Output);
        Assert.Null(CredentialStore.ForProfile(_profiles.Get("home")).Load());
        Assert.Equal(CloudToken, CredentialStore.ForProfile(_profiles.Get("default")).Load()!.ApiKey);
    }

    [Fact]
    public async Task Signed_out_profile_hint_logs_in_to_that_profile()
    {
        var result = await RunAsync(null, "container", "list", "--profile", "work");

        Assert.Equal(CliExitCodes.AuthError, result.ExitCode);
        Assert.Contains("connapse auth login --profile work", result.Error);
        Assert.Empty(_handler.Requests);
    }

    /// <summary>One CLI run, as Program.Main does it, with <paramref name="environment"/> as CONNAPSE_PROFILE.</summary>
    private async Task<CommandResult> RunAsync(string? environment, params string[] args)
    {
        var profiles = new ProfileContext(_profiles, () => environment);
        var collection = new ServiceCollection();
        collection.AddProfileStores(profiles);
        collection.AddSingleton<IClock, SystemClock>();
        collection.AddSingleton<OrgResolver>();
        collection.AddSingleton<IOrgContextProvider, OrgContextProvider>();
        collection.AddSingleton<OrgContextProvider>(sp => (OrgContextProvider)sp.GetRequiredService<IOrgContextProvider>());
        collection.AddSingleton<IHttpClientFactory>(new SignedInOnlyFactory(_handler, () => profiles.Current));
        collection.AddSingleton<IEditionProvider, SelfHostedEdition>();
        collection.AddSingleton<EditionDetector>();
        collection.AddSingleton<ApiClient>();
        collection.AddSingleton(sp => ApiClientHandler.CreateClient(sp.GetRequiredService<ApiClient>()));
        await using var services = collection.BuildServiceProvider();

        var profileOption = RootCommandBuilder.CreateProfileOption();
        var root = RootCommandBuilder.Build(
            services,
            new Option<string?>("--org") { Recursive = true },
            new Option<bool>("--verbose") { Recursive = true },
            profileOption);

        var output = new StringWriter();
        var error = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        int exit;
        try
        {
            var parseResult = root.Parse(args);
            profiles.Select(parseResult.GetValue(profileOption));
            exit = await parseResult.InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                Output = output,
                Error = error,
            });
        }
        catch (Exception ex)
        {
            exit = CliRuntime.HandleException(ex, verbose: false, profiles.NamedProfileOrNull);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return new CommandResult(exit, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Sends to the chosen profile's server, and like the real clients refuses to send while it is signed out.
    /// </summary>
    private sealed class SignedInOnlyFactory(HttpMessageHandler handler, Func<CliProfile> profile) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var creds = CredentialStore.ForProfile(profile()).Load() ?? throw new CliAuthError("Not logged in.");
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(creds.ApiBaseUrl) };
        }
    }

    private sealed class SelfHostedEdition : IEditionProvider
    {
        public Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(ServerEdition.SelfHosted);
    }
}
