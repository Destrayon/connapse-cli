using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Code;

/// <summary><c>connapse code map</c>: GET /api/code/map (code.repoMap).</summary>
public static class CodeMapCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var containerOption = CodeHelpers.ContainerOption();
        var pathOption = new Option<string?>("--path") { Description = "Map this folder only." };
        var maxFilesOption = new Option<int?>("--max-files")
        {
            Description = "How many files to show (1-200). The server defaults to 30.",
        };
        var jsonOption = CodeHelpers.JsonOption();

        var cmd = new Command("map",
            "Map indexed code: the files the rest of the code refers to most, each with its main definitions.");
        cmd.Options.Add(containerOption);
        cmd.Options.Add(pathOption);
        cmd.Options.Add(maxFilesOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var maxFiles = CodeHelpers.InRange(parseResult.GetValue(maxFilesOption), "--max-files", 1, 200);

            var client = services.GetRequiredService<ConnapseApiClient>();
            var answer = await client.Api.Code.Map.GetAsync(r =>
            {
                r.QueryParameters.ContainerId = parseResult.GetRequiredValue(containerOption);
                r.QueryParameters.Path = parseResult.GetValue(pathOption);
                r.QueryParameters.MaxFiles = maxFiles;
            }, ct) ?? throw new CliServerError("The server returned no answer.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(answer));
                return CliExitCodes.Success;
            }

            CodeHelpers.PrintNotice(answer.Notice);
            var files = answer.Files ?? [];
            Console.WriteLine($"{files.Count} of {answer.TotalFiles ?? files.Count} indexed files, most referred to first.");
            foreach (var file in files)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(file.Path);
                Console.ResetColor();
                foreach (var symbol in file.Symbols ?? [])
                {
                    Console.Write($"  {symbol.Line,5}: {symbol.Kind} {symbol.Symbol}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  used by {symbol.ReferencedBy} file{(symbol.ReferencedBy == 1 ? "" : "s")}");
                    Console.ResetColor();
                }
            }
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
