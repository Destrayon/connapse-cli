using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Connapse.Cli.Services;

/// <summary>What a tool call on the server's MCP route came back with.</summary>
/// <param name="Text">The tool's text.</param>
/// <param name="HeldRequestId">Set when the server held the call for a person's approval instead of running it.</param>
public sealed record McpToolResult(string Text, Guid? HeldRequestId)
{
    public bool IsHeld => HeldRequestId is not null;
}

/// <summary>
/// Calls one tool on the signed-in server's MCP route (<c>/mcp</c>), with the CLI's own sign-in. The CLI deletes
/// through this route because it is where the server holds a destructive call until the owner approves it (CC-10);
/// the REST delete routes never hold. Each call opens a session, calls the tool and closes the session.
/// </summary>
public sealed partial class McpToolClient(ApiClient api)
{
    private const string Path = "/mcp";
    private const string ProtocolVersion = "2025-06-18";
    private const string SessionHeader = "Mcp-Session-Id";
    private const string VersionHeader = "MCP-Protocol-Version";

    public async Task<McpToolResult> CallAsync(string tool, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        var initialize = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "connapse-cli", ["version"] = typeof(McpToolClient).Assembly.GetName().Version?.ToString() ?? "0" },
        };

        string? session;
        string version;
        using (var response = await PostAsync(Request(1, "initialize", initialize), session: null, version: null, ct))
        {
            session = response.Headers.TryGetValues(SessionHeader, out var values) ? values.FirstOrDefault() : null;
            var result = Result(await ReadMessageAsync(response, 1, ct));
            version = result?["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion;
        }

        try
        {
            using (await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, session, version, ct)) { }

            var args = new JsonObject();
            foreach (var (name, value) in arguments)
                args[name] = value;
            using var call = await PostAsync(Request(2, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args }), session, version, ct);
            return Interpret(Result(await ReadMessageAsync(call, 2, ct)));
        }
        finally
        {
            if (session is not null)
                await CloseAsync(session, version);
        }
    }

    /// <summary>
    /// Reads a tools/call result. A held call is a normal result whose text says it is pending approval and names
    /// the request; a refused call is an error result, or text that starts with "Error:" as the Connapse tools write.
    /// </summary>
    internal static McpToolResult Interpret(JsonNode? result)
    {
        string text = string.Join(Environment.NewLine, (result?["content"] as JsonArray ?? [])
            .Where(block => block?["type"]?.GetValue<string>() == "text")
            .Select(block => block!["text"]?.GetValue<string>() ?? ""));

        if (result?["isError"]?.GetValue<bool>() == true || text.StartsWith("Error:", StringComparison.Ordinal))
            throw new CliValidationError(text.StartsWith("Error:", StringComparison.Ordinal) ? text["Error:".Length..].Trim() : text);

        if (text.Contains("pending approval", StringComparison.Ordinal) && HeldRequest().Match(text) is { Success: true } held)
            return new McpToolResult(text, Guid.Parse(held.Groups[1].Value));

        return new McpToolResult(text, null);
    }

    private async Task<HttpResponseMessage> PostAsync(JsonObject message, string? session, string? version, CancellationToken ct)
    {
        using var holder = new HttpRequestMessage();
        holder.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        holder.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (session is not null)
            holder.Headers.TryAddWithoutValidation(SessionHeader, session);
        if (version is not null)
            holder.Headers.TryAddWithoutValidation(VersionHeader, version);

        var content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            return await api.SendRawAsync(HttpMethod.Post, Path, content, ct, holder.Headers);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new CliValidationError(
                "The server's approval route only admits Owner, Admin or agent sign-ins, so this account can't delete from the CLI. "
                + "Delete it in the web UI instead.");
        }
    }

    private async Task CloseAsync(string session, string version)
    {
        try
        {
            using var holder = new HttpRequestMessage();
            holder.Headers.TryAddWithoutValidation(SessionHeader, session);
            holder.Headers.TryAddWithoutValidation(VersionHeader, version);
            using var _ = await api.SendRawAsync(HttpMethod.Delete, Path, null, CancellationToken.None, holder.Headers);
        }
        catch (Exception ex) when (ex is CliUserError or HttpRequestException)
        {
            // The session ends on its own; failing to close it changes nothing about the call.
        }
    }

    /// <summary>The JSON-RPC response with id <paramref name="id"/>, from a JSON body or an event stream.</summary>
    private static async Task<JsonNode> ReadMessageAsync(HttpResponseMessage response, int id, CancellationToken ct)
    {
        string body = await response.Content.ReadAsStringAsync(ct);
        bool stream = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
        var messages = stream ? Events(body) : [body];
        foreach (var json in messages)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (JsonException) { continue; }
            if (node?["id"] is JsonValue value && value.TryGetValue(out int got) && got == id)
                return node;
        }
        throw new CliServerError($"The server's MCP route sent no answer to the CLI's request{(body.Length == 0 ? "" : $": {Shorten(body)}")}");
    }

    /// <summary>The data of each event in a server-sent event stream.</summary>
    internal static List<string> Events(string body)
    {
        var events = new List<string>();
        var data = new StringBuilder();
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0)
            {
                if (data.Length > 0) events.Add(data.ToString());
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line[5..].TrimStart());
            }
        }
        if (data.Length > 0) events.Add(data.ToString());
        return events;
    }

    private static JsonNode? Result(JsonNode message) =>
        message["error"] is { } error
            ? throw new CliServerError($"The server refused the call: {error["message"]?.GetValue<string>() ?? error.ToJsonString()}")
            : message["result"];

    private static JsonObject Request(int id, string method, JsonObject parameters) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "...";

    [GeneratedRegex(@"request ([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex HeldRequest();
}
