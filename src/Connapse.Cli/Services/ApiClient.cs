using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Connapse.Cli.Models;

namespace Connapse.Cli.Services;

/// <summary>
/// The HTTP facade used by every command. Handles:
///   1. Authorization header injection from CredentialStore
///   2. X-Connapse-Org header injection (Cloud only, when slug resolved)
///   3. 401 → refresh token → retry once
///   4. Typed helpers (GetAsync, PostAsync, DeleteAsync, SendAsync)
///
/// ApiClient does NOT construct its own HttpClient. The HttpClient is
/// injected with the correct BaseAddress so test and runtime paths can
/// differ.
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly CredentialStore _credStore;
    private readonly IOrgContextProvider _orgContext;
    private readonly IEditionProvider _editionProvider;

    public ApiClient(
        HttpClient httpClient,
        CredentialStore credStore,
        IOrgContextProvider orgContext,
        IEditionProvider editionProvider)
    {
        _httpClient = httpClient;
        _credStore = credStore;
        _orgContext = orgContext;
        _editionProvider = editionProvider;
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await SendWithAuthAsync(HttpMethod.Get, path, content: null, ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
    {
        var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await SendWithAuthAsync(HttpMethod.Post, path, content, ct);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var response = await SendWithAuthAsync(HttpMethod.Delete, path, content: null, ct);
    }

    public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct = default)
    {
        return await SendWithAuthAsync(method, path, content, ct);
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken ct)
    {
        var response = await SendOnceAsync(method, path, content, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            var refreshed = await TryRefreshAsync(ct);
            if (!refreshed)
                throw new ApiUnauthorizedException("Token expired or invalid. Run 'connapse auth login' to re-authenticate.");

            response = await SendOnceAsync(method, path, content, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new ApiUnauthorizedException("Authentication failed after token refresh. Run 'connapse auth login' to re-authenticate.");
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new ApiRequestException(response.StatusCode, ParseError(body), body);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (content is not null)
            request.Content = content;

        var creds = _credStore.Load();
        if (creds is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);

        var orgSlug = _orgContext.CurrentOrgSlug;
        if (!string.IsNullOrWhiteSpace(orgSlug))
        {
            var edition = await _editionProvider.GetEditionAsync(ct: ct);
            if (edition == ServerEdition.Cloud || edition == ServerEdition.Unknown)
                request.Headers.Add("X-Connapse-Org", orgSlug);
        }

        return await _httpClient.SendAsync(request, ct);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var creds = _credStore.Load();
        if (creds?.RefreshToken is null)
            return false;

        var clientId = $"{creds.ApiBaseUrl}/oauth/clients/cli.json";
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = creds.RefreshToken,
            ["client_id"] = clientId,
        });

        try
        {
            using var response = await _httpClient.PostAsync("/oauth/token", form, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOptions, ct);
            if (token is null)
                return false;

            _credStore.Save(new CliCredentials(
                ApiKey: token.AccessToken,
                ApiBaseUrl: creds.ApiBaseUrl,
                UserEmail: creds.UserEmail,
                RefreshToken: token.RefreshToken,
                ExpiresAt: DateTime.UtcNow.AddSeconds(token.ExpiresIn)));

            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static string ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorProp) &&
                errorProp.ValueKind == JsonValueKind.String)
            {
                return errorProp.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return string.IsNullOrWhiteSpace(body) ? "Unknown error" : body;
    }
}
