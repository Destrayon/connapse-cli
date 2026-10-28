using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Client;
using Connapse.Client.Models;

namespace Connapse.Cli.Commands.Agent;

internal static class AgentHelpers
{
    public static Argument<string> AgentArgument() =>
        new("agent") { Description = "The agent's name or ID." };

    /// <summary>The agents endpoint returns every agent at once, so the lookup is a single page.</summary>
    public static Task<Guid> ResolveIdAsync(ConnapseApiClient client, string nameOrId, CancellationToken ct) =>
        AdminHelpers.AdminAsync(AdminHelpers.UsersScope, () => AdminHelpers.ResolveIdAsync<AgentDto>(
            nameOrId,
            "agent",
            async (_, _) => (await client.Api.V1.Agents.GetAsync(cancellationToken: ct) ?? [], false),
            a => a.Name,
            a => a.Id));

    /// <summary>A key counts as active only while it is neither revoked nor past its expiry.</summary>
    public static bool IsLive(AgentKeyListItem k) =>
        k.IsRevoked != true && !(k.ExpiresAt < DateTimeOffset.UtcNow);

    public static void Print(AgentDto a)
    {
        Console.WriteLine($"{a.Name}  ({a.Id})");
        if (!string.IsNullOrEmpty(a.Description))
            Console.WriteLine($"  {a.Description}");
        Console.WriteLine($"  Active:   {(a.IsActive == false ? "no" : "yes")}");
        Console.WriteLine($"  Created:  {AdminHelpers.Date(a.CreatedAt)}");
        var keys = a.Keys ?? [];
        Console.WriteLine($"  Keys:     {keys.Count(IsLive)} active, {keys.Count(k => k.IsRevoked == true)} revoked");
    }
}
