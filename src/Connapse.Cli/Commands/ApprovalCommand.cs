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
        var approval = new Command("approval", "Follow up a delete that is waiting for the server owner's approval.");

        approval.Subcommands.Add(ApprovalStatusCommand.Create(services, orgOption));

        approval.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse approval status <request-id> [--wait <seconds>]");
            return Task.FromResult(0);
        });

        return approval;
    }
}
