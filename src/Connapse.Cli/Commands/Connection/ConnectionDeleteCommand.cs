using System.CommandLine;
using System.Net;
using System.Text.Json;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Connection;

public static class ConnectionDeleteCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var connectionArg = ConnectionHelpers.ConnectionArgument();

        var cmd = new Command("delete", "Delete a connection. Its sources must be deleted first.");
        cmd.Arguments.Add(connectionArg);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var id = await ConnectionHelpers.ResolveIdAsync(client, parseResult.GetRequiredValue(connectionArg), ct);

            // The server deletes only when told the name, so a mistyped ID can't remove the wrong one.
            var connection = await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope,
                () => client.Api.V1.Connections[id].GetAsync(cancellationToken: ct))
                ?? throw new CliServerError("The server returned no connection.");

            try
            {
                await AdminHelpers.AdminAsync(AdminHelpers.SourcesScope, () => client.Api.V1.Connections[id].DeleteAsync(
                    r => r.QueryParameters.ConfirmName = connection.Name, ct));
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new CliValidationError(InUseMessage(ex));
            }

            AdminHelpers.WriteSuccess($"Deleted connection '{connection.Name}'.");
            return CliExitCodes.Success;
        }));

        return cmd;
    }

    /// <summary>The server's refusal, with the sources that still read from the connection.</summary>
    private static string InUseMessage(ApiRequestException ex)
    {
        var names = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(ex.ResponseBody ?? "");
            if (doc.RootElement.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (var source in sources.EnumerateArray())
                {
                    if (source.TryGetProperty("name", out var name) && name.GetString() is { } n)
                        names.Add(n);
                }
            }
        }
        catch (JsonException)
        {
        }

        var message = ex.Problem?.Detail ?? ex.Problem?.Title ?? ex.Message;
        return names.Count == 0
            ? message
            : $"{message}{Environment.NewLine}  Delete these sources first: {string.Join(", ", names)}";
    }
}
