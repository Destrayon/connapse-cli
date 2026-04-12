namespace Connapse.Cli.Services;

/// <summary>
/// Top-level exception → exit code mapping and console formatting.
/// Used by Program.Main to wrap the System.CommandLine invocation in a
/// single try/catch that produces consistent error output.
/// </summary>
public static class CliRuntime
{
    /// <summary>
    /// Maps a caught exception to an exit code and writes a formatted
    /// error line to stderr. When <paramref name="verbose"/> is true and
    /// the exception is not a <see cref="CliUserError"/>, the full stack
    /// trace is also printed.
    /// </summary>
    public static int HandleException(Exception ex, bool verbose)
    {
        switch (ex)
        {
            case CliAuthError auth:
                WriteError(auth.Message);
                WriteHint("Run 'connapse auth login' to authenticate.");
                return CliExitCodes.AuthError;

            case CliUserError user:
                WriteError(user.Message);
                return CliExitCodes.UserError;

            case OperationCanceledException:
                WriteError("Operation cancelled.");
                return CliExitCodes.UserError;

            default:
                WriteError($"Unexpected error: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(ex.ToString());
                }
                else
                {
                    WriteHint("Re-run with --verbose for the full stack trace.");
                }
                return CliExitCodes.Unexpected;
        }
    }

    private static void WriteError(string message)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.Write("Error: ");
        Console.ForegroundColor = original;
        Console.Error.WriteLine(message);
    }

    private static void WriteHint(string message)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Error.WriteLine($"  {message}");
        Console.ForegroundColor = original;
    }
}
