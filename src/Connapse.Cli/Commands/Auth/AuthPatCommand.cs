using System.CommandLine;

namespace Connapse.Cli.Commands.Auth;

/// <summary>
/// Parent command for personal access token operations under
/// <c>connapse auth pat</c>.
/// </summary>
public static class AuthPatCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var pat = new Command("pat", "Manage personal access tokens.");

        pat.Subcommands.Add(AuthPatCreateCommand.Create(services, orgOption));
        pat.Subcommands.Add(AuthPatListCommand.Create(services, orgOption));
        pat.Subcommands.Add(AuthPatRevokeCommand.Create(services, orgOption));

        pat.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse auth pat <create|list|revoke> [options]");
            return Task.FromResult(0);
        });

        return pat;
    }
}
