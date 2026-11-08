using System.CommandLine;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

/// <summary>
/// <c>auth list</c> and <c>auth switch</c>: the saved logins (profiles) and which one commands use when
/// neither <c>--profile</c> nor <c>CONNAPSE_PROFILE</c> picks one.
/// </summary>
public static class AuthProfileCommands
{
    public static Command CreateList(IServiceProvider services)
    {
        var list = new Command("list", "List saved logins (profiles); '*' marks the one commands use now.");

        list.SetAction((_, _) =>
        {
            var context = services.GetRequiredService<ProfileContext>();
            var current = context.Current;

            var profiles = context.Store.List().ToList();
            // A profile named by --profile or CONNAPSE_PROFILE that has nothing saved yet has no folder.
            if (!profiles.Any(p => string.Equals(p.Name, current.Name, StringComparison.OrdinalIgnoreCase)))
                profiles.Add(current);

            foreach (var profile in profiles)
            {
                var creds = CredentialStore.ForProfile(profile).Load();
                var marker = string.Equals(profile.Name, current.Name, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                var login = creds is null
                    ? "(not logged in)"
                    : string.IsNullOrEmpty(creds.UserEmail) ? creds.ApiBaseUrl : $"{creds.ApiBaseUrl}  {creds.UserEmail}";
                Console.WriteLine($"{marker} {profile.Name,-16} {login}");
            }

            return Task.FromResult(CliExitCodes.Success);
        });

        return list;
    }

    public static Command CreateSwitch(IServiceProvider services)
    {
        var nameArgument = new Argument<string>("profile")
        {
            Description = "The saved login commands should use, or 'default'.",
        };

        var switchCommand = new Command("switch", "Choose the saved login (profile) commands use by default.");
        switchCommand.Arguments.Add(nameArgument);

        switchCommand.SetAction((parseResult, _) =>
        {
            var store = services.GetRequiredService<ProfileContext>().Store;
            var profile = store.Get(parseResult.GetRequiredValue(nameArgument).Trim());

            var creds = CredentialStore.ForProfile(profile).Load();
            if (creds is null && !profile.IsDefault)
                throw new CliValidationError(
                    $"No login saved in profile '{profile.Name}'. " +
                    $"Run 'connapse auth login --profile {profile.Name} --server <url>' first.");

            store.SetActive(profile);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(creds is null
                ? $"Commands now use profile '{profile.Name}' (not logged in)."
                : $"Commands now use profile '{profile.Name}' ({creds.ApiBaseUrl}).");
            Console.ResetColor();

            var environment = Environment.GetEnvironmentVariable(ProfileStore.ProfileVariable);
            if (!string.IsNullOrWhiteSpace(environment))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Note: {ProfileStore.ProfileVariable}={environment} is set and still wins in this shell.");
                Console.ResetColor();
            }

            return Task.FromResult(CliExitCodes.Success);
        });

        return switchCommand;
    }
}
