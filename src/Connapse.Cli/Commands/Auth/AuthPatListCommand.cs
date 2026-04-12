using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

public static class AuthPatListCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var cmd = new Command("list", "List your personal access tokens.");

        cmd.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var apiClient = services.GetRequiredService<ApiClient>();
            var pats = await apiClient.GetAsync<List<PatListItem>>("/api/v1/auth/pats", ct);

            if (pats is null || pats.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No personal access tokens found.");
                Console.ResetColor();
                return CliExitCodes.Success;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Personal access tokens ({pats.Count}):");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var pat in pats)
            {
                var (status, statusColor) = DescribeStatus(pat);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {pat.Name}");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  [{pat.Id}]");
                Console.ResetColor();

                Console.Write("  ");
                Console.ForegroundColor = statusColor;
                Console.WriteLine(status);
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"    Prefix: {pat.TokenPrefix}...  Created: {pat.CreatedAt.ToLocalTime():yyyy-MM-dd}");
                if (pat.ExpiresAt.HasValue)
                    Console.Write($"  Expires: {pat.ExpiresAt.Value.ToLocalTime():yyyy-MM-dd}");
                if (pat.LastUsedAt.HasValue)
                    Console.Write($"  Last used: {pat.LastUsedAt.Value.ToLocalTime():yyyy-MM-dd}");
                Console.WriteLine();
                Console.ResetColor();
            }

            return CliExitCodes.Success;
        }));

        return cmd;
    }

    private static (string Status, ConsoleColor Color) DescribeStatus(PatListItem pat)
    {
        if (pat.IsRevoked)
            return ("REVOKED", ConsoleColor.Red);

        if (pat.ExpiresAt.HasValue && pat.ExpiresAt < DateTime.UtcNow)
            return ("EXPIRED", ConsoleColor.Yellow);

        return ("Active", ConsoleColor.Green);
    }
}
