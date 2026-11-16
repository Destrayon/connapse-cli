using System.CommandLine;
using Connapse.Cli.Commands.Approval;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for <c>connapse approval</c>: deletes the server held for its owner's approval.
/// </summary>
public static class ApprovalCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var approval = new Command("approval", "Ask for deletions the server owner approves, and follow up a delete that is waiting for them.");

        approval.Subcommands.Add(ApprovalStatusCommand.Create(services, orgOption));
        approval.Subcommands.Add(ApprovalRequestCommand.Create(services, orgOption));

        approval.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse approval status <request-id> [--wait <seconds>]");
            Console.Out.WriteLine("       connapse approval request --reason <text> [--container <c> --file <f> --folder <p>] [--empty-container <c>] [--space <s>] [--fact <id>]");
            return Task.FromResult(0);
        });

        return approval;
    }
}
