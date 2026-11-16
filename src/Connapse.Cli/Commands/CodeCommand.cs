using System.CommandLine;
using Connapse.Cli.Commands.Code;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for <c>connapse code</c>: questions about indexed code (C#, Python, TypeScript) answered from the
/// server's code index, as its code tools do for agents.
/// </summary>
public static class CodeCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var code = new Command("code", "Find symbols, their uses and what a change would affect in indexed code.");

        code.Subcommands.Add(CodeSymbolsCommand.Create(services, orgOption));
        code.Subcommands.Add(CodeReferencesCommand.Create(services, orgOption));
        code.Subcommands.Add(CodeImpactCommand.Create(services, orgOption));
        code.Subcommands.Add(CodeMapCommand.Create(services, orgOption));

        code.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse code <symbols|references|impact|map> --container <name-or-id> [options]");
            return Task.FromResult(0);
        });

        return code;
    }
}
