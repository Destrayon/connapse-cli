using System.CommandLine;
using Connapse.Cli.Commands.Org;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for organization operations under <c>connapse org</c>.
/// Cloud-only — all subcommands short-circuit with a friendly message
/// when the server edition is self-hosted.
/// </summary>
public static class OrgCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var org = new Command("org", "Manage the active organization (Cloud only).");

        org.Subcommands.Add(OrgListCommand.Create(services, orgOption));
        org.Subcommands.Add(OrgSwitchCommand.Create(services, orgOption));
        org.Subcommands.Add(OrgCurrentCommand.Create(services, orgOption));

        org.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse org <list|switch|current> [options]");
            return Task.FromResult(0);
        });

        return org;
    }
}
