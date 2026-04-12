namespace Connapse.Cli.Services;

public sealed class ConsolePrompt : IConsolePrompt
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public int? PromptForChoice(string prompt, int count)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();
            if (input is null)
                return null; // EOF

            if (int.TryParse(input.Trim(), out var choice) && choice >= 1 && choice <= count)
                return choice;

            WriteColoredLine($"  Enter a number between 1 and {count}.", ConsoleColor.Yellow);
        }
    }

    public void WriteLine(string message) => Console.WriteLine(message);

    public void WriteColoredLine(string message, ConsoleColor color)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = original;
    }
}
