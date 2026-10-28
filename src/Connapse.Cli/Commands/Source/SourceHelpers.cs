using System.CommandLine;
using Connapse.Cli.Commands.Admin;
using Connapse.Cli.Services;
using Connapse.Client;
using Connapse.Client.Models;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Connapse.Cli.Commands.Source;

/// <summary>Name-or-ID lookup, scope parsing, and how a source is printed.</summary>
internal static class SourceHelpers
{
    /// <summary>The scope types the generated client knows, as a scope's "type" names them.</summary>
    public const string ScopeTypes = "azureBlob, confluence, custom, filesystem, github, jira, s3, sftp";

    public static Argument<string> SourceArgument() => new("source")
    {
        Description = "The source's name or ID.",
    };

    public static Task<Guid> ResolveIdAsync(ConnapseApiClient client, string nameOrId, CancellationToken ct) =>
        AdminHelpers.ResolveIdAsync<SourceResponse>(
            nameOrId,
            "source",
            async (skip, take) =>
            {
                var page = await client.Api.Sources.GetAsync(r =>
                {
                    r.QueryParameters.Skip = skip;
                    r.QueryParameters.Take = take;
                }, ct);
                return (page?.Items ?? [], page?.HasMore ?? false);
            },
            s => s.Name,
            s => s.Id);

    /// <summary>
    /// A scope's JSON as the generated client's typed scope, picked by its "type". A type the client
    /// doesn't know is refused here rather than sent as an empty scope.
    /// </summary>
    public static CreateSourceApiRequest.CreateSourceApiRequest_scope ParseScope(string json, string optionName)
    {
        AdminHelpers.ParseJsonObject(json, optionName);
        var node = AdminHelpers.ParseJson(json, optionName);
        var scope = node.GetObjectValue(CreateSourceApiRequest.CreateSourceApiRequest_scope.CreateFromDiscriminatorValue);
        var known = scope is not null && (
            scope.SourceScopeAzureBlobContainerScope is not null ||
            scope.SourceScopeConfluenceSpaceScope is not null ||
            scope.SourceScopeCustomSourceScope is not null ||
            scope.SourceScopeFilesystemFolderScope is not null ||
            scope.SourceScopeGitHubRepositoryScope is not null ||
            scope.SourceScopeJiraProjectScope is not null ||
            scope.SourceScopeS3BucketScope is not null ||
            scope.SourceScopeSftpFolderScope is not null);
        if (!known)
            throw new CliValidationError($"{optionName} needs a \"type\": one of {ScopeTypes}. 'connapse source scope-schema <provider>' shows each one's fields.");
        return scope!;
    }

    public static void Print(SourceResponse s)
    {
        Console.WriteLine($"{s.Name}  ({s.Id})");
        if (!string.IsNullOrEmpty(s.Description))
            Console.WriteLine($"  {s.Description}");
        Console.WriteLine($"  Kind:        {s.Kind}");
        if (!string.IsNullOrEmpty(s.Summary))
            Console.WriteLine($"  Scope:       {s.Summary}");
        Console.WriteLine($"  Connection:  {s.ConnectionId}");
        Console.WriteLine($"  Enabled:     {(s.Enabled == false ? "no" : "yes")}");
        Console.WriteLine($"  Documents:   {s.DocumentCount ?? 0} ({s.ProcessingDocumentCount ?? 0} processing, {s.FailedDocumentCount ?? 0} failed)");
        Console.WriteLine($"  Sync every:  {(s.SyncIntervalSeconds is { } secs ? $"{secs}s" : "-")}");
        Console.WriteLine($"  Last sync:   {s.LastSyncStatus} at {AdminHelpers.Date(s.LastSyncedAt)}");
        if (!string.IsNullOrEmpty(s.LastSyncError))
            Console.WriteLine($"  Sync error:  {s.LastSyncError}");
        if (s.WithheldDeletions is > 0)
            AdminHelpers.WriteWarning($"  {s.WithheldDeletions} deletion(s) withheld; 'connapse source sync {s.Name} --apply-withheld-deletions' applies them.");
        if (!string.IsNullOrEmpty(s.Warning))
            AdminHelpers.WriteWarning($"  {s.Warning}");
    }
}
