namespace Connapse.Cli.Tests.Testing;

/// <summary>
/// Test double for <see cref="IHttpClientFactory"/>. Matches the real
/// factory's semantics: each <see cref="CreateClient"/> call mints a fresh
/// <see cref="HttpClient"/> wrapping a shared, non-disposing handler.
/// Callers that <c>using var client = factory.CreateClient(...)</c> dispose
/// only the wrapper, not the handler, so subsequent calls still work.
/// </summary>
public sealed class MockHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    private readonly Uri? _baseAddress;

    public MockHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null)
    {
        _handler = handler;
        _baseAddress = baseAddress;
    }

    public HttpClient CreateClient(string name) =>
        new(_handler, disposeHandler: false) { BaseAddress = _baseAddress };
}
