using System.CommandLine;
using System.Net;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Approval;

/// <summary>
/// <c>connapse approval request</c>: POST /api/approvals/deletions (approvals.batches.request). Asks the token's owner
/// to approve a batch of deletions of any kind; the server deletes what the owner approves itself.
/// </summary>
public static class ApprovalRequestCommand
{
    /// <summary>The most deletions the server takes in one request.</summary>
    internal const int MaxItems = 50;

    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var reasonOption = new Option<string>("--reason")
        {
            Description = "Why these should be deleted. The owner reads it when deciding.",
            Required = true,
        };
        var containerOption = new Option<string?>("--container")
        {
            Description = "The container, by name or ID, that the --file and --folder items are in.",
        };
        var fileOption = Repeatable("--file", "A file to delete, by ID or path, in --container.");
        var folderOption = Repeatable("--folder", "A folder to delete, by path, in --container.");
        var emptyContainerOption = Repeatable("--empty-container", "An empty container to delete, by name or ID.");
        var spaceOption = Repeatable("--space", "A memory space to delete with its facts, by name or ID.");
        var factOption = Repeatable("--fact", "A remembered fact to delete, by ID.");
        var jsonOption = new Option<bool>("--json") { Description = "Print the server's JSON." };

        var cmd = new Command("request",
            $"Ask the owner of your sign-in to approve up to {MaxItems} deletions at once. The server deletes what they approve; exits 3 while it waits.");
        cmd.Options.Add(reasonOption);
        cmd.Options.Add(containerOption);
        cmd.Options.Add(fileOption);
        cmd.Options.Add(folderOption);
        cmd.Options.Add(emptyContainerOption);
        cmd.Options.Add(spaceOption);
        cmd.Options.Add(factOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var reason = parseResult.GetRequiredValue(reasonOption);
            if (string.IsNullOrWhiteSpace(reason))
                throw new CliValidationError("--reason can't be empty; the owner reads it when deciding.");
            var container = parseResult.GetValue(containerOption);
            var files = Values(parseResult, fileOption);
            var folders = Values(parseResult, folderOption);
            if ((files.Count > 0 || folders.Count > 0) && string.IsNullOrWhiteSpace(container))
                throw new CliValidationError("Pass --container to say which container the --file and --folder items are in.");
            foreach (var fact in Values(parseResult, factOption))
                if (!Guid.TryParse(fact, out _))
                    throw new CliValidationError($"'{fact}' is not a fact ID (expected GUID format).");

            // A fact is found by its id alone, so the server ignores its container.
            var items = files.Select(f => Item("file", container!, f, reason))
                .Concat(folders.Select(f => Item("folder", container!, f, reason)))
                .Concat(Values(parseResult, emptyContainerOption).Select(c => Item("container", c, null, reason)))
                .Concat(Values(parseResult, spaceOption).Select(s => Item("memory_space", s, null, reason)))
                .Concat(Values(parseResult, factOption).Select(f => Item("fact", "", f, reason)))
                .ToList();
            if (items.Count == 0)
                throw new CliValidationError("Name something to delete: --file, --folder, --empty-container, --space or --fact.");
            if (items.Count > MaxItems)
                throw new CliValidationError($"At most {MaxItems} deletions can be asked for at once; {items.Count} were given.");

            var client = services.GetRequiredService<ConnapseApiClient>();
            DeletionBatchView batch;
            try
            {
                batch = await client.Api.Approvals.Deletions.PostAsync(new RequestDeletionsBody { Items = items }, cancellationToken: ct)
                    ?? throw new CliServerError("The server returned no deletion batch.");
            }
            catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new ApiRequestException(ex.StatusCode,
                    $"{ex.Message}{Environment.NewLine}  Asking for deletions needs a token with the 'knowledge:write' scope.",
                    ex.ResponseBody, ex.Problem);
            }

            int exit = batch.State?.ToLowerInvariant() is "pending" or "running"
                ? CliExitCodes.PendingApproval
                : CliExitCodes.UserError;
            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(batch));
                return exit;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Asked for approval (deletion batch {batch.Id}).");
            Console.ResetColor();
            foreach (var item in (batch.Items ?? []).OrderBy(i => i.Position))
                Console.WriteLine($"  {item.Kind} {item.Label}");
            Console.WriteLine($"Nothing is deleted until the owner approves. Approve or deny it at {ApprovalHelpers.ServerUrl(services)}/approvals;");
            Console.WriteLine("the server then deletes what was approved, so don't run this command again.");
            Console.WriteLine($"To wait for the outcome: connapse approval status {batch.Id} --wait 50");
            return exit;
        }));

        return cmd;
    }

    private static Option<string[]> Repeatable(string name, string description) => new(name)
    {
        Description = $"{description} Give it more than once for several.",
        AllowMultipleArgumentsPerToken = true,
    };

    private static List<string> Values(ParseResult parseResult, Option<string[]> option) =>
        (parseResult.GetValue(option) ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();

    private static DeletionRequestItem Item(string kind, string container, string? target, string reason) =>
        new() { Kind = kind, Container = container, Target = target, Reason = reason };
}
