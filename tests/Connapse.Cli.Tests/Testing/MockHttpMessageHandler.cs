using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Connapse.Cli.Tests.Testing;

/// <summary>
/// A reusable HttpMessageHandler that captures outbound requests and returns
/// pre-configured responses. Lets us assert headers, URLs, and request bodies
/// from service tests without needing a real HTTP server.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Responder(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return response;
    }
}
