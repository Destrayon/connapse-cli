using System.Net;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

/// <summary>
/// <c>auth login --with-token</c> against a server that authenticates the way Connapse does:
/// a personal access token (<c>cnp_…</c>) only in <c>X-Api-Key</c>, a sign-in token only as
/// <c>Authorization: Bearer</c>. Anything else is a 401.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class AuthLoginCommandTests : IDisposable
{
    private const string Server = "https://server.example";
    private const string PersonalAccessToken = "cnp_0123456789abcdefghijklmnopqrstuvwxyzABCDE";
    private const string SignInToken = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJib2IifQ.c2lnbmF0dXJl";

    private readonly CommandHost _host = new();

    public AuthLoginCommandTests() => _host.Respond(Authenticate);

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task LoginWithToken_PersonalAccessToken_SignsIn()
    {
        var result = await _host.RunWithInputAsync(PersonalAccessToken + "\n", "auth", "login", "--server", Server, "--with-token");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Signed in as: alice@example.com", result.Output);
        Assert.DoesNotContain("could not fetch identity", result.Output);
        Assert.Equal(PersonalAccessToken, _host.StoredCredentials!.ApiKey);
    }

    [Fact]
    public async Task LoginWithToken_PersonalAccessToken_LaterCommandsUseIt()
    {
        await _host.RunWithInputAsync(PersonalAccessToken + "\n", "auth", "login", "--server", Server, "--with-token");

        var result = await _host.RunAsync("container", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Found 1 container(s)", result.Output);
        var request = _host.Handler.Requests.Last();
        Assert.Equal(PersonalAccessToken, request.Headers.GetValues("X-Api-Key").Single());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task LoginWithToken_SignInToken_SignsInAndLaterCommandsUseIt()
    {
        var login = await _host.RunWithInputAsync(SignInToken + "\n", "auth", "login", "--server", Server, "--with-token");
        var list = await _host.RunAsync("container", "list");

        Assert.Equal(0, login.ExitCode);
        Assert.Contains("Signed in as: alice@example.com", login.Output);
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("Found 1 container(s)", list.Output);
        var request = _host.Handler.Requests.Last();
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(SignInToken, request.Headers.Authorization.Parameter);
        Assert.False(request.Headers.Contains("X-Api-Key"));
    }

    private static HttpResponseMessage Authenticate(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v1/info")
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        if (!IsSignedIn(request))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        return path switch
        {
            "/api/v1/auth/me" => MockHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"id":"u1","email":"alice@example.com","displayName":"Alice","roles":["admin"]}"""),
            "/api/containers" => MockHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"items":[{"id":"7b1c9e2a-0000-4000-8000-000000000001","name":"docs","documentCount":0}],"totalCount":1,"hasMore":false}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    // Mirrors the server's scheme selector: an X-Api-Key header, even a wrong one, decides the scheme.
    private static bool IsSignedIn(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues("X-Api-Key", out var keys))
            return keys.Single() == PersonalAccessToken;

        return request.Headers.Authorization is { Scheme: "Bearer", Parameter: SignInToken };
    }
}
