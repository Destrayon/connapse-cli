using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

public static class VersionCommand
{
    public static Command Create(IServiceProvider services)
    {
        var cmd = new Command("version", "Print the CLI version.");

        cmd.SetAction(async (_, ct) =>
        {
            Console.WriteLine($"connapse v{UpdateChecker.GetCurrentVersion()}");

            // Best-effort: refresh the update-available notification cache.
            var updateChecker = services.GetRequiredService<UpdateChecker>();
            await updateChecker.CheckForUpdateNotificationAsync(ct);

            return CliExitCodes.Success;
        });

        return cmd;
    }
}
