using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Connapse.Cli.Commands.Memory;

/// <summary><c>connapse memory save</c>: POST /api/memory/spaces/{id}/facts (memory.remember).</summary>
public static class MemorySaveCommand
{
    public static Command Create(IServiceProvider services, Option<string?> orgOption)
    {
        var subjectArg = new Argument<string>("subject") { Description = "What the fact is about, e.g. 'billing-service'." };
        var attributeArg = new Argument<string>("attribute") { Description = "Which property of the subject, e.g. 'owner'." };
        var valueArg = new Argument<string>("value") { Description = "The value, e.g. 'team-payments'." };
        var spaceOption = MemoryHelpers.SpaceOption(
            "Required: the space to save into, by name or ID, e.g. 'team-notes'.");
        var validFromOption = new Option<string?>("--valid-from")
        {
            Description = "When the fact became true: an ISO 8601 date or time; one without an offset is UTC.",
        };
        var validToOption = new Option<string?>("--valid-to")
        {
            Description = "When the fact stopped being true: an ISO 8601 date or time.",
        };
        var sourceOption = new Option<string[]>("--source")
        {
            Description = "A document ID, or 'node:<id>', the fact was learned from. Repeat for several.",
        };
        var reasonOption = new Option<string?>("--reason") { Description = "Why the fact is being saved." };
        var jsonOption = MemoryHelpers.JsonOption();

        var cmd = new Command("save", "Save a fact to a memory space.");
        cmd.Arguments.Add(subjectArg);
        cmd.Arguments.Add(attributeArg);
        cmd.Arguments.Add(valueArg);
        cmd.Options.Add(spaceOption);
        cmd.Options.Add(validFromOption);
        cmd.Options.Add(validToOption);
        cmd.Options.Add(sourceOption);
        cmd.Options.Add(reasonOption);
        cmd.Options.Add(jsonOption);

        cmd.SetAction(services.WithOrgContext(orgOption, async (parseResult, ct) =>
        {
            var client = services.GetRequiredService<ConnapseApiClient>();
            var space = await MemoryHelpers.RequireSpaceAsync(parseResult, spaceOption, client, ct);
            var sources = parseResult.GetValue(sourceOption);
            var body = new RememberMemoryFactRequest
            {
                Subject = parseResult.GetRequiredValue(subjectArg),
                Attribute = parseResult.GetRequiredValue(attributeArg),
                Value = parseResult.GetRequiredValue(valueArg),
                ValidFrom = parseResult.GetValue(validFromOption),
                ValidTo = parseResult.GetValue(validToOption),
                Sources = sources is { Length: > 0 } ? [.. sources] : null,
                Reason = parseResult.GetValue(reasonOption),
            };

            var saved = await MemoryHelpers.WriteAsync(() =>
                client.Api.Memory.Spaces[space].Facts.PostAsync(body, cancellationToken: ct))
                ?? throw new CliServerError("The server returned no saved fact.");

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(AdminHelpers.ToJson(saved));
                return CliExitCodes.Success;
            }

            AdminHelpers.WriteSuccess($"Saved to {saved.SavedTo?.Name ?? space}:");
            if (saved.Fact is not null)
                MemoryHelpers.Print(saved.Fact, saved.SavedTo?.Name);
            return CliExitCodes.Success;
        }));

        return cmd;
    }
}
