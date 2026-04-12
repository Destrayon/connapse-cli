namespace Connapse.Cli.Services;

/// <summary>
/// Abstraction over interactive console prompts so PostLoginOrgSelector and
/// other interactive services can be unit-tested without real stdin.
/// </summary>
public interface IConsolePrompt
{
    /// <summary>True when stdin is connected to a terminal (not piped).</summary>
    bool IsInteractive { get; }

    /// <summary>Prompts the user to pick an index in [1, count]. Returns null on EOF/cancel.</summary>
    int? PromptForChoice(string prompt, int count);

    void WriteLine(string message);
    void WriteColoredLine(string message, ConsoleColor color);
}
