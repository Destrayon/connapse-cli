using System.Net;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Services;

/// <summary>
/// CON-129: a request the server has no route for, because it's older than the CLI, reports the
/// feature and the server's version instead of a raw 404, and nothing else changes.
/// </summary>
public sealed class ServerFeaturesTests : IDisposable
{
    private const string SourceId = "0f705e25-7c64-481e-9d84-f831e8f06f53";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "connapse-cli-tests-" + Guid.NewGuid());
    private readonly MockHttpMessageHandler _handler = new();

    public ServerFeaturesTests()
    {
        new CredentialStore(_tempDir).Save(new CliCredentials(
            ApiKey: "access-abc",
            ApiBaseUrl: "https://server.example",
            UserEmail: "alice@example.com"));
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ApiClient CreateClient()
    {
        var factory = new MockHttpClientFactory(_handler, new Uri("https://server.example"));
        return new ApiClient(factory, new CredentialStore(_tempDir), new OrgContextProvider(), new SelfHosted(), new ServerFeatures(factory));
    }

    /// <summary>Answers as a server would: <paramref name="info"/> null means it has no /api/v1/info.</summary>
    private void Server(string? info, Func<HttpRequestMessage, HttpResponseMessage> otherwise)
    {
        _handler.Responder = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/info")
                return info is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : MockHttpMessageHandler.Json(HttpStatusCode.OK, info);
            return otherwise(request);
        };
    }

    private static HttpResponseMessage NoRoute(HttpRequestMessage _) => new(HttpStatusCode.NotFound);

    [Fact]
    public async Task Missing_route_on_an_old_server_names_the_feature_and_the_server()
    {
        Server(info: null, NoRoute);

        var ex = await Assert.ThrowsAsync<ServerFeatureMissingException>(() => CreateClient().GetAsync<object>("/api/v1/sources?skip=0"));

        Assert.Equal("sources", ex.Feature);
        Assert.Equal(
            "This server doesn't support sources (it has no GET /api/v1/sources). It runs Connapse 0.3.x or earlier "
            + "(it doesn't report its version). Upgrade the server to use this command.",
            ex.Message);
    }

    [Fact]
    public async Task Missing_route_on_a_server_that_reports_its_version_names_that_version()
    {
        Server("""{"edition":"self-hosted","version":"0.4.0+abc123","features":["mcp"]}""", NoRoute);

        var ex = await Assert.ThrowsAsync<ServerFeatureMissingException>(
            () => CreateClient().GetAsync<object>($"/api/v1/connections/{SourceId}"));

        Assert.Contains("doesn't support connections (it has no GET /api/v1/connections/{id})", ex.Message);
        Assert.Contains("It runs Connapse 0.4.0.", ex.Message);
    }

    [Fact]
    public async Task Method_not_allowed_is_a_missing_feature_without_a_probe()
    {
        Server(info: null, _ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

        var ex = await Assert.ThrowsAsync<ServerFeatureMissingException>(
            () => CreateClient().SendRawAsync(HttpMethod.Patch, $"/api/containers/{SourceId}", content: null));

        Assert.Equal("PATCH /api/containers/{id}", ex.Feature);
        Assert.StartsWith("This server doesn't support PATCH /api/containers/{id}, which this command needs.", ex.Message);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == HttpMethod.Options);
    }

    [Fact]
    public async Task Bare_404_from_a_route_that_exists_stays_a_404()
    {
        // Routing answers OPTIONS on a path it knows with 405, so this 404 is the endpoint's own.
        Server(info: null, r => new HttpResponseMessage(r.Method == HttpMethod.Options ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(() => CreateClient().GetAsync<object>($"/api/sources/{SourceId}"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Not_found_with_a_body_stays_a_404_without_a_probe()
    {
        Server(info: null, _ => MockHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"error":"Container 'x' not found"}"""));

        var ex = await Assert.ThrowsAsync<ApiRequestException>(() => CreateClient().GetAsync<object>("/api/containers/by-name/x"));

        Assert.Contains("Container 'x' not found", ex.Message);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == HttpMethod.Options);
    }

    [Fact]
    public async Task Missing_route_when_info_fails_leaves_the_version_out()
    {
        Server(info: null, NoRoute);
        _handler.Responder = request => request.RequestUri!.AbsolutePath == "/api/v1/info"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : NoRoute(request);

        var ex = await Assert.ThrowsAsync<ServerFeatureMissingException>(() => CreateClient().GetAsync<object>("/api/v1/agents"));

        Assert.Equal("This server doesn't support agents (it has no GET /api/v1/agents). Upgrade the server to use this command.", ex.Message);
    }

    [Fact]
    public async Task Server_info_is_read_once_per_run()
    {
        Server("""{"edition":"self-hosted","version":"0.4.0"}""", NoRoute);
        var features = new ServerFeatures(new MockHttpClientFactory(_handler, new Uri("https://server.example")));

        await features.GetInfoAsync();
        var info = await features.GetInfoAsync();

        Assert.Equal("0.4.0", info!.Version);
        Assert.Single(_handler.Requests, r => r.RequestUri!.AbsolutePath == "/api/v1/info");
    }

    [Theory]
    [InlineData("/api/sources", "sources")]
    [InlineData("/api/v1/sources/scope-schema/github", "sources")]
    [InlineData("/api/v1/connections/" + SourceId + "/secret", "connections")]
    [InlineData("/api/v1/auth/me", "signed-in user details")]
    [InlineData("/api/containers/" + SourceId + "/files/abc/retry", "retrying failed files")]
    [InlineData("/api/containers/" + SourceId + "/retry-failed", "retrying failed files")]
    [InlineData("/api/containers/" + SourceId + "/files/bulk-delete", "deleting files in bulk")]
    [InlineData("/api/v1/settings/summaries/clear", "document summaries")]
    [InlineData("/api/v1/settings/chunking", "settings by category")]
    [InlineData("/api/sourcesx", null)]
    [InlineData("/api/containers", null)]
    public void FeatureFor_names_the_feature_a_path_belongs_to(string path, string? feature)
    {
        Assert.Equal(feature, ServerFeatures.FeatureFor(path));
    }

    [Fact]
    public void Template_drops_the_query_and_shows_ids_as_placeholders()
    {
        Assert.Equal("/api/containers/{id}/files/{id}", ServerFeatures.Template($"/api/containers/{SourceId}/files/{SourceId}?x=1"));
    }

    private sealed class SelfHosted : IEditionProvider
    {
        public Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(ServerEdition.SelfHosted);
    }
}
