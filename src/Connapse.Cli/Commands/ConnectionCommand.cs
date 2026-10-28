using System.CommandLine;
using Connapse.Cli.Commands.Connection;

namespace Connapse.Cli.Commands;

/// <summary>
/// Parent command for connection operations under <c>connapse connection</c>. Needs the Owner or
/// Admin role, and a token with the <c>admin:sources</c> scope.
/// </summary>
public static class ConnectionCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connection = new Command("connection", "Manage connections: the accounts and servers sources read from.");

        connection.Subcommands.Add(ConnectionListCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionGetCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionCreateCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionUpdateCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionDeleteCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionPauseCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionPauseCommand.CreateResume(services, orgOption));
        connection.Subcommands.Add(ConnectionTestCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionDiscoverCommand.Create(services, orgOption));
        connection.Subcommands.Add(ConnectionSecretCommands.CreateForgetHostKey(services, orgOption));
        connection.Subcommands.Add(ConnectionSecretCommands.CreateReplaceSecret(services, orgOption));

        connection.SetAction((_, _) =>
        {
            Console.Out.WriteLine(
                "Usage: connapse connection <list|get|create|update|delete|pause|resume|test|discover|forget-host-key|replace-secret> [options]");
            return Task.FromResult(0);
        });

        return connection;
    }
}
