using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShootDrop.Core.Agent;

/// <summary>A tool the model may call: name, one-line description, JSON schema of its input.</summary>
public sealed record ToolSpec(string Name, string Description, JsonObject InputSchema);

/// <summary>One tool call the model asked for.</summary>
public sealed record ToolCall(string Id, string Name, JsonObject Input);

/// <summary>One model turn: any text, and the tool calls it wants (none = it is done).</summary>
public sealed record ModelTurn(string Text, IReadOnlyList<ToolCall> ToolCalls);

/// <summary>
/// The model behind the agent. ADR-008 leaves open where it runs, so the agent talks to this interface:
/// <see cref="AnthropicModel"/> today, a local model later, a scripted fake in tests.
/// </summary>
public interface IModelClient
{
    /// <param name="messages">The conversation so far, in the Anthropic Messages format.</param>
    Task<ModelTurn> NextAsync(string system, JsonArray messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct);
}

/// <summary>
/// The agent loop (FR-27): look → think → call a tool → read the result → repeat → submit names.
/// It gets read-only tools and one tool to hand in its answer. It can't copy, move or delete anything,
/// and every name it hands in still goes through <see cref="Grounding"/>.
/// </summary>
public sealed class NamingAgent(IModelClient model, int maxTurns = 6)
{
    public const string SystemPrompt =
        "You name video-shoot sessions for ShootDrop, an app that copies camera cards into project folders. " +
        "Use the tools to read the sessions, the person's note and their existing projects. " +
        "Every name must be copied exactly from the note or from an existing project name; never invent a name. " +
        "If you can't tell, keep the session's date name. Finish by calling submit_names once.";

    public static IReadOnlyList<ToolSpec> Tools { get; } =
    [
        new("get_sessions", "The recording sessions found on the cards: index, day, start and end time, clip count, cards, date name.", Schema()),
        new("get_note", "What the person wrote about this shoot, e.g. 'morning Sinhagad Fort, evening Cafe review'. May be empty.", Schema()),
        new("get_past_projects", "Names of projects that already exist in the projects folder, newest first.", Schema()),
        new("submit_names", "Hand in one name per session you want to name. Sessions you leave out keep their date name.", Schema(
            ("names", new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["session"] = new JsonObject { ["type"] = "integer", ["description"] = "Session index from get_sessions" },
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["source"] = new JsonObject { ["type"] = "string", ["description"] = "Where the name came from: note or past project" },
                    },
                    ["required"] = new JsonArray("session", "name", "source"),
                },
            }))),
    ];

    private static JsonObject Schema(params (string Name, JsonObject Type)[] props)
    {
        var p = new JsonObject();
        foreach (var (name, type) in props) p[name] = type;
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = p,
            ["required"] = new JsonArray(props.Select(x => (JsonNode)JsonValue.Create(x.Name)!).ToArray()),
        };
    }

    public async Task<NamingResult> RunAsync(NamingFacts facts, CancellationToken ct = default)
    {
        var trace = new List<string>();
        var messages = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Name the sessions of this shoot." });
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var reply = await model.NextAsync(SystemPrompt, messages, Tools, ct).ConfigureAwait(false);
            if (reply.Text.Length > 0) trace.Add("Model: " + reply.Text.Trim());
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = AssistantContent(reply) });
            if (reply.ToolCalls.Count == 0) break;

            var results = new JsonArray();
            foreach (var call in reply.ToolCalls)
            {
                trace.Add($"Tool: {call.Name}");
                if (call.Name == "submit_names")
                    return Grounding.Enforce(ReadNames(call.Input), facts, trace);
                results.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = call.Id,
                    ["content"] = Run(call.Name, facts),
                });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }
        trace.Add("The model didn't hand in names, so every session keeps its date name.");
        return new NamingResult([], trace);
    }

    /// <summary>The read-only tools. Pure functions of the facts: nothing here can change a file.</summary>
    public static string Run(string tool, NamingFacts facts) => tool switch
    {
        "get_sessions" => new JsonArray(facts.Sessions.Select((s, i) => (JsonNode)new JsonObject
        {
            ["session"] = i,
            ["day"] = s.Day.ToString("yyyy-MM-dd"),
            ["start"] = s.First.ToString("HH:mm"),
            ["end"] = s.Last.ToString("HH:mm"),
            ["clips"] = s.ClipCount,
            ["cards"] = string.Join(", ", s.Session.CardNames),
            ["dateName"] = s.DefaultName,
        }).ToArray()).ToJsonString(),
        "get_note" => facts.Note.Length > 0 ? facts.Note : "(no note)",
        "get_past_projects" => new JsonArray(facts.PastProjects.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()).ToJsonString(),
        _ => $"Unknown tool '{tool}'.",
    };

    private static IEnumerable<NameSuggestion> ReadNames(JsonObject input)
    {
        if (input["names"] is not JsonArray names) yield break;
        foreach (var n in names.OfType<JsonObject>())
        {
            if (n["session"] is JsonValue sv && sv.TryGetValue<int>(out var i) && n["name"]?.GetValue<string>() is { } name)
                yield return new NameSuggestion(i, name, n["source"]?.GetValue<string>() ?? "");
        }
    }

    private static JsonArray AssistantContent(ModelTurn reply)
    {
        var content = new JsonArray();
        if (reply.Text.Length > 0) content.Add(new JsonObject { ["type"] = "text", ["text"] = reply.Text });
        foreach (var c in reply.ToolCalls)
            content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = c.Id, ["name"] = c.Name, ["input"] = c.Input.DeepClone() });
        return content;
    }
}

/// <summary>Claude through the Anthropic Messages API, with plain HttpClient (no extra packages).</summary>
public sealed class AnthropicModel(string apiKey, string model, HttpClient? http = null) : IModelClient
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// The key from the ANTHROPIC_API_KEY environment variable, or from a file <c>anthropic-key.txt</c> next to the
    /// settings (apps opened from the Dock don't see shell variables). Null when there is none: the agent is optional.
    /// </summary>
    public static string? FindKey(string settingsDirectory)
    {
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        var file = Path.Combine(settingsDirectory, "anthropic-key.txt");
        try { return File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } k ? k : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task<ModelTurn> NextAsync(string system, JsonArray messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 1024,
            ["system"] = system,
            ["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = t.InputSchema.DeepClone(),
            }).ToArray()),
            ["messages"] = messages.DeepClone(),
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API answered {(int)res.StatusCode}: {Short(json)}");
        return Parse(json);
    }

    /// <summary>Reads a Messages API response into text and tool calls.</summary>
    public static ModelTurn Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Empty answer from Claude.");
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        foreach (var block in (root["content"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            switch (block["type"]?.GetValue<string>())
            {
                case "text":
                    text.Append(block["text"]?.GetValue<string>());
                    break;
                case "tool_use":
                    calls.Add(new ToolCall(
                        block["id"]?.GetValue<string>() ?? "",
                        block["name"]?.GetValue<string>() ?? "",
                        block["input"] as JsonObject ?? new JsonObject()));
                    break;
            }
        }
        return new ModelTurn(text.ToString(), calls);
    }

    private static string Short(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
