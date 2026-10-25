using System.Net;
using System.Text.Json;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Services;

public sealed class ApiPassthroughTests : IDisposable
{
    private static readonly Uri BaseUri = new("https://server.example");

    private readonly string _tempDir;
    private readonly CredentialStore _credStore;
    private readonly MockHttpMessageHandler _handler = new();
    private readonly List<(string Method, string PathAndQuery, string? Body, string? ContentType)> _sent = new();
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public ApiPassthroughTests()
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

    private void Respond(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _handler.Responder = req =>
        {
            _sent.Add((req.Method.Method, req.RequestUri!.PathAndQuery,
                req.Content?.ReadAsStringAsync().GetAwaiter().GetResult(), req.Content?.Headers.ContentType?.ToString()));
            return responder(req);
        };

    private Task<int> Run(ApiPassthroughRequest request, string stdin = "")
    {
        var api = new ApiClient(new MockHttpClientFactory(_handler, BaseUri), _credStore, new OrgContextProvider(), new FakeEditionProvider(ServerEdition.SelfHosted));
        return new ApiPassthrough(api).RunAsync(request, new StringReader(stdin), _stdout, _stderr);
    }

    [Fact]
    public async Task Post_with_data_sends_the_body_as_json_and_prints_the_response()
    {
        Respond(_ => MockHttpMessageHandler.Json(HttpStatusCode.Created, """{"id":"c1"}"""));

        int exit = await Run(new ApiPassthroughRequest("post", "/api/containers", Data: """{"name":"docs"}"""));

        Assert.Equal(CliExitCodes.Success, exit);
        var sent = Assert.Single(_sent);
        Assert.Equal(("POST", "/api/containers", """{"name":"docs"}"""), (sent.Method, sent.PathAndQuery, sent.Body));
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal("""{"id":"c1"}""", _stdout.ToString().TrimEnd());
    }

    [Fact]
    public async Task Data_dash_reads_the_body_from_stdin()
    {
        Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Run(new ApiPassthroughRequest("PATCH", "/api/containers/1", Data: "-"), stdin: """{"description":"x"}""");

        Assert.Equal("""{"description":"x"}""", Assert.Single(_sent).Body);
    }

    [Fact]
    public async Task Data_at_file_reads_the_body_from_the_file()
    {
        Respond(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Directory.CreateDirectory(_tempDir);
        string file = Path.Combine(_tempDir, "body.json");
        await File.WriteAllTextAsync(file, """{"from":"file"}""");

        await Run(new ApiPassthroughRequest("PUT", "/api/settings/search", Data: "@" + file));

        Assert.Equal("""{"from":"file"}""", Assert.Single(_sent).Body);
    }

    [Theory]
    [InlineData("https://evil.example/api/containers")]
    [InlineData("//evil.example/api/containers")]
    [InlineData("\\\\evil.example\\api")]
    [InlineData("C:/Program Files/Git/api/containers")]
    public async Task A_url_or_another_host_is_refused_before_any_request(string path)
    {
        Respond(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<CliValidationError>(() => Run(new ApiPassthroughRequest("GET", path)));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Not_signed_in_is_refused_before_any_request()
    {
        _credStore.Delete();

        await Assert.ThrowsAsync<CliAuthError>(() => Run(new ApiPassthroughRequest("GET", "/api/containers")));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task A_path_without_a_leading_slash_is_on_the_server()
    {
        Respond(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Run(new ApiPassthroughRequest("GET", "api/containers"));

        Assert.Equal("/api/containers", Assert.Single(_sent).PathAndQuery);
    }

    [Fact]
    public async Task An_error_status_prints_the_servers_body_and_exits_1()
    {
        Respond(_ => MockHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"title":"Container not found"}"""));

        int exit = await Run(new ApiPassthroughRequest("GET", "/api/containers/nope"));

        Assert.Equal(CliExitCodes.UserError, exit);
        Assert.Contains("Container not found", _stdout.ToString());
        Assert.Contains("HTTP 404", _stderr.ToString());
    }

    [Fact]
    public async Task A_401_refreshes_the_token_and_resends_the_same_body()
    {
        int calls = 0;
        Respond(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/oauth/token")
                return MockHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"access_token":"access-new","token_type":"Bearer","expires_in":3600,"refresh_token":"refresh-new"}""");
            return ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : MockHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        });

        int exit = await Run(new ApiPassthroughRequest("POST", "/api/containers", Data: """{"name":"docs"}"""));

        Assert.Equal(CliExitCodes.Success, exit);
        var posts = _sent.Where(s => s.PathAndQuery == "/api/containers").ToList();
        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.Equal("""{"name":"docs"}""", p.Body));
        Assert.Equal("Bearer access-new", _handler.Requests.Last().Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Paginate_follows_every_page_and_prints_one_array()
    {
        Respond(req => req.RequestUri!.Query switch
        {
            "?take=2&skip=0" => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[{"n":1},{"n":2}],"totalCount":5,"hasMore":true}"""),
            "?take=2&skip=2" => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[{"n":3},{"n":4}],"totalCount":5,"hasMore":true}"""),
            "?take=2&skip=4" => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[{"n":5}],"totalCount":5,"hasMore":false}"""),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
        });

        int exit = await Run(new ApiPassthroughRequest("GET", "/api/containers?take=2", Paginate: true));

        Assert.Equal(CliExitCodes.Success, exit);
        Assert.Equal(3, _sent.Count);
        var items = JsonDocument.Parse(_stdout.ToString()).RootElement.EnumerateArray().Select(e => e.GetProperty("n").GetInt32());
        Assert.Equal([1, 2, 3, 4, 5], items);
    }

    [Fact]
    public async Task Paginate_without_take_asks_for_pages_of_the_default_size()
    {
        Respond(_ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[],"totalCount":0,"hasMore":false}"""));

        await Run(new ApiPassthroughRequest("GET", "/api/sources", Paginate: true));

        Assert.Equal($"/api/sources?take={ApiPassthrough.DefaultTake}&skip=0", Assert.Single(_sent).PathAndQuery);
        Assert.Equal("[]", _stdout.ToString().Trim());
    }

    [Fact]
    public async Task Paginate_prints_a_response_that_isnt_a_page_as_it_came()
    {
        Respond(_ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"version":"1.0"}"""));

        int exit = await Run(new ApiPassthroughRequest("GET", "/api/info", Paginate: true));

        Assert.Equal(CliExitCodes.Success, exit);
        Assert.Equal("""{"version":"1.0"}""", _stdout.ToString().TrimEnd());
    }

    [Fact]
    public async Task Paginate_stops_at_an_error_page_and_reports_it()
    {
        Respond(req => req.RequestUri!.Query.Contains("skip=0")
            ? MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"items":[{"n":1}],"hasMore":true}""")
            : MockHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{"title":"boom"}"""));

        int exit = await Run(new ApiPassthroughRequest("GET", "/api/containers?take=1", Paginate: true));

        Assert.Equal(CliExitCodes.UserError, exit);
        Assert.Contains("boom", _stdout.ToString());
    }

    [Theory]
    [InlineData("POST", null, true)]
    [InlineData("GET", "{}", false)]
    [InlineData("FETCH", null, false)]
    public async Task Bad_combinations_are_refused_before_any_request(string method, string? data, bool paginate)
    {
        await Assert.ThrowsAsync<CliValidationError>(() => Run(new ApiPassthroughRequest(method, "/api/containers", data, Paginate: paginate)));

        Assert.Empty(_handler.Requests);
    }

    private sealed class FakeEditionProvider(ServerEdition edition) : IEditionProvider
    {
        public Task<ServerEdition> GetEditionAsync(bool forceRefresh = false, CancellationToken ct = default) =>
            Task.FromResult(edition);
    }
}
