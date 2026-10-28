using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Client;
using Connapse.Client.Models;

namespace Connapse.Cli.Commands.Connection;

/// <summary>Name-or-ID lookup, the secret options, and how a connection is printed.</summary>
internal static class ConnectionHelpers
{
    public static Argument<string> ConnectionArgument() => new("connection")
    {
        Description = "The connection's name or ID.",
    };

    public static Task<Guid> ResolveIdAsync(ConnapseApiClient client, string nameOrId, CancellationToken ct) =>
        AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => AdminHelpers.ResolveIdAsync<ConnectionResponse>(
            nameOrId,
            "connection",
            async (skip, take) =>
            {
                var page = await client.Api.V1.Connections.GetAsync(r =>
                {
                    r.QueryParameters.Skip = skip;
                    r.QueryParameters.Take = take;
                }, ct);
                return (page?.Items ?? [], page?.HasMore ?? false);
            },
            c => c.Name,
            c => c.Id));

    public static void Print(ConnectionResponse c)
    {
        Console.WriteLine($"{c.Name}  ({c.Id})");
        Console.WriteLine($"  Provider:   {c.Provider}");
        Console.WriteLine($"  Sources:    {c.SourceCount ?? 0}");
        Console.WriteLine($"  Secret:     {(c.HasSecret == true ? "stored" : "none")}");
        Console.WriteLine($"  Sync:       {(c.SyncPaused == true ? "paused" : "running")}");
        if (c.Health is { } health)
        {
            var state = health.Succeeded == true ? (health.Warning == true ? "warning" : "ok") : "failing";
            Console.WriteLine($"  Health:     {state} at {AdminHelpers.Date(health.CheckedAt)}{(string.IsNullOrEmpty(health.Message) ? "" : $" — {health.Message}")}");
        }
        Console.WriteLine($"  Updated:    {AdminHelpers.Date(c.UpdatedAt)}");
    }
}

/// <summary>
/// The secret a connection can carry: a token, or an SSH private key with its passphrase. A token can
/// come from stdin so it stays out of the shell's history.
/// </summary>
internal sealed class SecretOptions
{
    public Option<string?> Token { get; } = new("--token") { Description = "The access token or password." };
    public Option<bool> TokenStdin { get; } = new("--token-stdin") { Description = "Read the token from stdin." };
    public Option<string?> PrivateKeyFile { get; } = new("--private-key-file") { Description = "A file holding the SSH private key (SFTP)." };
    public Option<string?> Passphrase { get; } = new("--passphrase") { Description = "The private key's passphrase." };

    public void AddTo(Command command)
    {
        command.Options.Add(Token);
        command.Options.Add(TokenStdin);
        command.Options.Add(PrivateKeyFile);
        command.Options.Add(Passphrase);
    }

    public (string? Token, string? PrivateKey, string? Passphrase) Read(ParseResult parseResult)
    {
        var token = parseResult.GetValue(Token);
        if (parseResult.GetValue(TokenStdin))
        {
            if (token is not null)
                throw new Services.CliValidationError("Pass --token or --token-stdin, not both.");
            token = AdminHelpers.ReadSecretFromStdin("--token-stdin");
        }

        var keyFile = parseResult.GetValue(PrivateKeyFile);
        var key = keyFile is null ? null : AdminHelpers.ReadFile(keyFile, "--private-key-file");
        return (token, key, parseResult.GetValue(Passphrase));
    }
}
