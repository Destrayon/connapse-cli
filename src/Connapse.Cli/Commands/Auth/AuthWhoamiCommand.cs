using System.CommandLine;
using Connapse.Cli.Models;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Auth;

public static class AuthWhoamiCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var whoami = new Command("whoami", "Show the current authenticated identity.");

        whoami.SetAction(services.WithOrgContext(orgOption, async (_, ct) =>
        {
            var credStore = services.GetRequiredService<CredentialStore>();
            var configStore = services.GetRequiredService<ConfigStore>();
            var client = services.GetRequiredService<ConnapseApiClient>();
            var editionDetector = services.GetRequiredService<EditionDetector>();

            var creds = credStore.Load();
            if (creds is null)
                throw new CliAuthError("Not authenticated.");

            var edition = await editionDetector.GetEditionAsync(forceRefresh: true, ct);

            // A server from before /api/v1/auth/me can't say who the token belongs to; the rest still shows.
            Connapse.Client.Models.MeResponse? me = null;
            bool meMissing = false;
            try
            {
                me = await client.Api.V1.Auth.Me.GetAsync(cancellationToken: ct);
            }
            catch (ServerFeatureMissingException)
            {
                meMissing = true;
            }

            var config = configStore.Load();

            Console.WriteLine();
            PrintRow("Server", creds.ApiBaseUrl);
            PrintRow("Edition", edition switch
            {
                ServerEdition.Cloud => "Cloud",
                ServerEdition.SelfHosted => "Self-hosted",
                _ => "Unknown",
            });

            if (me is not null)
            {
                PrintRow("Email", me.Email ?? "");
                if (!string.IsNullOrWhiteSpace(me.DisplayName))
                    PrintRow("Name", me.DisplayName);
                if (me.Roles is { Count: > 0 })
                    PrintRow("Roles", string.Join(", ", me.Roles));
            }
            else if (meMissing)
            {
                PrintRow("Email", $"{creds.UserEmail} (as signed in; this server can't report the user)");
            }

            if (edition == ServerEdition.Cloud)
                PrintRow("Default org", config.DefaultOrgSlug ?? "(none)");

            PrintRow("Token", PrefixPreview(creds.ApiKey));

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Status: Connected");
            Console.ResetColor();

            return CliExitCodes.Success;
        }));

        return whoami;
    }

    private static void PrintRow(string label, string value)
    {
        Console.Write($"  {label,-12} ");
        Console.WriteLine(value);
    }

    private static string PrefixPreview(string token)
    {
        if (token.Length <= 12) return token;
        return token[..12] + "...";
    }
}
