using System.Net;
using System.Text.Json;
using Connapse.Cli.Tests.Testing;

namespace Connapse.Cli.Tests.Commands;

/// <summary>
/// Deletes go to the server's MCP delete tools, where the server holds them for the owner's approval (CC-10), and
/// <c>connapse approval status</c> reports the decision.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ApprovalCommandTests : IDisposable
{
    private const string FileId = "7b1c9e2a-0000-4000-8000-0000000000f1";
    private const string FactId = "5d0a3c1e-0000-4000-8000-0000000000aa";
    private static readonly Guid RequestId = Guid.Parse("0aae0f35-ddd6-457c-a031-aae9f8c11db6");

    private readonly CommandHost _host = new();
    private readonly FakeMcpServer _mcp = new();

    public ApprovalCommandTests() => _host.Respond(_mcp.Respond);

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FilesDelete_CallsTheDeleteToolWithTheContainerAsNamed()
    {
        _mcp.ToolText = (_, _) => $"File 'note.md' (ID: {FileId}) deleted.";

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Deleted file {FileId}.", result.Output);
        var (tool, args) = Assert.Single(_mcp.Calls);
        Assert.Equal("delete_file", tool);
        Assert.Equal("notes", args["containerId"]!.GetValue<string>());
        Assert.Equal(FileId, args["fileId"]!.GetValue<string>());
        Assert.DoesNotContain(_host.Sent, s => s.StartsWith("DELETE /api", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FilesDelete_Held_SaysWhereToApproveAndExits3()
    {
        _mcp.ToolText = (tool, _) => FakeMcpServer.Held(tool, RequestId);

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains($"Held for approval (request {RequestId})", result.Output);
        Assert.Contains("https://server.example/approvals", result.Output);
        Assert.Contains($"connapse approval status {RequestId} --wait 50", result.Output);
        Assert.DoesNotContain("Deleted", result.Output);
    }

    [Fact]
    public async Task FilesDelete_HeldWithoutARequestId_FailsInsteadOfSayingDeleted()
    {
        _mcp.ToolText = (tool, _) => $"'{tool}' is pending approval: ask the person to approve it at /approvals.";

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("named no request", result.Error);
        Assert.DoesNotContain("Deleted", result.Output);
    }

    [Fact]
    public async Task FilesDelete_ToolError_IsAUserError()
    {
        _mcp.ToolText = (_, _) => $"Error: File '{FileId}' not found in this container.";

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"File '{FileId}' not found in this container.", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task FilesDelete_NotAnId_IsRefusedBeforeAnyRequest()
    {
        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", "note.md");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task Delete_OpensASessionUsesItAndClosesIt()
    {
        _mcp.ToolText = (_, _) => "Container 'notes' deleted.";

        await _host.RunAsync("container", "delete", "notes");

        Assert.Equal(["POST /mcp", "POST /mcp", "POST /mcp", "DELETE /mcp"], _host.Sent);
        Assert.Equal(3, _mcp.SessionHeaders.Count);
        Assert.All(_mcp.SessionHeaders, s => Assert.Equal(FakeMcpServer.SessionId, s));
    }

    [Fact]
    public async Task Delete_ReadsAnEventStreamAnswer()
    {
        _mcp.EventStream = true;
        _mcp.ToolText = (tool, _) => FakeMcpServer.Held(tool, RequestId);

        var result = await _host.RunAsync("container", "delete", "notes");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(RequestId.ToString(), result.Output);
    }

    [Fact]
    public async Task ContainerDelete_CallsTheToolByName()
    {
        _mcp.ToolText = (_, _) => "Container 'notes' deleted.";

        var result = await _host.RunAsync("container", "delete", "notes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Deleted container 'notes'.", result.Output);
        var (tool, args) = Assert.Single(_mcp.Calls);
        Assert.Equal("container_delete", tool);
        Assert.Equal("notes", args["containerId"]!.GetValue<string>());
    }

    [Fact]
    public async Task ContainerDelete_NotEmpty_PassesTheServersReason()
    {
        _mcp.ToolText = (_, _) => "Error: Container 'notes' is not empty. Delete all files first.";

        var result = await _host.RunAsync("container", "delete", "notes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("is not empty. Delete all files first.", result.Error);
    }

    private const string ContainerId = "3f2a1b4c-0000-4000-8000-0000000000c1";

    /// <summary>
    /// The MCP route refuses the sign-in, as it does an Editor's; every other route answers from
    /// <paramref name="routes"/>, and an unlisted one answers 404.
    /// </summary>
    private void McpForbidden(params (string Route, string Json)[] routes) =>
        _host.Respond(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/mcp")
                return MockHttpMessageHandler.Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}""");
            string key = $"{request.Method} {request.RequestUri.AbsolutePath}";
            foreach (var (route, json) in routes)
            {
                if (route == key)
                    return MockHttpMessageHandler.Json(HttpStatusCode.OK, json);
            }
            return MockHttpMessageHandler.Json(HttpStatusCode.NotFound, """{"title":"Not found","status":404}""");
        });

    private static (string, string) ByName =>
        ("GET /api/containers/by-name/notes", $$"""{"id":"{{ContainerId}}","name":"notes"}""");

    [Fact]
    public async Task FilesDelete_EditorSignIn_FallsBackToTheRestDelete()
    {
        McpForbidden(ByName, ($"DELETE /api/containers/{ContainerId}/files/{FileId}", "{}"));

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Deleted file {FileId}.", result.Output);
        Assert.Equal(["POST /mcp", "GET /api/containers/by-name/notes", $"DELETE /api/containers/{ContainerId}/files/{FileId}"], _host.Sent);
    }

    [Fact]
    public async Task FilesDelete_EditorSignIn_MissingFileIsAUserError()
    {
        McpForbidden(ByName);

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"File '{FileId}' not found in container 'notes'.", result.Error);
    }

    [Fact]
    public async Task ContainerDelete_EditorSignIn_FallsBackToTheRestDelete()
    {
        McpForbidden(ByName, ($"DELETE /api/containers/{ContainerId}", "{}"));

        var result = await _host.RunAsync("container", "delete", "notes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Deleted container 'notes'.", result.Output);
        Assert.Contains($"DELETE /api/containers/{ContainerId}", _host.Sent);
    }

    [Fact]
    public async Task ContainerDelete_EditorSignIn_UnknownContainerIsAUserError()
    {
        McpForbidden();

        var result = await _host.RunAsync("container", "delete", "notes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Container 'notes' not found.", result.Error);
    }

    [Fact]
    public async Task MemoryForget_EditorSignIn_FallsBackToTheRestDelete()
    {
        McpForbidden(($"DELETE /api/memory/facts/{FactId}", """{"deleted":2}"""));

        var result = await _host.RunAsync("memory", "forget", FactId, "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(2, doc.RootElement.GetProperty("deleted").GetInt32());
        Assert.Contains($"DELETE /api/memory/facts/{FactId}", _host.Sent);
    }

    [Fact]
    public async Task MemoryForget_SaysHowManyVersionsWent()
    {
        _mcp.ToolText = (_, _) => $"Forgot fact '{FactId}' (2 version(s) deleted).";

        var result = await _host.RunAsync("memory", "forget", FactId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Forgot fact {FactId} (2 version(s) deleted).", result.Output);
        var (tool, args) = Assert.Single(_mcp.Calls);
        Assert.Equal("memory_forget", tool);
        Assert.Equal(FactId, args["factId"]!.GetValue<string>());
    }

    [Fact]
    public async Task MemoryForget_Json_KeepsTheDeletedCount()
    {
        _mcp.ToolText = (_, _) => $"Forgot fact '{FactId}' (1 version(s) deleted).";

        var result = await _host.RunAsync("memory", "forget", FactId, "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(1, doc.RootElement.GetProperty("deleted").GetInt32());
    }

    [Fact]
    public async Task MemoryForget_Json_Held_PrintsTheRequest()
    {
        _mcp.ToolText = (tool, _) => FakeMcpServer.Held(tool, RequestId);

        var result = await _host.RunAsync("memory", "forget", FactId, "--json");

        Assert.Equal(3, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("pending-approval", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(RequestId, doc.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal("https://server.example/approvals", doc.RootElement.GetProperty("approveAt").GetString());
    }

    private static string Status(string state, string? reason = null) =>
        JsonSerializer.Serialize(new
        {
            id = RequestId,
            toolName = "container_delete",
            state,
            reason,
            createdAt = "2026-11-10T02:15:40Z",
            expiresAt = "2026-11-11T02:15:40Z",
            message = $"Request {RequestId} is {state.ToLowerInvariant()}.",
        });

    [Theory]
    [InlineData("Pending", 3)]
    [InlineData("Approved", 0)]
    [InlineData("Used", 0)]
    [InlineData("Denied", 1)]
    [InlineData("Expired", 1)]
    public async Task ApprovalStatus_ExitCodeFollowsTheDecision(string state, int exit)
    {
        _host.Routes(($"GET /api/approvals/{RequestId}", CommandHost.Json(Status(state))));

        var result = await _host.RunAsync("approval", "status", RequestId.ToString());

        Assert.Equal(exit, result.ExitCode);
        Assert.Contains($"{state}: container_delete", result.Output);
    }

    [Fact]
    public async Task ApprovalStatus_Wait_IsPassedToTheServer_AndDeniedShowsTheReason()
    {
        _host.Routes(($"GET /api/approvals/{RequestId}", CommandHost.Json(Status("Denied", "Keep this one."))));

        var result = await _host.RunAsync("approval", "status", RequestId.ToString(), "--wait", "50");

        Assert.Equal($"GET /api/approvals/{RequestId}?wait=50", _host.Sent.Single());
        Assert.Contains("Reason: Keep this one.", result.Output);
    }

    [Fact]
    public async Task ApprovalStatus_Json_PrintsTheServersAnswer()
    {
        _host.Routes(($"GET /api/approvals/{RequestId}", CommandHost.Json(Status("Pending"))));

        var result = await _host.RunAsync("approval", "status", RequestId.ToString(), "--json");

        Assert.Equal(3, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("Pending", doc.RootElement.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("""{"id":"0aae0f35-ddd6-457c-a031-aae9f8c11db6","toolName":"container_delete","state":"Paused"}""", "could not be read")]
    [InlineData("""{"id":"0aae0f35-ddd6-457c-a031-aae9f8c11db6","toolName":"container_delete","state":2}""", "could not be read")]
    [InlineData("""{"id":"0aae0f35-ddd6-457c-a031-aae9f8c11db6","toolName":"container_delete"}""", "has no state")]
    [InlineData("<html>Bad gateway</html>", "could not be read")]
    public async Task ApprovalStatus_UnreadableStatus_IsAnErrorNotPending(string body, string error)
    {
        _host.Routes(($"GET /api/approvals/{RequestId}", CommandHost.Json(body)));

        var result = await _host.RunAsync("approval", "status", RequestId.ToString());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(error, result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ApprovalStatus_AnotherSignInsRequest_ReadsNotFound()
    {
        _host.Routes();

        var result = await _host.RunAsync("approval", "status", RequestId.ToString());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("only be read with the sign-in that made it", result.Error);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("51")]
    public async Task ApprovalStatus_WaitOutOfRange_IsRefusedBeforeAnyRequest(string wait)
    {
        var result = await _host.RunAsync("approval", "status", RequestId.ToString(), "--wait", wait);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_host.Sent);
    }

    private const string OtherFileId = "7b1c9e2a-0000-4000-8000-0000000000f2";

    [Fact]
    public async Task FilesDelete_SeveralFiles_GoAsOneDeleteFileCallWithFileIds()
    {
        _mcp.ToolText = (_, _) => "Deleted 2 of 2 file(s).";

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId, "--file", OtherFileId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Deleted 2 of 2 file(s).", result.Output);
        var (tool, args) = Assert.Single(_mcp.Calls);
        Assert.Equal("delete_file", tool);
        Assert.Equal("notes", args["containerId"]!.GetValue<string>());
        Assert.Null(args["fileId"]);
        Assert.Equal([FileId, OtherFileId], JsonSerializer.Deserialize<string[]>(args["fileIds"]!.GetValue<string>()));
    }

    [Fact]
    public async Task FilesDelete_SeveralFiles_SomeFailing_ShowsTheFailuresAndExits1()
    {
        _mcp.ToolText = (_, _) => $"Deleted 1 of 2 file(s).\n\nFailures:\n- {OtherFileId}: not found";

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId, OtherFileId);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"- {OtherFileId}: not found", result.Output);
    }

    [Fact]
    public async Task FilesDelete_TheSameFileTwice_GoesOnceAsFileId()
    {
        _mcp.ToolText = (_, _) => $"File 'note.md' (ID: {FileId}) deleted.";

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId, "--file", FileId.ToUpperInvariant());

        Assert.Equal(0, result.ExitCode);
        var (_, args) = Assert.Single(_mcp.Calls);
        Assert.Equal(FileId, args["fileId"]!.GetValue<string>());
        Assert.Null(args["fileIds"]);
    }

    [Fact]
    public async Task FilesDelete_MoreThanTheServerTakes_IsRefusedBeforeAnyRequest()
    {
        var args = new List<string> { "files", "delete", "--container", "notes" };
        for (int i = 0; i <= 100; i++)
            args.AddRange(["--file", Guid.NewGuid().ToString()]);

        var result = await _host.RunAsync([.. args]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("At most 100 files", result.Error);
        Assert.Empty(_host.Sent);
    }

    [Fact]
    public async Task FilesDelete_SeveralFiles_EditorSignIn_DeletesEachThroughRest()
    {
        McpForbidden(ByName,
            ($"DELETE /api/containers/{ContainerId}/files/{FileId}", "{}"),
            ($"DELETE /api/containers/{ContainerId}/files/{OtherFileId}", "{}"));

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId, "--file", OtherFileId);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Deleted file {FileId}.", result.Output);
        Assert.Contains($"Deleted file {OtherFileId}.", result.Output);
    }

    [Fact]
    public async Task FilesDelete_SeveralFiles_EditorSignIn_OneMissing_DeletesTheRestAndExits1()
    {
        McpForbidden(ByName, ($"DELETE /api/containers/{ContainerId}/files/{FileId}", "{}"));

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId, "--file", OtherFileId);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"Deleted file {FileId}.", result.Output);
        Assert.Contains($"File '{OtherFileId}' not found in container 'notes'.", result.Error);
    }

    private static readonly Guid BatchId = Guid.Parse("6c1f8e0a-5b2d-4e3f-9a7b-0c1d2e3f4a5b");

    [Fact]
    public async Task FilesDelete_HeldAsABatch_SaysTheServerDeletesItAndNotToRerun()
    {
        _mcp.ToolText = (tool, _) => FakeMcpServer.HeldBatch(tool, BatchId);

        var result = await _host.RunAsync("files", "delete", "--container", "notes", "--file", FileId, "--file", OtherFileId);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains($"Held for approval (deletion batch {BatchId})", result.Output);
        Assert.Contains("don't run this command again", result.Output);
        Assert.Contains($"connapse approval status {BatchId} --wait 50", result.Output);
        Assert.DoesNotContain("Deleted", result.Output);
    }

    [Fact]
    public async Task MemoryForget_Json_HeldAsABatch_PrintsTheBatch()
    {
        _mcp.ToolText = (tool, _) => FakeMcpServer.HeldBatch(tool, BatchId);

        var result = await _host.RunAsync("memory", "forget", FactId, "--json");

        Assert.Equal(3, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("pending-approval", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(BatchId, doc.RootElement.GetProperty("batchId").GetGuid());
    }

    private static string Batch(string state, params (string State, string? Outcome)[] items) =>
        JsonSerializer.Serialize(new
        {
            id = BatchId,
            state,
            createdAt = "2026-11-12T02:15:40Z",
            expiresAt = "2026-11-13T02:15:40Z",
            items = items.Select((item, i) => new
            {
                id = Guid.NewGuid(),
                position = i,
                kind = "file",
                containerId = ContainerId,
                target = FileId,
                label = $"note{i}.md",
                reason = "Held from the agent's delete_file call.",
                state = item.State,
                outcome = item.Outcome,
            }),
            message = $"Batch {BatchId} is {state}.",
            isYours = false,
            tokenKey = "k",
        });

    [Theory]
    [InlineData("pending", "pending", 3)]
    [InlineData("running", "approved", 3)]
    [InlineData("done", "deleted", 0)]
    [InlineData("done", "skipped", 0)]
    [InlineData("done", "failed", 1)]
    [InlineData("done", "not_approved", 1)]
    [InlineData("denied", "denied", 1)]
    [InlineData("expired", "pending", 1)]
    public async Task ApprovalStatus_ABatchId_ReadsTheBatch(string state, string itemState, int exit)
    {
        _host.Routes(($"GET /api/approvals/deletions/{BatchId}", CommandHost.Json(Batch(state, ("deleted", null), (itemState, null)))));

        var result = await _host.RunAsync("approval", "status", BatchId.ToString(), "--wait", "5");

        Assert.Equal(exit, result.ExitCode);
        Assert.Equal([$"GET /api/approvals/{BatchId}?wait=5", $"GET /api/approvals/deletions/{BatchId}?wait=5"], _host.Sent);
        Assert.Contains($"deletion batch {BatchId}", result.Output);
        Assert.Contains("note1.md", result.Output);
    }

    [Fact]
    public async Task ApprovalStatus_NeitherARequestNorABatch_ReadsNotFound()
    {
        _host.Routes();

        var result = await _host.RunAsync("approval", "status", BatchId.ToString());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"No approval request or deletion batch {BatchId}", result.Error);
    }

    /// <summary>
    /// Connapse 0.4.2 folded get_documents, bulk_upload and bulk_delete into get_document, upload_file and
    /// delete_file (CON-344) and drops the old names after 0.4.x, so the CLI must never call them.
    /// </summary>
    [Fact]
    public void TheCliNeverNamesTheDeprecatedMultiFileTools()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Connapse.Cli.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var sources = Directory.GetFiles(Path.Combine(root.FullName, "src", "Connapse.Cli"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        var named = sources
            .SelectMany(f => new[] { "get_documents", "bulk_upload", "bulk_delete" }
                .Where(name => File.ReadAllText(f).Contains($"\"{name}\"", StringComparison.Ordinal))
                .Select(name => $"{Path.GetFileName(f)}: {name}"))
            .ToList();
        Assert.Empty(named);
    }
}
