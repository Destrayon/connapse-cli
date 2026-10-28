using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Agent;

/// <summary><c>agent list|get|create|delete|enable|disable</c>.</summary>
public static class AgentCommands
{
    public static Command CreateList(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("list", "List agents: non-human callers that use API keys.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var agents = await AdminHelpers.AdminAsync(AdminHelpers.UsersScope,
                () => client.Api.V1.Agents.GetAsync(cancellationToken: ct)) ?? [];

            if (agents.Count == 0)
            {
                Console.WriteLine("No agents.");
                return CliExitCodes.Success;
            }

            Console.WriteLine($"{"NAME",-30} {"ACTIVE",-7} {"KEYS",5} ID");
            foreach (var a in agents)
                Console.WriteLine($"{a.Name,-30} {(a.IsActive == false ? "no" : "yes"),-7} {a.Keys?.Count(k => k.IsRevoked != true) ?? 0,5} {a.Id}");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateGet(IServiceProvider services, Option<string?> orgOption)
    {
        var agentArg = AgentHelpers.AgentArgument();
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON." };

        var cmd = new Command("get", "Show an agent and how many keys it has.");
        cmd.Arguments.Add(agentArg);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await AgentHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(agentArg), ct);
            var agent = await AdminHelpers.AdminAsync(AdminHelpers.UsersScope,
                () => client.Api.V1.Agents[id].GetAsync(cancellationToken: ct))
                ?? throw new CliServerError("The server returned no agent.");

            if (parseResult.GetValue(jsonOption))
                Console.WriteLine(AdminHelpers.ToJson(agent));
            else
                AgentHelpers.Print(agent);
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateCreate(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name") { Description = "2-64 letters, digits, hyphens and underscores." };
        var descriptionOption = new Option<string?>("--description") { Description = "A description." };

        var cmd = new Command("create", "Create an agent. Give it a key with 'agent key create'.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(descriptionOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var agent = await AdminHelpers.AdminAsync(AdminHelpers.UsersScope, () => client.Api.V1.Agents.PostAsync(
                new CreateAgentRequest
                {
                    Name = parseResult.GetRequiredValue(nameArg),
                    Description = parseResult.GetValue(descriptionOption),
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no agent.");

            AdminHelpers.WriteSuccess($"Created agent '{agent.Name}' ({agent.Id}).");
            Console.WriteLine($"Give it a key with 'connapse agent key create {agent.Name} --name <key-name> --scope knowledge:read'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateDelete(IServiceProvider services, Option<string?> orgOption)
    {
        var agentArg = AgentHelpers.AgentArgument();
        var yesOption = new Option<bool>("--yes") { Description = "Confirm: deleting an agent revokes every key it has." };

        var cmd = new Command("delete", "Delete an agent and revoke all its keys.");
        cmd.Arguments.Add(agentArg);
        cmd.Options.Add(yesOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            // The server takes no name to confirm a delete, so the flag stands in for one.
            if (!parseResult.GetValue(yesOption))
                throw new CliValidationError("Deleting an agent revokes every key it has; pass --yes to confirm.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            var nameOrId = parseResult.GetRequiredValue(agentArg);
            var id = await AgentHelpers.ResolveIdAsync(client, nameOrId, ct);

            await AdminHelpers.AdminAsync(AdminHelpers.UsersScope,
                () => client.Api.V1.Agents[id].DeleteAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Deleted agent '{nameOrId}' and revoked its keys.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateSetActive(IServiceProvider services, Option<string?> orgOption, bool active)
    {
        var agentArg = AgentHelpers.AgentArgument();

        var cmd = active
            ? new Command("enable", "Let a disabled agent's keys work again.")
            : new Command("disable", "Stop an agent's keys working without revoking them.");
        cmd.Arguments.Add(agentArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var nameOrId = parseResult.GetRequiredValue(agentArg);
            var id = await AgentHelpers.ResolveIdAsync(client, nameOrId, ct);

            await AdminHelpers.AdminAsync(AdminHelpers.UsersScope, () => client.Api.V1.Agents[id].Active.PutAsync(
                new SetAgentActiveRequest { IsActive = active }, cancellationToken: ct));

            AdminHelpers.WriteSuccess($"{(active ? "Enabled" : "Disabled")} agent '{nameOrId}'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
