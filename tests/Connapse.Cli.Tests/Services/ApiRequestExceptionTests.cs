using Connapse.Cli.Services;

namespace Connapse.Cli.Tests.Services;

public sealed class ApiRequestExceptionTests
{
    [Fact]
    public void ParseBody_extracts_detail_from_rfc7807_problem()
    {
        var body = """
            {
              "type": "https://example.com/errors/not-found",
              "title": "Resource not found",
              "status": 404,
              "detail": "Container 'khastra' does not exist",
              "traceId": "abc123"
            }
            """;

        var (message, problem) = ApiRequestException.ParseBody(body);

        Assert.Equal("Container 'khastra' does not exist", message);
        Assert.NotNull(problem);
        Assert.Equal("abc123", problem!.TraceId);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public void ParseBody_falls_back_to_title_when_detail_missing()
    {
        var body = """{"title":"Bad Request","status":400}""";

        var (message, _) = ApiRequestException.ParseBody(body);

        Assert.Equal("Bad Request", message);
    }

    [Fact]
    public void ParseBody_extracts_error_field_from_legacy_shape()
    {
        var body = """{"error":"container name already exists"}""";

        var (message, problem) = ApiRequestException.ParseBody(body);

        Assert.Equal("container name already exists", message);
        Assert.Null(problem);
    }

    [Fact]
    public void ParseBody_returns_raw_body_when_not_json()
    {
        var body = "Internal server error — contact support";

        var (message, problem) = ApiRequestException.ParseBody(body);

        Assert.Equal(body, message);
        Assert.Null(problem);
    }

    [Fact]
    public void ParseBody_truncates_long_non_json_bodies()
    {
        var body = new string('x', 800);

        var (message, _) = ApiRequestException.ParseBody(body);

        Assert.EndsWith("...", message);
        Assert.True(message.Length <= 503);
    }

    [Fact]
    public void ParseBody_returns_explanatory_message_for_empty_body()
    {
        var (message, _) = ApiRequestException.ParseBody("");
        Assert.Equal("Server returned an error with an empty response body.", message);
    }

    [Fact]
    public void ApiRequestException_inherits_from_CliUserError()
    {
        var ex = new ApiRequestException(System.Net.HttpStatusCode.BadRequest, "oops");
        Assert.IsAssignableFrom<CliUserError>(ex);
    }

    [Fact]
    public void ApiUnauthorizedException_inherits_from_CliAuthError()
    {
        var ex = new ApiUnauthorizedException("no token");
        Assert.IsAssignableFrom<CliAuthError>(ex);
    }
}
