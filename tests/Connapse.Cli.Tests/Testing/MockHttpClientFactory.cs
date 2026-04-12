namespace Connapse.Cli.Tests.Testing;

/// <summary>
/// Test double for <see cref="IHttpClientFactory"/>. Matches the real
/// factory's semantics: each <see cref="CreateClient"/> call returns a
/// fresh <see cref="HttpClient"/> wrapping the shared handler, so callers
/// that <c>using var client = factory.CreateClient(...)</c> don't dispose
/// the handler and break subsequent calls.
/// </summary>
public sealed class MockHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    private readonly Uri? _baseAddress;

    public MockHttpClientFactory(HttpClient prototype)
    {
        // Reach the handler by cloning semantics — tests pass us a client
        // built over MockHttpMessageHandler, so we store the handler and
        // mint non-owning wrappers on each CreateClient call.
        _handler = ExtractHandler(prototype);
        _baseAddress = prototype.BaseAddress;
    }

    public HttpClient CreateClient(string name) =>
        new(_handler, disposeHandler: false) { BaseAddress = _baseAddress };

    private static HttpMessageHandler ExtractHandler(HttpClient client)
    {
        // HttpClient doesn't expose its handler publicly. Use reflection on
        // the internal _handler field so tests can pass a pre-built client.
        var field = typeof(HttpMessageInvoker).GetField(
            "_handler",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("HttpMessageInvoker._handler field not found");
        return (HttpMessageHandler)field.GetValue(client)!;
    }
}
