using System.CommandLine;
using System.Net;
using System.Text;
using Connapse.Cli.Commands;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Tests.Testing;

/// <summary>
/// Runs the CLI's real command tree against a canned server, signed in, and captures what it prints.
/// Commands write to <see cref="Console"/>, so tests using this belong to the <see cref="ConsoleCollection"/>.
/// </summary>
public sealed class CommandHost : IDisposable
{
    private static readonly Uri BaseUri = new("https://server.example");

    private readonly string _tempDir;
    private readonly ServiceProvider _services;

    public MockHttpMessageHandler Handler { get; } = new();

    /// <summary>Request bodies, read as they're sent; the client disposes them afterwards.</summary>
    public List<string> Bodies { get; } = new();

    public CommandHost()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid().ToString("N"));
        var credStore = new CredentialStore(_tempDir);
        credStore.Save(new CliCredentials(
            ApiKey: "access-abc",
            ApiBaseUrl: BaseUri.ToString(),
            UserEmail: "alice@example.com",
            RefreshToken: null,
            ExpiresAt: DateTime.UtcNow.AddHours(1)));

        var collection = new ServiceCollection();
        collection.AddSingleton(credStore);
        collection.AddSingleton(new ConfigStore(_tempDir));
        collection.AddSingleton<IClock, SystemClock>();
        collection.AddSingleton<OrgResolver>();
        collection.AddSingleton<IOrgContextProvider, OrgContextProvider>();
        collection.AddSingleton<OrgContextProvider>(sp => (OrgContextProvider)sp.GetRequiredService<IOrgContextProvider>());
        collection.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(Handler, BaseUri));
        collection.AddSingleton<IEditionProvider, SelfHostedEdition>();
        collection.AddSingleton<ApiClient>();
        collection.AddSingleton(sp => ApiClientHandler.CreateClient(sp.GetRequiredService<ApiClient>()));
        collection.AddSingleton<IConsolePrompt, ConsolePrompt>();
        _services = collection.BuildServiceProvider();
    }

    /// <summary>Answers every request with <paramref name="responder"/>, keeping each request body.</summary>
    public void Respond(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        Handler.Responder = request =>
        {
            if (request.Content is not null)
                Bodies.Add(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return responder(request);
        };
    }

    /// <summary>A route table: "GET /api/sources" → response. An unlisted route answers 404.</summary>
    public void Routes(params (string Route, Func<HttpResponseMessage> Response)[] routes)
    {
        Respond(request =>
        {
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            foreach (var (route, response) in routes)
            {
                if (route == key)
                    return response();
            }
            return MockHttpMessageHandler.Json(HttpStatusCode.NotFound, $$"""{"title":"Not found","status":404,"detail":"No route {{key}}"}""");
        });
    }

    public static Func<HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        () => MockHttpMessageHandler.Json(status, json);

    public static Func<HttpResponseMessage> Status(HttpStatusCode status) =>
        () => new HttpResponseMessage(status);

    /// <summary>Parses and runs a command line, mapping errors the way Program.Main does.</summary>
    public async Task<CommandResult> RunAsync(params string[] args)
    {
        var orgOption = new Option<string?>("--org") { Recursive = true };
        var verboseOption = new Option<bool>("--verbose") { Recursive = true };
        var root = RootCommandBuilder.Build(_services, orgOption, verboseOption);

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

    /// <summary>The requests sent, as "METHOD /path?query".</summary>
    public IReadOnlyList<string> Sent =>
        Handler.Requests.Select(r => $"{r.Method} {r.RequestUri!.PathAndQuery}").ToList();

    public void Dispose()
    {
        _services.Dispose();
        Handler.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class SelfHostedEdition : IEditionProvider
    {
        public Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(ServerEdition.SelfHosted);
    }
}

public sealed record CommandResult(int ExitCode, string Output, string Error);

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Console";
}
