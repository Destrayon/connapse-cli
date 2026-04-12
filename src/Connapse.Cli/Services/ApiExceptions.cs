using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Connapse.Cli.Services;

/// <summary>
/// Thrown when the server returns a non-success status code that isn't 401.
/// Parses RFC 7807 ProblemDetails from the response body when available so
/// the top-level handler can show a concise "{title}: {detail}" message.
/// </summary>
public sealed class ApiRequestException : CliUserError
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
    public ProblemDetails? Problem { get; }

    public ApiRequestException(HttpStatusCode statusCode, string message, string? responseBody = null, ProblemDetails? problem = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Problem = problem;
    }

    /// <summary>
    /// Parses a response body into a user-facing message. Prefers
    /// ProblemDetails.Detail, falls back to ProblemDetails.Title, then to
    /// a plain "error" field, then to the raw body.
    /// </summary>
    public static (string Message, ProblemDetails? Problem) ParseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ("Server returned an error with an empty response body.", null);

        try
        {
            var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonOptions);
            if (problem is not null && (!string.IsNullOrWhiteSpace(problem.Detail) || !string.IsNullOrWhiteSpace(problem.Title)))
            {
                var message = !string.IsNullOrWhiteSpace(problem.Detail) ? problem.Detail! : problem.Title!;
                return (message, problem);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var errorProp) &&
                errorProp.ValueKind == JsonValueKind.String)
            {
                return (errorProp.GetString() ?? body, null);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return (body.Length > 500 ? body[..500] + "..." : body, null);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>
/// RFC 7807 ProblemDetails DTO. Only the fields ApiClient actually reads.
/// </summary>
public sealed record ProblemDetails
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("status")] public int? Status { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("instance")] public string? Instance { get; init; }
    [JsonPropertyName("traceId")] public string? TraceId { get; init; }
}

/// <summary>
/// Thrown when the user's credentials are invalid, missing, or expired and
/// cannot be refreshed. Command handlers let this bubble; the top-level
/// handler maps it to exit code 2 and prints a re-login hint.
/// </summary>
public sealed class ApiUnauthorizedException : CliAuthError
{
    public ApiUnauthorizedException(string message) : base(message) { }
}
