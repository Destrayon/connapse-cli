using System.Text;
using System.Text.Json;
using System.Web;

namespace Connapse.Cli.Services;

/// <summary>What <c>connapse api</c> was asked to send.</summary>
public sealed record ApiPassthroughRequest(
    string Method,
    string Path,
    string? Data = null,
    string ContentType = "application/json",
    bool Paginate = false);

/// <summary>
/// <c>connapse api &lt;method&gt; &lt;path&gt;</c>: sends any request to the signed-in server, with the
/// same sign-in, token refresh and org header as every other command, so a route the CLI has no command
/// for yet is still reachable. The response body goes to stdout as the server sent it. A status other
/// than 2xx is also reported on stderr and exits 1.
/// </summary>
public sealed class ApiPassthrough(ApiClient api)
{
    /// <summary>Default page size for <c>--paginate</c> when the path names no <c>take</c>.</summary>
    public const int DefaultTake = 100;

    private const int MaxPages = 10_000;

    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS",
    };

    public async Task<int> RunAsync(ApiPassthroughRequest request, TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        // Without a saved sign-in the CLI would talk to its default server, which may be another one.
        if (!api.IsSignedIn)
            throw new CliAuthError("Not signed in, so there is no server to send this to.");
        if (!Methods.Contains(request.Method))
            throw new CliValidationError($"'{request.Method}' is not an HTTP method. Use GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS.");
        var method = new HttpMethod(request.Method.ToUpperInvariant());
        string path = CheckPath(request.Path);

        byte[]? body = request.Data is null ? null : await ReadBodyAsync(request.Data, stdin, ct);
        if (body is not null && (method == HttpMethod.Get || method == HttpMethod.Head))
            throw new CliValidationError($"A {method} request takes no body; drop --data.");

        if (request.Paginate)
        {
            if (method != HttpMethod.Get)
                throw new CliValidationError("--paginate follows pages of a GET request only.");
            return await PaginateAsync(path, stdout, stderr, ct);
        }

        using var response = await api.SendPassthroughAsync(method, path, () => Content(body, request.ContentType), ct);
        string text = await response.Content.ReadAsStringAsync(ct);
        Write(stdout, text);
        return Report(response, stderr);
    }

    /// <summary>
    /// The path must be on the signed-in server: a full URL, or a protocol-relative <c>//host</c>, would
    /// send the bearer token to whichever host it names.
    /// </summary>
    internal static string CheckPath(string path)
    {
        string p = path.Trim();
        if (p.Length == 0)
            throw new CliValidationError("Give a path, like /api/containers.");
        if (p.Contains('\\') || p.StartsWith("//", StringComparison.Ordinal) || (!p.StartsWith('/') && p.Contains("://", StringComparison.Ordinal)))
            throw new CliValidationError($"'{path}' isn't a path on the signed-in server. Give one like /api/containers, not a URL.");
        // Git Bash rewrites an argument like /api/containers into C:/Program Files/Git/api/containers.
        if (p.Length > 2 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '/')
            throw new CliValidationError($"'{path}' is a Windows path. Git Bash rewrites /api/... arguments; run with MSYS_NO_PATHCONV=1 set.");
        return p.StartsWith('/') ? p : "/" + p;
    }

    private static async Task<byte[]> ReadBodyAsync(string data, TextReader stdin, CancellationToken ct)
    {
        if (data == "-")
            return Encoding.UTF8.GetBytes(await stdin.ReadToEndAsync(ct));
        if (data.StartsWith('@'))
        {
            string file = data[1..];
            if (!File.Exists(file))
                throw new CliValidationError($"--data names a file that doesn't exist: {file}");
            return await File.ReadAllBytesAsync(file, ct);
        }
        return Encoding.UTF8.GetBytes(data);
    }

    private static HttpContent? Content(byte[]? body, string contentType)
    {
        if (body is null)
            return null;
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return content;
    }

    /// <summary>
    /// Follows Connapse's paging (<c>skip</c> and <c>take</c> in the query, <c>items</c> and
    /// <c>hasMore</c> in the response) until the last page, and prints every page's items as one array.
    /// A response that isn't a page is printed as it came, as without --paginate.
    /// </summary>
    private async Task<int> PaginateAsync(string path, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        int queryAt = path.IndexOf('?');
        string route = queryAt < 0 ? path : path[..queryAt];
        var query = HttpUtility.ParseQueryString(queryAt < 0 ? "" : path[(queryAt + 1)..]);
        int skip = int.TryParse(query["skip"], out int s) ? s : 0;
        int take = int.TryParse(query["take"], out int t) ? t : DefaultTake;
        query["take"] = take.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var items = new List<JsonElement>();
        for (int page = 0; page < MaxPages; page++)
        {
            query["skip"] = skip.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var response = await api.SendPassthroughAsync(HttpMethod.Get, $"{route}?{query}", () => null, ct);
            string text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                Write(stdout, text);
                return Report(response, stderr);
            }

            using var doc = Parse(text);
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("items", out var pageItems) || pageItems.ValueKind != JsonValueKind.Array)
            {
                if (page > 0)
                    throw new CliServerError($"Page {page + 1} of {route} isn't a page of items like the ones before it.");
                Write(stdout, text);
                return CliExitCodes.Success;
            }

            items.AddRange(pageItems.EnumerateArray().Select(e => e.Clone()));
            bool hasMore = doc.RootElement.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True;
            int count = pageItems.GetArrayLength();
            if (!hasMore || count == 0)
            {
                Write(stdout, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                return CliExitCodes.Success;
            }
            skip += count;
        }
        throw new CliServerError($"{route} still had more after {MaxPages} pages; stopped.");
    }

    private static JsonDocument? Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Write(TextWriter stdout, string text)
    {
        if (text.Length == 0)
            return;
        stdout.Write(text);
        if (!text.EndsWith('\n'))
            stdout.WriteLine();
    }

    private static int Report(HttpResponseMessage response, TextWriter stderr)
    {
        if (response.IsSuccessStatusCode)
            return CliExitCodes.Success;
        stderr.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return CliExitCodes.UserError;
        stderr.WriteLine("  Run 'connapse auth login' to authenticate.");
        return CliExitCodes.AuthError;
    }
}
