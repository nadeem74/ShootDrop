using System.Text.Json.Nodes;
using ShootDrop.Core.Agent;
using ShootDrop.Core.Autopilot;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Tests;

/// <summary>FR-26 autopilot and FR-27 agent naming (ADR-008).</summary>
public class AutopilotTests
{
    private static DateTime T(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Local);

    private static (ShootDropService Service, AppSettings Settings, FakeCard Pocket) Arrange(TempDir tmp)
    {
        var pocket = new FakeCard(tmp, "POCKET");
        // An older day, then a shoot day: morning, afternoon, evening (gaps of more than an hour).
        pocket.Add("DCIM/DJI_001/DJI_0001.MP4", T(21, 10, 0));
        pocket.Add("DCIM/DJI_001/DJI_0002.MP4", T(23, 9, 15));
        pocket.Add("DCIM/DJI_001/DJI_0003.MP4", T(23, 9, 50));
        pocket.Add("DCIM/DJI_001/DJI_0004.MP4", T(23, 14, 5));
        pocket.Add("DCIM/DJI_001/DJI_0005.MP4", T(23, 18, 30));
        pocket.Add("DCIM/DJI_001/DJI_0006.MP4", T(23, 19, 10));
        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var service = new ShootDropService(new FolderVolumeProvider(pocket.Root), ProfileCatalog.BuiltIn(), new FixedClock());
        return (service, settings, pocket);
    }

    [Fact]
    public void A_new_card_offers_its_latest_day_as_sessions_named_by_date_and_time()
    {
        using var tmp = new TempDir();
        var (service, settings, _) = Arrange(tmp);

        var p = new AutoCopy(service).Propose(settings);

        Assert.Equal(new[] { "2026-11-23 0915", "2026-11-23 1405", "2026-11-23 1830" }, p.Sessions.Select(s => s.DefaultName));
        Assert.Equal(new[] { 2, 1, 2 }, p.Sessions.Select(s => s.ClipCount));
        Assert.DoesNotContain(p.Sessions, s => s.Day == new DateOnly(2026, 11, 21)); // older day not offered
    }

    [Fact]
    public async Task After_a_copy_the_same_card_offers_only_newer_clips_and_the_card_is_untouched()
    {
        using var tmp = new TempDir();
        var (service, settings, pocket) = Arrange(tmp);
        var before = pocket.Fingerprint();
        var auto = new AutoCopy(service);
        var p = auto.Propose(settings);

        // Keep sessions 1 and 3 (renamed), skip session 2.
        var preview = auto.Preview(p, ["Sinhagad Fort", null, "Cafe review"], new HashSet<int> { 1 }, settings);
        var result = await service.RunAsync(preview, settings);
        AutoCopy.Remember(settings, result.Files);

        Assert.Equal(4, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(settings.ProjectsRoot, "Sinhagad Fort"), "*.MP4", SearchOption.AllDirectories).Length);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(settings.ProjectsRoot, "Cafe review"), "*.MP4", SearchOption.AllDirectories).Length);
        Assert.False(Directory.Exists(Path.Combine(settings.ProjectsRoot, "2026-11-23 1405")));
        Assert.Equal(before, pocket.Fingerprint());

        Assert.False(auto.Propose(settings).HasWork); // inserted again: nothing new

        pocket.Add("DCIM/DJI_001/DJI_0007.MP4", T(24, 8, 0));
        var next = auto.Propose(settings);
        Assert.Equal(new[] { "2026-11-24 0800" }, next.Sessions.Select(s => s.DefaultName));
    }

    [Fact]
    public void A_backup_drive_with_some_audio_is_not_a_dji_mic_but_a_mic_card_still_is()
    {
        using var tmp = new TempDir();
        var drive = new FakeCard(tmp, "HDD 2TB");
        drive.Add("Projects/Fort/Video/A.MP4", T(20, 10, 0));
        drive.Add("Projects/Fort/Video/B.MOV", T(20, 10, 5));
        drive.Add("Projects/Fort/Audio/C.WAV", T(20, 10, 6));
        drive.Add("Projects/Fort/notes.txt", T(20, 10, 7));
        var mic = new FakeCard(tmp, "DJI MIC");
        mic.Add("DJI_Audio_001/DJI_01.WAV", T(23, 9, 0));
        mic.Add("DJI_Audio_001/DJI_02.WAV", T(23, 9, 30));

        var detector = new ProfileDetector(ProfileCatalog.BuiltIn(), FileTypeCatalog.Default);
        Assert.Null(detector.Detect(new VolumeInfo(drive.Root, "HDD 2TB")).Profile);
        Assert.Equal("dji-mic", detector.Detect(new VolumeInfo(mic.Root, "DJI MIC")).Profile?.Id);
    }

    private static NamingFacts Facts(TempDir tmp, string note, params string[] pastProjects)
    {
        var (service, settings, _) = Arrange(tmp);
        foreach (var p in pastProjects) Directory.CreateDirectory(Path.Combine(settings.ProjectsRoot, p));
        return NamingFacts.For(new AutoCopy(service).Propose(settings), note, settings.ProjectsRoot);
    }

    [Theory]
    [InlineData("morning Sinhagad Fort, afternoon Lunch, evening Cafe review", "Sinhagad Fort|Lunch|Cafe review")]
    [InlineData("Sinhagad Fort then Lunch aur phir Cafe review", "Sinhagad Fort|Lunch|Cafe review")]
    [InlineData("subah Sinhagad Fort; dopahar Lunch; shaam Cafe review", "Sinhagad Fort|Lunch|Cafe review")]
    [InlineData("Sinhagad trip", "Sinhagad trip|Sinhagad trip|Sinhagad trip")]
    public void Rules_name_sessions_from_the_note(string note, string expected)
    {
        using var tmp = new TempDir();
        var result = SessionNamer.FromNote(Facts(tmp, note));
        Assert.Equal(expected.Split('|'), Enumerable.Range(0, 3).Select(i => result.NameFor(i)));
        Assert.All(result.Names, n => Assert.StartsWith("your note", n.Source));
    }

    [Fact]
    public void Rules_keep_date_names_when_the_note_doesnt_match_the_sessions()
    {
        using var tmp = new TempDir();
        var result = SessionNamer.FromNote(Facts(tmp, "Fort, Cafe"));
        Assert.Empty(result.Names);
        Assert.Contains(result.Trace, t => t.Contains("can't match", StringComparison.Ordinal));
    }

    /// <summary>A pretend model that plays back fixed turns, so the agent loop can be tested without a network.</summary>
    private sealed class ScriptedModel(params ModelTurn[] turns) : IModelClient
    {
        private int _next;
        public List<string> ToolResultsSeen { get; } = [];

        public Task<ModelTurn> NextAsync(string system, JsonArray messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct)
        {
            if (messages[^1]?["content"] is JsonArray blocks)
                ToolResultsSeen.AddRange(blocks.Select(b => b?["content"]?.GetValue<string>() ?? ""));
            return Task.FromResult(turns[Math.Min(_next++, turns.Length - 1)]);
        }
    }

    private static ToolCall Call(string name, string json = "{}") => new("id-" + name, name, (JsonObject)JsonNode.Parse(json)!);

    [Fact]
    public async Task The_agent_uses_tools_and_made_up_names_are_refused()
    {
        using var tmp = new TempDir();
        var facts = Facts(tmp, "Sinhagad Fort in the morning, cafe later", "Cafe review");
        var model = new ScriptedModel(
            new ModelTurn("Let me look.", [Call("get_sessions"), Call("get_note"), Call("get_past_projects")]),
            new ModelTurn("", [Call("submit_names", """
                {"names":[{"session":0,"name":"Sinhagad Fort","source":"note"},
                          {"session":1,"name":"Golden hour magic","source":"vibes"},
                          {"session":2,"name":"Cafe review","source":"past project"}]}
                """)]));

        var result = await new NamingAgent(model).RunAsync(facts);

        Assert.Contains(model.ToolResultsSeen, r => r.Contains("2026-11-23 0915", StringComparison.Ordinal));
        Assert.Contains(model.ToolResultsSeen, r => r.Contains("Cafe review", StringComparison.Ordinal));
        Assert.Equal("Sinhagad Fort", result.NameFor(0));
        Assert.Null(result.NameFor(1));                 // invented: refused, keeps its date name
        Assert.Equal("Cafe review", result.NameFor(2));
        Assert.Contains(result.Trace, t => t.Contains("refused “Golden hour magic”", StringComparison.Ordinal));
        Assert.Contains("existing project", result.Names.Single(n => n.Session == 2).Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_that_never_answers_changes_nothing()
    {
        using var tmp = new TempDir();
        var model = new ScriptedModel(new ModelTurn("", [Call("get_sessions")]));
        var result = await new NamingAgent(model, maxTurns: 3).RunAsync(Facts(tmp, "Fort"));
        Assert.Empty(result.Names);
    }

    [Fact]
    public void Claude_responses_are_read_into_text_and_tool_calls()
    {
        var turn = AnthropicModel.Parse("""
            {"content":[{"type":"text","text":"Checking."},
                        {"type":"tool_use","id":"t1","name":"get_note","input":{}}],"stop_reason":"tool_use"}
            """);
        Assert.Equal("Checking.", turn.Text);
        Assert.Equal("get_note", Assert.Single(turn.ToolCalls).Name);
    }

    [Fact]
    public async Task Mcp_tools_find_sessions_and_copy_them()
    {
        using var tmp = new TempDir();
        var (service, settings, _) = Arrange(tmp);
        var tools = new ShootDropTools(service, settings, _ => 1L << 40);

        Assert.Contains("2026-11-23 1830", await tools.CallAsync("find_new_sessions", null), StringComparison.Ordinal);
        var copied = await tools.CallAsync("copy_sessions", (JsonObject)JsonNode.Parse("""{"names":["Fort"],"skip":[1,2]}""")!);
        Assert.StartsWith("Copied 2,", copied, StringComparison.Ordinal);
        var after = await tools.CallAsync("find_new_sessions", null);   // the skipped later sessions are still new
        Assert.DoesNotContain("2026-11-23 0915", after, StringComparison.Ordinal);
        Assert.Contains("2026-11-23 1405", after, StringComparison.Ordinal);
    }
}
