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
        var idArg = new Argument<string>("request-id") { Description = "The request or deletion batch ID a held delete printed." };
        var waitOption = new Option<int>("--wait")
        {
            Description = $"Wait up to this many seconds (0-{MaxWait}) for a decision while the request is pending.",
            DefaultValueFactory = _ => 0,
        };
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's answer as JSON." };

        var cmd = new Command("status",
            "Show whether the owner approved a held delete. Exits 0 when approved (for a deletion batch, when every item is gone), 3 while pending, 1 when denied or expired.");
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
                // A held delete of knowledge prints a deletion batch's id instead (Connapse 0.4.2, CON-330).
                return await BatchStatusAsync(api, requestId, wait, parseResult.GetValue(jsonOption), ct);
            }

            ApprovalStatus? status;
            try
            {
                status = JsonSerializer.Deserialize<ApprovalStatus>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new CliServerError($"The server's approval status could not be read: {ex.Message}");
            }
            if (status is null)
                throw new CliServerError("The server returned no approval status.");
            if (status.State == ApprovalState.Unknown)
                throw new CliServerError("The server's approval status has no state.");

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

    /// <summary>
    /// GET /api/approvals/deletions/{id} (approvals.batches.status): a held delete that became a batch the server
    /// deletes itself once approved. Exits 0 when it is done and every item is gone, 3 while it is pending or
    /// running, and 1 otherwise.
    /// </summary>
    private static async Task<int> BatchStatusAsync(ApiClient api, Guid batchId, int wait, bool json, CancellationToken ct)
    {
        string body;
        try
        {
            using var response = await api.SendRawAsync(HttpMethod.Get, $"/api/approvals/deletions/{batchId}?wait={wait}", null, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CliValidationError(
                $"No approval request or deletion batch {batchId} for this sign-in. A request can only be read with the sign-in that made it.");
        }

        DeletionBatch? batch;
        try
        {
            batch = JsonSerializer.Deserialize<DeletionBatch>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CliServerError($"The server's deletion batch could not be read: {ex.Message}");
        }
        if (batch is null || string.IsNullOrWhiteSpace(batch.State))
            throw new CliServerError("The server's deletion batch has no state.");

        string state = batch.State.ToLowerInvariant();
        var items = batch.Items ?? [];
        bool allGone = items.Count > 0 && items.All(i => i.State?.ToLowerInvariant() is "deleted" or "skipped");
        int exit = state switch
        {
            "pending" or "running" => CliExitCodes.PendingApproval,
            "done" when allGone => CliExitCodes.Success,
            _ => CliExitCodes.UserError,
        };

        if (json)
        {
            Console.WriteLine(body);
            return exit;
        }

        Console.ForegroundColor = exit switch
        {
            CliExitCodes.Success => ConsoleColor.Green,
            CliExitCodes.PendingApproval => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };
        Console.WriteLine($"{Capitalized(state)}: deletion batch {batch.Id}");
        Console.ResetColor();
        if (!string.IsNullOrWhiteSpace(batch.Reason))
            Console.WriteLine($"Reason: {batch.Reason}");
        foreach (var item in items.OrderBy(i => i.Position))
            Console.WriteLine($"  {item.State,-12} {item.Kind} {item.Label}{(string.IsNullOrWhiteSpace(item.Outcome) ? "" : $" ({item.Outcome})")}");
        if (!string.IsNullOrWhiteSpace(batch.Message))
            Console.WriteLine(batch.Message);
        return exit;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    internal sealed record DeletionBatch(Guid Id, string? State, string? Reason, List<DeletionBatchItem>? Items, string? Message);

    internal sealed record DeletionBatchItem(int Position, string? Kind, string? Label, string? State, string? Outcome);

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
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary><see cref="Unknown"/> is what a status without a state reads as, so it can't pass for Pending.</summary>
    internal enum ApprovalState { Unknown, Pending, Approved, Denied, Expired, Used }

    internal sealed record ApprovalStatus(Guid Id, string ToolName, ApprovalState State, string? Reason, string? Message);
}
