using System.Net;

namespace Connapse.Cli.Services;

/// <summary>
/// Thrown when the server returns a non-success status code that isn't 401.
/// The message contains a server-provided error string when available.
/// </summary>
public sealed class ApiRequestException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public ApiRequestException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Thrown when the user's credentials are invalid or expired and cannot be
/// refreshed. Command handlers catch this and direct the user to re-login.
/// </summary>
public sealed class ApiUnauthorizedException : Exception
{
    public ApiUnauthorizedException(string message) : base(message) { }
}
