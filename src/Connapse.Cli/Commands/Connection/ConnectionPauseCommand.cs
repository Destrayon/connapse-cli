using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

/// <summary><c>connection pause</c> and <c>connection resume</c>: stop or restart syncing every source on a connection.</summary>
public static class ConnectionPauseCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();

        var cmd = new Command("pause", "Stop syncing every source on a connection until it's resumed.");
        cmd.Arguments.Add(connectionArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            var state = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.V1.Connections[id].Pause.PostAsync(cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Paused syncing for {state?.Sources ?? 0} source(s).");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    public static Command CreateResume(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();
        var sourceOption = new Option<Guid[]>("--source")
        {
            Description = "Resume only these sources (by ID); repeat for more. Without it, all of them.",
            AllowMultipleArgumentsPerToken = true,
        };

        var cmd = new Command("resume", "Restart syncing a paused connection's sources.");
        cmd.Arguments.Add(connectionArg);
        cmd.Options.Add(sourceOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);
            var sources = parseResult.GetValue(sourceOption) ?? [];

            var state = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.V1.Connections[id].Resume.PostAsync(
                    new ResumeConnectionRequest { SourceIds = sources.Length == 0 ? null : sources.Select(s => (Guid?)s).ToList() },
                    cancellationToken: ct));

            AdminHelpers.WriteSuccess($"Resumed syncing for {state?.Sources ?? 0} source(s).");
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
