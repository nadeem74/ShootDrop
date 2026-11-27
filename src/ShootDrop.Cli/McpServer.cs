using System.Text.Json.Nodes;
using ShootDrop.Core;
using ShootDrop.Core.Agent;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Settings;

/// <summary>
/// <c>shootdrop mcp</c> (Phase 2, ADR-008): a Model Context Protocol server over stdin/stdout, so AI apps such as
/// Claude Desktop can use ShootDrop's tools. MCP is JSON-RPC 2.0, one JSON message per line. Only the handful of
/// methods a tools-only server needs are implemented: initialize, tools/list, tools/call, ping.
/// Logs go to stderr: stdout carries only protocol messages.
/// </summary>
static class McpServer
{
    public static async Task<int> RunAsync(ShootDropService service, AppSettings settings, SettingsStore store)
    {
        var tools = new ShootDropTools(service, settings, StorageCheck.FreeBytes);
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            if (line.Trim().Length == 0) continue;
            JsonObject? msg;
            try { msg = JsonNode.Parse(line) as JsonObject; }
            catch (System.Text.Json.JsonException) { msg = null; }
            if (msg is null) { Write(Error(null, -32700, "Parse error")); continue; }

            var id = msg["id"]?.DeepClone();
            var method = msg["method"]?.GetValue<string>() ?? "";
            if (id is null) continue; // a notification (e.g. notifications/initialized): no answer

            JsonObject reply;
            switch (method)
            {
                case "initialize":
                    reply = Result(id, new JsonObject
                    {
                        ["protocolVersion"] = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "shootdrop", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString() ?? "0" },
                    });
                    break;
                case "ping":
                    reply = Result(id, new JsonObject());
                    break;
                case "tools/list":
                    reply = Result(id, new JsonObject
                    {
                        ["tools"] = new JsonArray(ShootDropTools.Specs.Select(t => (JsonNode)new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["inputSchema"] = t.InputSchema.DeepClone(),
                        }).ToArray()),
                    });
                    break;
                case "tools/call":
                {
                    var name = msg["params"]?["name"]?.GetValue<string>() ?? "";
                    var args = msg["params"]?["arguments"] as JsonObject;
                    string text;
                    var isError = false;
                    try
                    {
                        text = await tools.CallAsync(name, args);
                        if (name == "copy_sessions") store.Save(settings); // remembers what was copied
                    }
                    catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or FormatException)
                    {
                        text = e.Message;
                        isError = true;
                    }
                    reply = Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                        ["isError"] = isError,
                    });
                    break;
                }
                default:
                    reply = Error(id, -32601, $"Method not found: {method}");
                    break;
            }
            Write(reply);
        }
        return 0;
    }

    private static JsonObject Result(JsonNode id, JsonObject result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static void Write(JsonObject message)
    {
        Console.Out.WriteLine(message.ToJsonString());
        Console.Out.Flush();
    }
}
