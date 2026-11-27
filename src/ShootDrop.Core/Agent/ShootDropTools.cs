using System.Text.Json.Nodes;
using ShootDrop.Core.Autopilot;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Agent;

/// <summary>
/// ShootDrop as tools for any AI app (Phase 2, ADR-008), offered over MCP by <c>shootdrop mcp</c>.
/// Three read-only tools and one that copies. The copy tool runs the same autopilot as the app, so every
/// ADR-003 rule still holds: cards only read, every file verified, nothing overwritten or deleted.
/// </summary>
public sealed class ShootDropTools(ShootDropService service, AppSettings settings, Func<string, long?> freeBytes)
{
    public static IReadOnlyList<ToolSpec> Specs { get; } =
    [
        new("list_cards", "Camera cards plugged in right now: name and device. Read-only.", Obj()),
        new("find_new_sessions",
            "Recording sessions on the plugged-in cards that haven't been copied yet, oldest first, with clip count, size and the default project name. Read-only.",
            Obj()),
        new("check_storage", "Is the projects drive connected, and is there room for the new sessions? Read-only.", Obj()),
        new("copy_sessions",
            "Copy the new sessions into projects and verify every file by checksum. Cards are only read; nothing is overwritten or deleted. " +
            "Ask the person before calling this. names: one project name per session index (empty = default date name). skip: session indexes not to copy.",
            Obj(("names", new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }),
                ("skip", new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } }))),
    ];

    private static JsonObject Obj(params (string Name, JsonObject Type)[] props)
    {
        var p = new JsonObject();
        foreach (var (name, type) in props) p[name] = type;
        return new JsonObject { ["type"] = "object", ["properties"] = p };
    }

    public async Task<string> CallAsync(string name, JsonObject? input, CancellationToken ct = default)
    {
        var auto = new AutoCopy(service);
        switch (name)
        {
            case "list_cards":
                return new JsonArray(service.DetectCards(settings).Select(c => (JsonNode)new JsonObject
                {
                    ["name"] = c.Name,
                    ["device"] = c.Profile?.Name ?? "unknown device",
                }).ToArray()).ToJsonString();

            case "find_new_sessions":
            {
                var p = auto.Propose(settings);
                if (!p.HasWork) return "No new recordings on the plugged-in cards.";
                return new JsonArray(p.Sessions.Select((s, i) => (JsonNode)new JsonObject
                {
                    ["session"] = i,
                    ["summary"] = AutoCopy.Describe(s),
                    ["defaultName"] = s.DefaultName,
                    ["cards"] = string.Join(", ", s.Session.CardNames),
                }).ToArray()).ToJsonString();
            }

            case "check_storage":
            {
                var p = auto.Propose(settings);
                var status = StorageCheck.Evaluate(settings.ProjectsRoot, p.TotalBytes, freeBytes, Directory.Exists);
                return status.Message.Length > 0 ? status.Message : $"OK: {StorageCheck.Bytes(p.TotalBytes)} needed.";
            }

            case "copy_sessions":
            {
                var p = auto.Propose(settings);
                if (!p.HasWork) return "Nothing new to copy.";
                var status = StorageCheck.Evaluate(settings.ProjectsRoot, p.TotalBytes, freeBytes, Directory.Exists);
                if (status.Blocks) return "Not copied: " + status.Message;
                var names = (input?["names"] as JsonArray)?.Select(n => n?.GetValue<string>()).ToList() ?? [];
                var skip = (input?["skip"] as JsonArray)?.Select(n => n!.GetValue<int>()).ToHashSet() ?? [];
                var preview = auto.Preview(p, names, skip, settings);
                if (!preview.Plan.CanRun) return "Not copied: choose a folder for " + string.Join(", ", preview.Plan.Unrouted.Select(c => c.Name)) + " in the app first.";
                var result = await service.RunAsync(preview, settings, null, ct).ConfigureAwait(false);
                AutoCopy.Remember(settings, result.Files);
                var projects = string.Join(", ", preview.Plan.AllParts.Where(x => x.Items.Count > 0).Select(x => x.Name));
                return $"Copied {result.Copied}, already there {result.Skipped}, failed {result.Failed}. Projects: {projects}.";
            }

            default:
                return $"Unknown tool '{name}'.";
        }
    }
}
