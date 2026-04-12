using System.CommandLine;
using System.Net;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

public static class AuthPatRevokeCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var idArg = new Argument<string>("id")
        {
            Description = "The PAT ID (GUID) to revoke.",
        };

        var cmd = new Command("revoke", "Revoke a personal access token.");
        cmd.Arguments.Add(idArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var idStr = parseResult.GetRequiredValue(idArg);

            if (!Guid.TryParse(idStr, out _))
                throw new CliValidationError($"'{idStr}' is not a valid PAT ID (expected GUID format).");

            var apiClient = services.GetRequiredService<ApiClient>();

            Console.Write($"Revoking PAT {idStr}... ");

            try
            {
                await apiClient.DeleteAsync($"/api/v1/auth/pats/{idStr}", ct);
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Not found or already revoked.");
                Console.ResetColor();
                return CliExitCodes.UserError;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Revoked");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
