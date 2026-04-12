using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

public static class UpdateCommand
{
    public static Command Create(IServiceProvider services)
    {
        var checkOption = new Option<bool>("--check")
        {
            Description = "Check for a newer version without downloading or installing.",
        };

        var includePreOption = new Option<bool>("--include-prerelease")
        {
            Description = "Include pre-release versions when checking GitHub.",
        };

        var cmd = new Command("update", "Check for and install CLI updates from GitHub.");
        cmd.Options.Add(checkOption);
        cmd.Options.Add(includePreOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var checkOnly = parseResult.GetValue(checkOption);
            var includePre = parseResult.GetValue(includePreOption);

            var updateChecker = services.GetRequiredService<UpdateChecker>();
            return await updateChecker.UpdateAsync(checkOnly, includePre, ct);
        });

        return cmd;
    }
}
