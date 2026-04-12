namespace Connapse.Cli.Services;

/// <summary>
/// Base class for errors that should be shown to the user as a friendly
/// message (no stack trace) with exit code 1. The top-level handler in
/// Program.Main catches this and formats <see cref="Exception.Message"/>.
/// </summary>
public abstract class CliUserError : Exception
{
    protected CliUserError(string message) : base(message) { }
    protected CliUserError(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A user-facing error specifically caused by missing or invalid
/// credentials. Exits with code 2 and appends the hint
/// "Run 'connapse auth login' to authenticate."
/// </summary>
public class CliAuthError : CliUserError
{
    public CliAuthError(string message) : base(message) { }
    public CliAuthError(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a user supplies invalid input at the command boundary
/// (empty required argument, bad flag value, unparseable date, etc.).
/// Exits with code 1 and prints just the message.
/// </summary>
public sealed class CliValidationError : CliUserError
{
    public CliValidationError(string message) : base(message) { }
}

/// <summary>
/// Thrown when the server returns an unexpected response shape — a null
/// body when a typed response was expected, missing required fields, or
/// anything that suggests the client/server contract is broken. Exits
/// with code 1 and prints the message; distinct from ApiRequestException,
/// which only fires on non-success status codes.
/// </summary>
public sealed class CliServerError : CliUserError
{
    public CliServerError(string message) : base(message) { }
}

/// <summary>
/// Exit codes produced by the top-level invocation handler.
/// </summary>
public static class CliExitCodes
{
    public const int Success = 0;
    public const int UserError = 1;
    public const int AuthError = 2;
    public const int Unexpected = 99;
}
