using System.CommandLine;
using System.Globalization;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

public static class AuthPatCreateCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "A human-readable label for the token.",
        };

        var expiresOption = new Option<string?>("--expires")
        {
            Description = "Expiration date in yyyy-MM-dd format (UTC). " +
                          "Omit for a token that never expires.",
        };

        var cmd = new Command("create", "Create a new personal access token.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(expiresOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var name = parseResult.GetRequiredValue(nameArg);
            var expiresStr = parseResult.GetValue(expiresOption);

            DateTime? expiresAt = null;
            if (!string.IsNullOrWhiteSpace(expiresStr))
            {
                if (!DateTime.TryParseExact(
                        expiresStr,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    throw new CliValidationError(
                        $"Invalid date '{expiresStr}'. Use yyyy-MM-dd format.");
                }
                expiresAt = parsed;
            }

            var client = services.GetRequiredService<ConnapseApiClient>();

            Console.Write($"Creating PAT '{name}'... ");

            var response = await client.Api.V1.Auth.Pats.PostAsync(
                new PatCreateRequest { Name = name, ExpiresAt = expiresAt },
                cancellationToken: ct);

            if (response is null)
                throw new CliServerError("Unexpected response from server.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Created");
            Console.ResetColor();

            Console.WriteLine($"  ID:      {response.Id}");
            Console.WriteLine($"  Name:    {response.Name}");
            Console.WriteLine($"  Expires: {(response.ExpiresAt.HasValue ? response.ExpiresAt.Value.ToLocalTime().ToString("yyyy-MM-dd") : "Never")}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Token (copy now — shown only once):");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  {response.Token}");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
