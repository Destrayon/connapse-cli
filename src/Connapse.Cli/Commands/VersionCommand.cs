using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands;

public static class VersionCommand
{
    public static Command Create(IServiceProvider services)
    {
        var cmd = new Command("version", "Print the CLI version, and the server's when signed in.");

        cmd.SetAction(async (_, ct) =>
        {
            Console.WriteLine($"connapse v{UpdateChecker.GetCurrentVersion()}");

            // The server's version and features, so a command it lacks isn't a surprise (CON-129).
            var creds = services.GetRequiredService<CredentialStore>().Load();
            if (creds is not null)
            {
                try
                {
                    var info = await services.GetRequiredService<ServerFeatures>().GetInfoAsync(ct);
                    Console.WriteLine($"server {creds.ApiBaseUrl}: {ServerFeatures.VersionLabel(info)}");
                    if (info?.Features is { Count: > 0 })
                        Console.WriteLine($"server features: {string.Join(", ", info.Features)}");
                }
                catch (HttpRequestException)
                {
                    Console.WriteLine($"server {creds.ApiBaseUrl}: not reachable");
                }
            }

            // Best-effort: refresh the update-available notification cache.
            var updateChecker = services.GetRequiredService<UpdateChecker>();
            await updateChecker.CheckForUpdateNotificationAsync(ct);

            return CliExitCodes.Success;
        });

        return cmd;
    }
}
