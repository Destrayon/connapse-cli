using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Agent;

/// <summary><c>agent key list|create|revoke</c>.</summary>
public static class AgentKeyCommands
{
    private static readonly string[] AgentScopes = ["knowledge:read", "knowledge:write", "agent:ingest"];

    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var key = new Command("key", "Manage an agent's API keys.");
        key.Subcommands.Add(CreateList(services, orgOption));
        key.Subcommands.Add(CreateCreate(services, orgOption));
        key.Subcommands.Add(CreateRevoke(services, orgOption));
        key.SetAction((_, _) =>
        {
            Console.Out.WriteLine("Usage: connapse agent key <list|create|revoke> <agent> [options]");
            return Task.FromResult(0);
        });
        return key;
    }

    private static Command CreateList(IServiceProvider services, Option<string?> orgOption)
    {
        var agentArg = AgentHelpers.AgentArgument();

        var cmd = new Command("list", "List an agent's keys, revoked ones included. Tokens are never shown again.");
        cmd.Arguments.Add(agentArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await AgentHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(agentArg), ct);
            var keys = await AdminHelpers.AdminAsync(AdminHelpers.UsersScope,
                () => client.Api.V1.Agents[id].Keys.GetAsync(cancellationToken: ct)) ?? [];

            if (keys.Count == 0)
            {
                Console.WriteLine("No keys.");
                return CliExitCodes.Success;
            }

            Console.WriteLine($"{"NAME",-24} {"PREFIX",-12} {"STATE",-8} {"LAST USED",-16} {"SCOPES",-40} ID");
            foreach (var k in keys)
            {
                var state = k.IsRevoked == true ? "revoked" : k.ExpiresAt < DateTimeOffset.UtcNow ? "expired" : "active";
                Console.WriteLine(
                    $"{k.Name,-24} {k.TokenPrefix,-12} {state,-8} {AdminHelpers.Date(k.LastUsedAt),-16} {string.Join(",", k.Scopes ?? []),-40} {k.Id}");
            }
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    private static Command CreateCreate(IServiceProvider services, Option<string?> orgOption)
    {
        var agentArg = AgentHelpers.AgentArgument();
        var nameOption = new Option<string>("--name") { Description = "A name for the key.", Required = true };
        var scopeOption = new Option<string[]>("--scope")
        {
            Description = $"What the key may do: {string.Join(", ", AgentScopes)}. Repeat for more than one.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var expiresOption = new Option<int?>("--expires-in-days") { Description = "Days until the key stops working. Without it, it never expires." };
        var approvalOption = new Option<bool?>("--requires-approval") { Description = "Hold the key's writes for an admin's approval (true or false). Without it, the server's default applies." };

        var cmd = new Command("create", "Create an API key for an agent. The token is shown once.");
        cmd.Arguments.Add(agentArg);
        cmd.Options.Add(nameOption);
        cmd.Options.Add(scopeOption);
        cmd.Options.Add(expiresOption);
        cmd.Options.Add(approvalOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var scopes = parseResult.GetRequiredValue(scopeOption);
            if (scopes.FirstOrDefault(s => !AgentScopes.Contains(s)) is { } bad)
                throw new CliValidationError($"'{bad}' isn't an agent scope; use {string.Join(", ", AgentScopes)}.");
            if (parseResult.GetValue(expiresOption) is <= 0)
                throw new CliValidationError("--expires-in-days must be at least 1.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await AgentHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(agentArg), ct);

            var created = await AdminHelpers.AdminAsync(AdminHelpers.UsersScope, () => client.Api.V1.Agents[id].Keys.PostAsync(
                new CreateAgentKeyRequest
                {
                    Name = parseResult.GetRequiredValue(nameOption),
                    Scopes = [.. scopes],
                    ExpiresAt = parseResult.GetValue(expiresOption) is { } days ? DateTimeOffset.UtcNow.AddDays(days) : null,
                    RequiresApproval = parseResult.GetValue(approvalOption),
                }, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no key.");

            AdminHelpers.WriteSuccess($"Created key {created.KeyId} with {string.Join(", ", created.Scopes ?? [])}.");
            AdminHelpers.WriteWarning("Copy the token now; it is not shown again.");
            Console.WriteLine(created.Token);
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    private static Command CreateRevoke(IServiceProvider services, Option<string?> orgOption)
    {
        var agentArg = AgentHelpers.AgentArgument();
        var keyArg = new Argument<Guid>("key-id") { Description = "The key's ID, from 'agent key list'." };

        var cmd = new Command("revoke", "Revoke one of an agent's keys. It stops working at once.");
        cmd.Arguments.Add(agentArg);
        cmd.Arguments.Add(keyArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await AgentHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(agentArg), ct);
            var keyId = parseResult.GetRequiredValue(keyArg);

            await AdminHelpers.AdminAsync(AdminHelpers.UsersScope,
                () => client.Api.V1.Agents[id].Keys[keyId].DeleteAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Revoked key {keyId}.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
