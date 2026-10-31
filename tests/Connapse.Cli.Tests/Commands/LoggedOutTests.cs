using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Tests.Commands;

/// <summary>
/// The CLI after <c>connapse auth logout</c>: no credentials.json, but config.json still names the
/// server. Uses the real server clients, with only the network handler replaced (CON-168).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class LoggedOutTests : IDisposable
{
    private const string ConfiguredServer = "https://connapse.example/";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MockHttpMessageHandler _network = new();
    private readonly CredentialStore _credStore;
    private readonly ConfigStore _configStore;
    private readonly ServiceProvider _services;

    public LoggedOutTests()
    {
        _credStore = new CredentialStore(_tempDir);
        _configStore = new ConfigStore(_tempDir);
        _configStore.Save(new CliConfig { ApiBaseUrl = ConfiguredServer, Edition = ServerEdition.SelfHosted });

        var collection = new ServiceCollection();
        collection.AddSingleton(_credStore);
        collection.AddSingleton(_configStore);
        collection.AddSingleton<IClock, SystemClock>();
        collection.AddSingleton<OrgResolver>();
        collection.AddSingleton<IOrgContextProvider, OrgContextProvider>();
        collection.AddSingleton<OrgContextProvider>(sp => (OrgContextProvider)sp.GetRequiredService<IOrgContextProvider>());
        collection.AddConnapseServerClients();
        foreach (var name in new[] { HttpClientNames.ConnapseServer, HttpClientNames.ConnapseServerNoRedirect })
            collection.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new PassThrough(_network));
        collection.AddSingleton<EditionDetector>();
        collection.AddSingleton<IEditionProvider>(sp => sp.GetRequiredService<EditionDetector>());
        collection.AddSingleton<ApiClient>();
        collection.AddSingleton(sp => ApiClientHandler.CreateClient(sp.GetRequiredService<ApiClient>()));
        collection.AddSingleton<IConsolePrompt, ConsolePrompt>();
        _services = collection.BuildServiceProvider();

        _network.Responder = _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[],"totalCount":0,"hasMore":false}""");
    }

    public void Dispose()
    {
        _services.Dispose();
        _network.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ContainerList_LoggedOut_SaysNotLoggedInAndExits2()
    {
        var result = await RunAsync("container", "list");

        Assert.Equal(CliExitCodes.AuthError, result.ExitCode);
        Assert.Contains("Not logged in", result.Error);
        Assert.Contains("connapse auth login", result.Error);
        Assert.Empty(_network.Requests);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ServerClient_LoggedOut_UsesTheAddressSavedInConfig()
    {
        var client = _services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.ConnapseServer);

        Assert.Equal(new Uri(ConfiguredServer), client.BaseAddress);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Address_NothingSaved_IsTheLocalDefault()
    {
        new ConfigStore(_tempDir).Save(new CliConfig());

        Assert.Equal(ServerHttpClients.DefaultAddress, ServerHttpClients.Address(_credStore, _configStore));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ContainerList_SignedIn_SendsToTheSignedInServer()
    {
        _credStore.Save(new CliCredentials(
            ApiKey: "access-abc",
            ApiBaseUrl: "https://signed-in.example/",
            UserEmail: "alice@example.com",
            RefreshToken: null,
            ExpiresAt: DateTime.UtcNow.AddHours(1)));

        var result = await RunAsync("container", "list");

        Assert.True(result.ExitCode == CliExitCodes.Success, result.Error);
        Assert.Equal("signed-in.example", Assert.Single(_network.Requests).RequestUri!.Host);
    }

    private async Task<CommandResult> RunAsync(params string[] args)
    {
        var root = RootCommandBuilder.Build(_services, new Option<string?>("--org") { Recursive = true }, new Option<bool>("--verbose") { Recursive = true });

        var output = new StringWriter();
        var error = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        int exit;
        try
        {
            exit = await root.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                Output = output,
                Error = error,
            });
        }
        catch (Exception ex)
        {
            exit = CliRuntime.HandleException(ex, verbose: false);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return new CommandResult(exit, output.ToString(), error.ToString());
    }

    /// <summary>Hands requests to the shared mock without letting each pooled client dispose it.</summary>
    private sealed class PassThrough(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override void Dispose(bool disposing) { }
    }
}
