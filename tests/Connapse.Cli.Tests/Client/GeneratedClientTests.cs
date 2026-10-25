using System.Net;
using Connapse.Cli.Tests.Testing;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Connapse.Cli.Tests.Client;

/// <summary>
/// The generated client against canned server responses: the routes it builds, the typed bodies it
/// reads, and the problem+json errors it raises. Response JSON is shaped as the server sends it.
/// </summary>
public class GeneratedClientTests
{
    private readonly MockHttpMessageHandler _handler = new();

    private ConnapseApiClient Client() =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: new HttpClient(_handler))
        {
            BaseUrl = "https://connapse.test",
        });

    [Fact]
    public async Task ListContainers_ReadsTheTypedPage()
    {
        _handler.Responder = _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """
            {"items":[{"id":"7b1c9e2a-0000-4000-8000-000000000001","name":"notes","description":null,
            "createdAt":"2026-10-01T00:00:00Z","updatedAt":"2026-10-02T00:00:00Z","documentCount":3}],
            "totalCount":1,"hasMore":false}
            """);

        var page = await Client().Api.Containers.GetAsync(r => { r.QueryParameters.Skip = 0; r.QueryParameters.Take = 10; });

        Assert.Equal("https://connapse.test/api/containers?skip=0&take=10", _handler.Requests.Single().RequestUri!.ToString());
        var container = Assert.Single(page!.Items!);
        Assert.Equal("notes", container.Name);
        Assert.Equal(3, container.DocumentCount);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Me_ReadsTheSignedInIdentity()
    {
        _handler.Responder = _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """
            {"id":"11111111-2222-3333-4444-555555555555","email":"alice@example.com","displayName":"Alice",
            "roles":["Owner","Admin"],"createdAt":"2026-01-01T00:00:00Z"}
            """);

        var me = await Client().Api.V1.Auth.Me.GetAsync();

        Assert.Equal("https://connapse.test/api/v1/auth/me", _handler.Requests.Single().RequestUri!.ToString());
        Assert.Equal("alice@example.com", me!.Email);
        Assert.Equal("Alice", me.DisplayName);
        Assert.Contains("Owner", me.Roles!);
    }

    [Fact]
    public async Task GetContainer_Missing_RaisesTheServersProblem()
    {
        _handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"type":"https://connapse.dev/problems/container-not-found","title":"Container not found","status":404,"detail":"Container 7b1c9e2a-0000-4000-8000-000000000002 not found"}""",
                System.Text.Encoding.UTF8, "application/problem+json"),
        };

        var problem = await Assert.ThrowsAsync<ProblemDetails>(() =>
            Client().Api.Containers[Guid.Parse("7b1c9e2a-0000-4000-8000-000000000002")].GetAsync());

        Assert.Equal(404, problem.Status);
        Assert.Equal("Container not found", problem.Title);
        Assert.EndsWith("problems/container-not-found", problem.Type);
    }

    [Fact]
    public async Task GetSettings_ReadsTheCategorysTypedShape()
    {
        _handler.Responder = _ => MockHttpMessageHandler.Json(HttpStatusCode.OK, """
            {"provider":"Ollama","model":"nomic-embed-text","dimensions":768,"baseUrl":"http://localhost:11434"}
            """);

        var settings = await Client().Api.Settings["embedding"].GetAsync();

        Assert.Equal("https://connapse.test/api/settings/embedding", _handler.Requests.Single().RequestUri!.ToString());
        Assert.Equal("nomic-embed-text", settings!.EmbeddingSettings!.Model);
        Assert.Equal("Ollama", settings.EmbeddingSettings.Provider);
    }

    /// <summary>
    /// The workaround the client's README gives until CON-77: the merged SourceScope's Type enum only
    /// knows "s3", so another provider's scope sends its type through AdditionalData.
    /// </summary>
    [Fact]
    public async Task CreateSource_AGitHubScope_SendsItsTypeThroughAdditionalData()
    {
        string body = "";
        _handler.Responder = request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return MockHttpMessageHandler.Json(HttpStatusCode.Created, "{}");
        };
        var scope = new SourceScope { Owner = "octo", Repo = "docs" };
        scope.AdditionalData["type"] = "github";

        await Client().Api.Sources.PostAsync(new CreateSourceApiRequest
        {
            Name = "docs",
            ConnectionId = Guid.Parse("7b1c9e2a-0000-4000-8000-000000000004"),
            Scope = scope,
        });

        Assert.Contains("\"type\":\"github\"", body);
        Assert.Contains("\"owner\":\"octo\"", body);
        Assert.Contains("\"repo\":\"docs\"", body);
    }

    [Fact]
    public async Task CreateContainer_SendsTheTypedBody()
    {
        // Read while it's sent: the client disposes the request's content once the response arrives.
        string body = "";
        _handler.Responder = request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return MockHttpMessageHandler.Json(HttpStatusCode.Created, """
                {"id":"7b1c9e2a-0000-4000-8000-000000000003","name":"research","description":"papers",
                "createdAt":"2026-10-01T00:00:00Z","updatedAt":"2026-10-01T00:00:00Z","documentCount":0}
                """);
        };

        var created = await Client().Api.Containers.PostAsync(new CreateContainerApiRequest { Name = "research", Description = "papers" });

        Assert.Equal(HttpMethod.Post, _handler.Requests.Single().Method);
        Assert.Contains("\"name\":\"research\"", body);
        Assert.Contains("\"description\":\"papers\"", body);
        Assert.Equal("research", created!.Name);
    }
}
