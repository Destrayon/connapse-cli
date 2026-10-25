using System.Net;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Connapse.Cli.Tests.Services;

/// <summary>
/// The generated client sent through ApiClient, as the commands use it: it reaches the configured server,
/// carries the token and org header, refreshes once on a 401, and fails with the CLI's own errors.
/// </summary>
public sealed class ApiClientHandlerTests : IDisposable
{
    private static readonly Uri BaseUri = new("https://server.example");

    private readonly string _tempDir;
    private readonly CredentialStore _credStore;
    private readonly MockHttpMessageHandler _handler = new();
    private readonly OrgContextProvider _orgContext = new();

    public ApiClientHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid());
        _credStore = new CredentialStore(_tempDir);
        _credStore.Save(new CliCredentials(
            ApiKey: "access-abc",
            ApiBaseUrl: "https://server.example",
            UserEmail: "alice@example.com",
            RefreshToken: "refresh-xyz",
            ExpiresAt: DateTime.UtcNow.AddHours(1)));
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ConnapseApiClient Client()
    {
        var api = new ApiClient(new MockHttpClientFactory(_handler, BaseUri), _credStore, _orgContext, new CloudEdition());
        return new ConnapseApiClient(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(),
            httpClient: new HttpClient(new ApiClientHandler(api)))
        {
            BaseUrl = ApiClientHandler.BaseUrl,
        });
    }

    [Fact]
    public async Task Request_GoesToTheConfiguredServerWithTheTokenOrgAndAccept()
    {
        _orgContext.CurrentOrgSlug = "khastra";
        _handler.Responder = _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[],"totalCount":0,"hasMore":false}""");

        await Client().Api.Containers.GetAsync(r => r.QueryParameters.Take = 5);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("https://server.example/api/containers?take=5", request.RequestUri!.ToString());
        Assert.Equal("access-abc", request.Headers.Authorization!.Parameter);
        Assert.Equal("khastra", request.Headers.GetValues("X-Connapse-Org").Single());
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnceAndResendsTheSameBody()
    {
        var bodies = new List<string>();
        _handler.Responder = request =>
        {
            if (request.RequestUri!.PathAndQuery == "/oauth/token")
                return MockHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"access_token":"access-new","token_type":"Bearer","expires_in":3600,"refresh_token":"refresh-new"}""");

            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return bodies.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : MockHttpMessageHandler.Json(HttpStatusCode.Created, """{"id":"7b1c9e2a-0000-4000-8000-000000000001","name":"notes"}""");
        };

        var created = await Client().Api.Containers.PostAsync(new CreateContainerApiRequest { Name = "notes" });

        Assert.Equal("notes", created!.Name);
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains("\"name\":\"notes\"", bodies[1]);
        Assert.Equal("access-new", _credStore.Load()!.ApiKey);
        Assert.Equal("access-new", _handler.Requests.Last().Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ServerError_IsTheClisApiRequestException()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"type":"https://connapse.dev/problems/container-not-found","title":"Container not found","status":404,"detail":"Container x not found"}""",
                System.Text.Encoding.UTF8, "application/problem+json"),
        };

        var error = await Assert.ThrowsAsync<ApiRequestException>(() => Client().Api.Containers.ByName["missing"].GetAsync());

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Contains("Container x not found", error.Message);
    }

    private sealed class CloudEdition : IEditionProvider
    {
        public Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(ServerEdition.Cloud);
    }
}
