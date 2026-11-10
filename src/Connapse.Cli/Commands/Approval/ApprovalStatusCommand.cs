using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Connapse.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Approval;

/// <summary>
/// <c>connapse approval status</c>: GET /api/approvals/{id} (approvals.status). Where a delete the server held for
/// the owner's approval stands, optionally waiting for the decision.
/// </summary>
public static class ApprovalStatusCommand
{
    private const int MaxWait = 50;

    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var idArg = new Argument<string>("request-id") { Description = "The request ID a held delete printed." };
        var waitOption = new Option<int>("--wait")
        {
            Description = $"Wait up to this many seconds (0-{MaxWait}) for a decision while the request is pending.",
            DefaultValueFactory = _ => 0,
        };
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's answer as JSON." };

        var cmd = new Command("status",
            "Show whether the owner approved a held delete. Exits 0 when approved, 3 while pending, 1 when denied or expired.");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(waitOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var id = parseResult.GetRequiredValue(idArg);
            if (!Guid.TryParse(id, out var requestId))
                throw new CliValidationError($"'{id}' is not a request ID; a held delete prints it.");
            int wait = parseResult.GetValue(waitOption);
            if (wait is < 0 or > MaxWait)
                throw new CliValidationError($"--wait must be between 0 and {MaxWait} seconds.");

            var api = services.GetRequiredService<ApiClient>();
            string body;
            try
            {
                using var response = await api.SendRawAsync(HttpMethod.Get, $"/api/approvals/{requestId}?wait={wait}", null, ct);
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CliValidationError(
                    $"No approval request {requestId} for this sign-in. A request can only be read with the sign-in that made it.");
            }

            var status = JsonSerializer.Deserialize<ApprovalStatus>(body, JsonOptions)
                ?? throw new CliServerError("The server returned no approval status.");

            if (parseResult.GetValue(jsonOption))
                Console.WriteLine(body);
            else
                WriteText(status);

            return status.State switch
            {
                ApprovalState.Approved or ApprovalState.Used => CliExitCodes.Success,
                ApprovalState.Pending => CliExitCodes.PendingApproval,
                _ => CliExitCodes.UserError,
            };
        }));

        return cmd;
    }

    private static void WriteText(ApprovalStatus status)
    {
        Console.ForegroundColor = status.State switch
        {
            ApprovalState.Approved or ApprovalState.Used => ConsoleColor.Green,
            ApprovalState.Pending => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };
        Console.WriteLine($"{status.State}: {status.ToolName} (request {status.Id})");
        Console.ResetColor();
        if (!string.IsNullOrWhiteSpace(status.Reason))
            Console.WriteLine($"Reason: {status.Reason}");
        if (!string.IsNullOrWhiteSpace(status.Message))
            Console.WriteLine(status.Message);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal enum ApprovalState { Pending, Approved, Denied, Expired, Used }

    internal sealed record ApprovalStatus(Guid Id, string ToolName, ApprovalState State, string? Reason, string? Message);
}
