using System.Text.Json;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Approval;

/// <summary>
/// Deletes that wait for the owner (CC-10). The CLI sends a delete as a call to the server's MCP tool, because that
/// is where the server holds a destructive call for a person to approve; a held delete exits with
/// <see cref="CliExitCodes.PendingApproval"/> and says how to follow it up.
/// </summary>
internal static class ApprovalHelpers
{
    /// <summary>
    /// Calls the delete tool. Gives null when the MCP route refuses the sign-in, as it does an Editor's; the caller
    /// then deletes through the REST route, which the server does not hold.
    /// </summary>
    public static async Task<McpToolResult?> CallToolAsync(
        IServiceProvider services, string tool, IReadOnlyDictionary<string, string> arguments, CancellationToken ct)
    {
        try
        {
            return await new McpToolClient(services.GetRequiredService<ApiClient>()).CallAsync(tool, arguments, ct);
        }
        catch (McpRouteForbiddenException)
        {
            return null;
        }
    }

    /// <summary>Says the call was held, where to approve it and how to wait for the decision, then gives the exit code.</summary>
    public static int WriteHeld(IServiceProvider services, Guid requestId, bool json)
    {
        string approveAt = $"{ServerUrl(services)}/approvals";
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pending-approval", requestId, approveAt }));
            return CliExitCodes.PendingApproval;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Held for approval (request {requestId}).");
        Console.ResetColor();
        Console.WriteLine("Nothing is deleted until the server's owner approves it.");
        Console.WriteLine($"Approve or deny it at {approveAt}, then run this same command again to delete.");
        Console.WriteLine($"To wait for the decision: connapse approval status {requestId} --wait 50");
        return CliExitCodes.PendingApproval;
    }

    private static string ServerUrl(IServiceProvider services) =>
        (services.GetRequiredService<CredentialStore>().Load()?.ApiBaseUrl ?? "").TrimEnd('/');
}
