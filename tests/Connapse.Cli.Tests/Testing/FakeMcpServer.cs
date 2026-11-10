using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Connapse.Cli.Tests.Testing;

/// <summary>
/// Answers the server's MCP route (<c>/mcp</c>) the way the Streamable HTTP transport does: initialize opens a
/// session, the initialized notification is accepted, and tools/call answers with <see cref="ToolText"/>. Every
/// tool call is kept, with its arguments. Other routes go to <see cref="Other"/>.
/// </summary>
public sealed class FakeMcpServer
{
    public const string SessionId = "session-1";

    /// <summary>The text the tool answers with, from its name and arguments.</summary>
    public Func<string, JsonObject, string> ToolText { get; set; } = (_, _) => "";

    /// <summary>Answer as an event stream instead of a JSON body.</summary>
    public bool EventStream { get; set; }

    public Func<HttpRequestMessage, HttpResponseMessage>? Other { get; set; }

    public List<(string Tool, JsonObject Arguments)> Calls { get; } = [];

    public List<string> SessionHeaders { get; } = [];

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath != "/mcp")
            return Other?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);

        if (request.Headers.TryGetValues("Mcp-Session-Id", out var session))
            SessionHeaders.Add(session.Single());

        if (request.Method == HttpMethod.Delete)
            return new HttpResponseMessage(HttpStatusCode.OK);

        var message = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
        string method = message["method"]!.GetValue<string>();
        int? id = message["id"]?.GetValue<int>();

        switch (method)
        {
            case "initialize":
                var init = Reply(id!.Value, new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18",
                    ["capabilities"] = new JsonObject(),
                    ["serverInfo"] = new JsonObject { ["name"] = "Connapse", ["version"] = "1" },
                });
                init.Headers.Add("Mcp-Session-Id", SessionId);
                return init;

            case "notifications/initialized":
                return new HttpResponseMessage(HttpStatusCode.Accepted);

            case "tools/call":
                string tool = message["params"]!["name"]!.GetValue<string>();
                var args = message["params"]!["arguments"]!.AsObject();
                Calls.Add((tool, (JsonObject)args.DeepClone()));
                return Reply(id!.Value, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ToolText(tool, args) }),
                    ["isError"] = false,
                });

            default:
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>The text the server's approval gate answers with when it holds a call.</summary>
    public static string Held(string tool, Guid requestId) =>
        $"'{tool}' is pending approval: it changes or removes data, so a person must approve it first. "
        + $"Ask the person to approve request {requestId} at /approvals. Call approval_status with requestId {requestId} "
        + "(waitSeconds up to 50 waits for the decision) to learn whether they approved; once approved, repeat this exact "
        + "call with the same arguments. The request expires at 2026-11-10 12:00:00Z.";

    private HttpResponseMessage Reply(int id, JsonObject result)
    {
        string json = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
        return EventStream
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"event: message\ndata: {json}\n\n", Encoding.UTF8, "text/event-stream"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
