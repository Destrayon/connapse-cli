using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

public static class AuthLogoutCommand
{
    public static Command Create(IServiceProvider services)
    {
        var logout = new Command("logout", "Clear stored credentials.");

        logout.SetAction((_, _) =>
        {
            var credStore = services.GetRequiredService<CredentialStore>();
            var configStore = services.GetRequiredService<ConfigStore>();
            var profile = services.GetRequiredService<ProfileContext>().Current;

            var current = credStore.Load();
            if (current is null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(profile.IsDefault
                    ? "Not logged in (no credentials found)."
                    : $"Not logged in to profile '{profile.Name}' (no credentials found).");
                Console.ResetColor();
                return Task.FromResult(CliExitCodes.Success);
            }

            credStore.Delete();

            var existing = configStore.Load();
            configStore.Save(new CliConfig { ApiBaseUrl = existing.ApiBaseUrl });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(profile.IsDefault
                ? "Logged out. Credentials cleared."
                : $"Logged out of profile '{profile.Name}'. Credentials cleared.");
            Console.ResetColor();

            return Task.FromResult(CliExitCodes.Success);
        });

        return logout;
    }
}
