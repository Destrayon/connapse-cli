using System.Net;
using System.Net.Http.Headers;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Services;

public sealed class ApiClientTests : IDisposable
{
    private static readonly Uri BaseUri = new("https://server.example");

    private readonly string _tempDir;
    private readonly CredentialStore _credStore;
    private readonly ConfigStore _configStore;
    private readonly MockHttpMessageHandler _handler;
    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly OrgContextProvider _orgContext;
    private readonly FakeEditionProvider _editionProvider;

    public ApiClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid());
        _credStore = new CredentialStore(_tempDir);
        _configStore = new ConfigStore(_tempDir);
        _handler = new MockHttpMessageHandler();
        _httpClientFactory = new MockHttpClientFactory(_handler, BaseUri);
        _orgContext = new OrgContextProvider();
        _editionProvider = new FakeEditionProvider(ServerEdition.Cloud);

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

    private ApiClient CreateClient() => new(
        _httpClientFactory,
        _credStore,
        _orgContext,
        _editionProvider);

    [Fact]
    public async Task Injects_authorization_header()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        };

        await CreateClient().GetAsync<object>("/api/containers");

        var req = Assert.Single(_handler.Requests);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("access-abc", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Injects_org_header_when_cloud_and_org_set()
    {
        _orgContext.CurrentOrgSlug = "khastra";
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        };

        await CreateClient().GetAsync<object>("/api/containers");

        var req = Assert.Single(_handler.Requests);
        Assert.True(req.Headers.TryGetValues("X-Connapse-Org", out var values));
        Assert.Equal("khastra", values!.Single());
    }

    [Fact]
    public async Task Does_not_inject_org_header_when_slug_null()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        };

        await CreateClient().GetAsync<object>("/api/containers");

        var req = Assert.Single(_handler.Requests);
        Assert.False(req.Headers.Contains("X-Connapse-Org"));
    }

    [Fact]
    public async Task Does_not_inject_org_header_when_self_hosted()
    {
        _editionProvider.Edition = ServerEdition.SelfHosted;
        _orgContext.CurrentOrgSlug = "khastra"; // would be a user error; we strip

        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        };

        await CreateClient().GetAsync<object>("/api/containers");

        var req = Assert.Single(_handler.Requests);
        Assert.False(req.Headers.Contains("X-Connapse-Org"));
    }

    [Fact]
    public async Task Refreshes_token_on_401_and_retries()
    {
        var callCount = 0;
        _handler.Responder = req =>
        {
            callCount++;
            // 1st call: API returns 401
            if (callCount == 1 && req.RequestUri!.PathAndQuery == "/api/containers")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            // 2nd call: token refresh at /oauth/token
            if (req.RequestUri!.PathAndQuery == "/oauth/token")
                return MockHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"access_token":"access-new","token_type":"Bearer","expires_in":3600,"refresh_token":"refresh-new2"}""");

            // 3rd call: retry of /api/containers
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}"""),
            };
        };

        var result = await CreateClient().GetAsync<Dictionary<string, bool>>("/api/containers");

        Assert.NotNull(result);
        Assert.True(result!["ok"]);
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Equal("access-new", _credStore.Load()!.ApiKey);
    }

    [Fact]
    public async Task Second_401_after_refresh_throws()
    {
        _handler.Responder = req =>
        {
            if (req.RequestUri!.PathAndQuery == "/oauth/token")
                return MockHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"access_token":"access-new","token_type":"Bearer","expires_in":3600,"refresh_token":"refresh-new2"}""");

            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        };

        await Assert.ThrowsAsync<ApiUnauthorizedException>(
            () => CreateClient().GetAsync<object>("/api/containers"));
    }

    [Fact]
    public async Task Non_401_error_throws_ApiRequestException()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"container not found"}"""),
        };

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => CreateClient().GetAsync<object>("/api/containers"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("container not found", ex.Message);
    }

    private sealed class FakeEditionProvider(ServerEdition initial) : IEditionProvider
    {
        public ServerEdition Edition { get; set; } = initial;

        public Task<ServerEdition> GetEditionAsync(
            bool forceRefresh = false,
            CancellationToken ct = default) =>
            Task.FromResult(Edition);
    }
}
