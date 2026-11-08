using System.CommandLine;
using Connapse.Cli.Commands.Memory;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for fact memory under <c>connapse memory</c>: small facts (subject, attribute, value)
/// saved to a memory space and recalled later, as the server's memory tools do for agents.
/// </summary>
public static class MemoryCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var memory = new Command("memory", "Save, recall, list and forget remembered facts.");

        memory.Subcommands.Add(MemorySaveCommand.Create(services, orgOption));
        memory.Subcommands.Add(MemoryRecallCommand.Create(services, orgOption));
        memory.Subcommands.Add(MemoryListCommand.Create(services, orgOption));
        memory.Subcommands.Add(MemoryForgetCommand.Create(services, orgOption));

        memory.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse memory <save|recall|list|forget> [options]");
            return Task.FromResult(0);
        });

        return memory;
    }
}
