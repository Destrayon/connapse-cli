using System.CommandLine;

namespace Connapse.Cli.Commands.Auth;

/// <summary>
/// Parent command for authentication operations. Hosts login/logout/whoami
/// (this task) and the pat subtree (Task 5.3).
/// </summary>
public static class AuthCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var auth = new Command("auth", "Authenticate with a Connapse server.");

        auth.Subcommands.Add(AuthLoginCommand.Create(services, orgOption));
        auth.Subcommands.Add(AuthLogoutCommand.Create(services));
        auth.Subcommands.Add(AuthWhoamiCommand.Create(services, orgOption));
        auth.Subcommands.Add(AuthPatCommand.Create(services, orgOption));

        auth.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse auth <login|logout|whoami|pat> [options]");
            return Task.FromResult(0);
        });

        return auth;
    }
}
