using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// PKCE + loopback-redirect OAuth flow (RFC 8252 + RFC 7636).
///
/// Ported from the original single-file Connapse.CLI Program.cs
/// (AuthLoginBrowser, WaitForCallbackAsync, and PKCE helpers).
/// </summary>
public sealed class OAuthBrowserFlow
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs the browser OAuth flow against the given server URL. Returns
    /// the resulting tokens on success.
    /// </summary>
    /// <exception cref="OAuthFlowException">
    /// Thrown when the flow is denied, times out, or fails for any reason.
    /// </exception>
    public async Task<OAuthLoginResult> LoginAsync(string serverUrl, CancellationToken ct = default)
    {
        serverUrl = serverUrl.TrimEnd('/');

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeS256Challenge(codeVerifier);
        var state = GenerateState();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/callback";

        var clientId = $"{serverUrl}/oauth/clients/cli.json";
        var authUrl = $"{serverUrl}/oauth/authorize" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&scope={Uri.EscapeDataString("knowledge:read knowledge:write")}" +
            $"&resource={Uri.EscapeDataString(serverUrl)}";

        Console.WriteLine($"Logging in to {serverUrl}");
        Console.WriteLine("Opening browser to complete authentication...");
        try
        {
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch
        {
            // Ignore — user can open manually
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("If your browser did not open, visit:");
        Console.WriteLine($"  {authUrl}");
        Console.ResetColor();
        Console.WriteLine("Waiting for browser authentication (timeout: 2 minutes)...");

        string receivedCode, receivedState;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(CallbackTimeout);
            try
            {
                (receivedCode, receivedState) = await WaitForCallbackAsync(listener, cts.Token);
            }
            catch (OperationCanceledException)
            {
                listener.Stop();
                throw new OAuthFlowException("Authentication timed out. Run 'connapse auth login' to try again.");
            }
            finally
            {
                listener.Stop();
            }
        }

        if (receivedCode == "__denied__")
            throw new OAuthFlowException("Authorization was denied in the browser.");

        if (!string.Equals(receivedState, state, StringComparison.Ordinal))
            throw new OAuthFlowException("State parameter mismatch — possible CSRF. Run 'connapse auth login' to try again.");

        Console.Write("Exchanging authorization code... ");

        var loginHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var loginClient = new HttpClient(loginHandler)
        {
            BaseAddress = new Uri(serverUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = receivedCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        });

        HttpResponseMessage exchangeResponse;
        try
        {
            exchangeResponse = await loginClient.PostAsync("/oauth/token", formContent, ct);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed: {ex.Message}");
            Console.ResetColor();
            throw new OAuthFlowException($"Token exchange failed: {ex.Message}", ex);
        }

        if (!exchangeResponse.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed ({(int)exchangeResponse.StatusCode})");
            Console.ResetColor();
            throw new OAuthFlowException($"Token exchange returned HTTP {(int)exchangeResponse.StatusCode}");
        }

        var tokenResult = await exchangeResponse.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: ct);
        if (tokenResult is null)
            throw new OAuthFlowException("Unexpected response from token endpoint.");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Authenticated");
        Console.ResetColor();

        return new OAuthLoginResult(
            ServerUrl: serverUrl,
            AccessToken: tokenResult.AccessToken,
            RefreshToken: tokenResult.RefreshToken,
            ExpiresAt: DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn));
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string ComputeS256Challenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static async Task<(string Code, string State)> WaitForCallbackAsync(
        TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();

        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer, ct);
        var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        var firstLine = request.Split('\n')[0];
        var pathPart = firstLine.Split(' ').ElementAtOrDefault(1) ?? "";
        var qIdx = pathPart.IndexOf('?');
        var queryString = qIdx >= 0 ? pathPart[(qIdx + 1)..].Trim() : "";

        var queryParams = queryString
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(
                p => Uri.UnescapeDataString(p[0]),
                p => Uri.UnescapeDataString(p[1]));

        var code = queryParams.GetValueOrDefault("code", "");
        var state = queryParams.GetValueOrDefault("state", "");
        var error = queryParams.GetValueOrDefault("error", "");

        var body = string.IsNullOrEmpty(error) ? SuccessHtml : DeniedHtml;
        var response =
            $"HTTP/1.1 200 OK\r\n" +
            $"Content-Type: text/html\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            $"Connection: close\r\n\r\n{body}";
        var responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, ct);
        await stream.FlushAsync(ct);

        return string.IsNullOrEmpty(error) ? (code, state) : ("__denied__", state);
    }

    private const string SuccessHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width,initial-scale=1" />
          <title>Authorized - Connapse</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            body { background: #0c0c14; color: #e4e2ee; font-family: 'Segoe UI', system-ui, sans-serif; min-height: 100vh; display: flex; align-items: center; justify-content: center; }
            .card { background: #1a1a28; border: 1px solid #2a2a3e; border-radius: 0.75rem; padding: 2.5rem 2rem; max-width: 400px; width: 100%; text-align: center; }
            .icon { width: 52px; height: 52px; border-radius: 50%; background: rgba(139, 92, 246, 0.1); border: 1px solid rgba(139, 92, 246, 0.25); display: flex; align-items: center; justify-content: center; margin: 0 auto 1.25rem; color: #a78bfa; font-size: 1.4rem; }
            h1 { font-size: 1.1rem; font-weight: 600; margin-bottom: 0.5rem; }
            p { font-size: 0.875rem; color: #8b89a0; }
            .brand { font-size: 0.75rem; color: #8b89a0; margin-top: 1.5rem; }
            .brand strong { color: #8b5cf6; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">&#10003;</div>
            <h1>Authentication Successful</h1>
            <p>You may close this tab and return to the terminal.</p>
            <p class="brand">Powered by <strong>Connapse</strong></p>
          </div>
        </body>
        </html>
        """;

    private const string DeniedHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width,initial-scale=1" />
          <title>Authorization Denied - Connapse</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            body { background: #0c0c14; color: #e4e2ee; font-family: 'Segoe UI', system-ui, sans-serif; min-height: 100vh; display: flex; align-items: center; justify-content: center; }
            .card { background: #1a1a28; border: 1px solid #2a2a3e; border-radius: 0.75rem; padding: 2.5rem 2rem; max-width: 400px; width: 100%; text-align: center; }
            .icon { width: 52px; height: 52px; border-radius: 50%; background: rgba(239, 68, 68, 0.1); border: 1px solid rgba(239, 68, 68, 0.3); display: flex; align-items: center; justify-content: center; margin: 0 auto 1.25rem; color: #f87171; font-size: 1.4rem; }
            h1 { font-size: 1.1rem; font-weight: 600; margin-bottom: 0.5rem; }
            p { font-size: 0.875rem; color: #8b89a0; }
            .brand { font-size: 0.75rem; color: #8b89a0; margin-top: 1.5rem; }
            .brand strong { color: #8b5cf6; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">&#x2715;</div>
            <h1>Authorization Denied</h1>
            <p>You may close this tab and return to the terminal.</p>
            <p class="brand">Powered by <strong>Connapse</strong></p>
          </div>
        </body>
        </html>
        """;
}

/// <summary>
/// Raised when the OAuth browser flow cannot complete successfully.
/// </summary>
public sealed class OAuthFlowException : Exception
{
    public OAuthFlowException(string message) : base(message) { }
    public OAuthFlowException(string message, Exception inner) : base(message, inner) { }
}
