using System.CommandLine;
using Connapse.Cli.Commands.Agent;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for agents under <c>connapse agent</c>. Every subcommand needs the Owner or
/// Admin role, and a token needs <c>admin:users</c>.
/// </summary>
public static class AgentCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var agent = new Command("agent", "Manage agents and their API keys.");

        agent.Subcommands.Add(AgentCommands.CreateList(services, orgOption));
        agent.Subcommands.Add(AgentCommands.CreateGet(services, orgOption));
        agent.Subcommands.Add(AgentCommands.CreateCreate(services, orgOption));
        agent.Subcommands.Add(AgentCommands.CreateDelete(services, orgOption));
        agent.Subcommands.Add(AgentCommands.CreateSetActive(services, orgOption, active: true));
        agent.Subcommands.Add(AgentCommands.CreateSetActive(services, orgOption, active: false));
        agent.Subcommands.Add(AgentKeyCommands.Create(services, orgOption));

        agent.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse agent <list|get|create|delete|enable|disable|key> [options]");
            return Task.FromResult(0);
        });

        return agent;
    }
}
