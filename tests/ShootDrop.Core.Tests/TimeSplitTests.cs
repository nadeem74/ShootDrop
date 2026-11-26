using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;
using ShootDrop.Core.SmartBox;

namespace ShootDrop.Core.Tests;

/// <summary>Copying one day's footage into several projects by time ("till 1 pm into A, …").</summary>
public class TimeSplitTests
{
    private static readonly IReadOnlyList<KnownCard> Cards =
    [
        new("dji-osmo-pocket", ["pocket", "osmo"]),
        new("panasonic-lumix", ["lumix", "camera"]),
        new("dji-mic", ["mic", "dji mic"]),
    ];

    private static ParsedRequest P(string text) => RuleParser.Parse(text, Days.Today, Cards);

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Local);

    // ---- Parsing ------------------------------------------------------------------------------

    [Fact]
    public void Three_projects_with_till_times_chain_into_each_other()
    {
        var r = P("copy yesterday footage till 1 p.m. in project A, till 6 PM in project B and till 11 pm in the C folder");

        Assert.Equal(DateRule.Yesterday, r.Date);
        Assert.Null(r.ProjectName); // names belong to the parts, not to one main project
        var s = r.Splits!;
        Assert.Equal(3, s.Count);
        Assert.Equal(new ProjectSplit("A", new TimeWindow(null, T(13))), s[0]);
        Assert.Equal(new ProjectSplit("B", new TimeWindow(T(13, 1), T(18))), s[1]);
        Assert.Equal(new ProjectSplit("C", new TimeWindow(T(18, 1), T(23))), s[2]);
    }

    [Fact]
    public void Ranges_and_after()
    {
        var r = P("yesterday till 1 pm into Sinhagad, 1 pm to 6 pm into Cafe review and after 6 pm into Night walk");

        var s = r.Splits!;
        Assert.Equal(new TimeWindow(null, T(13)), s[0].Window);
        Assert.Equal("Sinhagad", s[0].ProjectName);
        Assert.Equal(new TimeWindow(T(13), T(18)), s[1].Window); // explicit start is kept
        Assert.Equal("Cafe review", s[1].ProjectName);
        Assert.Equal(new TimeWindow(T(18), null), s[2].Window);
        Assert.Equal("Night walk", s[2].ProjectName);
    }

    [Theory]
    [InlineData("1-6pm for vlog", 13, 18)]
    [InlineData("between 2 and 5 pm for vlog", 14, 17)]
    [InlineData("from 10:30 am to 1:15 pm for vlog", 10, 13)]
    [InlineData("11-1pm for vlog", 11, 13)]
    [InlineData("09:00-17:30 for vlog", 9, 17)]
    public void Time_ranges(string text, int fromHour, int toHour)
    {
        var w = P(text).Splits!.Single().Window;
        Assert.Equal(fromHour, w.From!.Value.Hour);
        Assert.Equal(toHour, w.To!.Value.Hour);
    }

    [Fact]
    public void Hinglish_tak()
    {
        var r = P("kal ka footage 1 baje tak project A mein, aur 6 baje tak project B");
        Assert.Equal(DateRule.Yesterday, r.Date);
        Assert.Equal(2, r.Splits!.Count);
        Assert.Equal("A", r.Splits[0].ProjectName);
        Assert.Equal("B", r.Splits[1].ProjectName);
        Assert.Equal(T(13), r.Splits[0].Window.To);
        Assert.Equal(T(18), r.Splits[1].Window.To);
    }

    [Fact]
    public void One_window_with_the_main_project_named_before_it()
    {
        var r = P("today for Goa vlog, after 6 pm");
        Assert.Equal(DateRule.Today, r.Date);
        Assert.Equal("Goa vlog", r.ProjectName);
        var split = r.Splits!.Single();
        Assert.Null(split.ProjectName); // uses the main project
        Assert.Equal(new TimeWindow(T(18), null), split.Window);
    }

    [Fact]
    public void Plain_requests_have_no_splits_and_dates_are_not_read_as_times()
    {
        Assert.Null(P("all cards, yesterday for Sinhagad trip").Splits);
        Assert.Null(P("Pocket only, last 3 days").Splits);
        var r = P("21-22 Nov");
        Assert.Null(r.Splits);
        Assert.Equal(DateRule.Between(new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22)), r.Date);
    }

    // ---- Windows ------------------------------------------------------------------------------

    [Fact]
    public void Window_includes_the_whole_last_minute()
    {
        var till1 = new TimeWindow(null, T(13));
        Assert.True(till1.Contains(new TimeOnly(13, 0, 40)));
        Assert.False(till1.Contains(new TimeOnly(13, 1, 0)));
        Assert.Equal("till 1:00 pm", till1.Describe());
        Assert.Equal("1:01 pm – 6:00 pm", new TimeWindow(T(13, 1), T(18)).Describe());
    }

    [Fact]
    public void Break_detection_finds_three_sessions_in_a_day()
    {
        var clips = new[]
        {
            At(22, 9, 10), At(22, 9, 50), At(22, 10, 40), At(22, 11, 30), At(22, 12, 20), At(22, 12, 58), // location 1
            At(22, 14, 30), At(22, 15, 20), At(22, 16, 10), At(22, 17, 0), At(22, 17, 45),                // location 2 (1.5 h drive before it)
            At(22, 20, 0), At(22, 20, 50), At(22, 21, 40), At(22, 22, 20), At(22, 22, 50),                // location 3
        };
        var windows = SessionSplitter.Suggest(clips);
        Assert.Equal(3, windows.Count);
        Assert.Equal(new TimeWindow(T(9, 10), T(12, 58)), windows[0]);
        Assert.Equal(new TimeWindow(T(14, 30), T(17, 45)), windows[1]);
        Assert.Equal(new TimeWindow(T(20), T(22, 50)), windows[2]);
    }

    [Fact]
    public void Break_detection_refuses_more_than_one_day()
    {
        Assert.Empty(SessionSplitter.Suggest([At(21, 10), At(22, 10)]));
        Assert.Empty(SessionSplitter.Suggest([]));
    }

    // ---- Copying ------------------------------------------------------------------------------

    [Fact]
    public async Task Split_copy_puts_each_clip_in_its_project_and_leaves_the_card_untouched()
    {
        using var tmp = new TempDir();
        var pocket = new FakeCard(tmp, "POCKET");
        pocket.Add("DCIM/DJI_001/DJI_0001.MP4", At(22, 9, 30));
        pocket.Add("DCIM/DJI_001/DJI_0002.MP4", At(22, 12, 59));
        pocket.Add("DCIM/DJI_001/DJI_0003.MP4", At(22, 15, 10));
        pocket.Add("DCIM/DJI_001/DJI_0004.MP4", At(22, 21, 0));
        pocket.Add("DCIM/DJI_001/DJI_0005.MP4", At(22, 23, 40)); // after every window
        var mic = new FakeCard(tmp, "DJI MIC");
        mic.Add("DJI_Audio_001/REC_0001.WAV", At(22, 16, 0));
        var before = pocket.Fingerprint();

        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var service = new ShootDropService(new FolderVolumeProvider(pocket.Root, mic.Root), ProfileCatalog.BuiltIn(), new FixedClock());
        var parsed = P("yesterday till 1 pm into A, till 6 pm into B and till 11 pm into C");
        var request = CopyRequest.FromSetup(Setup.ShootDay, "unused") with { Date = parsed.Date!, Splits = parsed.Splits };

        var preview = service.Preview(request, settings);
        Assert.True(preview.Plan.IsSplit);
        Assert.Equal(new[] { "A", "B", "C" }, preview.Plan.AllParts.Select(p => p.Name));
        Assert.Equal(new[] { 2, 2, 1 }, preview.Plan.AllParts.Select(p => p.Items.Count));
        Assert.Equal("DJI_0005.MP4", preview.Plan.UnassignedFiles.Single().Source.Name);

        var result = await service.RunAsync(preview, settings);
        Assert.Equal(5, result.Copied);
        string In(string project, params string[] path) => Path.Combine([settings.ProjectsRoot, project, .. path]);
        Assert.True(File.Exists(In("A", "Video", "Pocket 4P", "DJI_0001.MP4")));
        Assert.True(File.Exists(In("A", "Video", "Pocket 4P", "DJI_0002.MP4")));
        Assert.True(File.Exists(In("B", "Video", "Pocket 4P", "DJI_0003.MP4")));
        Assert.True(File.Exists(In("B", "Audio", "DJI Mic", "REC_0001.WAV")));
        Assert.True(File.Exists(In("C", "Video", "Pocket 4P", "DJI_0004.MP4")));
        Assert.False(File.Exists(In("C", "Video", "Pocket 4P", "DJI_0005.MP4")));

        // Each project gets its own report with only its own files.
        Assert.Equal(3, result.ReportPaths!.Count);
        var reportB = await File.ReadAllTextAsync(In("B", CopyReport.FileName));
        Assert.Contains("DJI_0003.MP4", reportB);
        Assert.DoesNotContain("DJI_0001.MP4", reportB);

        Assert.Equal(before, pocket.Fingerprint()); // card untouched
    }

    [Fact]
    public void Parts_with_the_same_name_share_one_project_and_unnamed_parts_use_the_main_name()
    {
        using var tmp = new TempDir();
        var pocket = new FakeCard(tmp, "POCKET");
        pocket.Add("DCIM/DJI_001/DJI_0001.MP4", At(23, 9));
        pocket.Add("DCIM/DJI_001/DJI_0002.MP4", At(23, 14));
        pocket.Add("DCIM/DJI_001/DJI_0003.MP4", At(23, 20));

        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var service = new ShootDropService(new FolderVolumeProvider(pocket.Root), ProfileCatalog.BuiltIn(), new FixedClock());
        var splits = new[]
        {
            new ProjectSplit("Vlog", new TimeWindow(null, T(12))),
            new ProjectSplit(null, new TimeWindow(T(12, 1), T(18))),
            new ProjectSplit("vlog", new TimeWindow(T(18, 1), null)),
        };
        var request = CopyRequest.FromSetup(Setup.ShootDay, "Cafe") with { Splits = splits };

        var plan = service.Preview(request, settings).Plan;
        Assert.Equal(new[] { "Vlog", "Cafe" }, plan.AllParts.Select(p => p.Name));
        Assert.Equal(new[] { 2, 1 }, plan.AllParts.Select(p => p.Items.Count));
    }

    [Fact]
    public void Suggested_splits_are_named_parts()
    {
        using var tmp = new TempDir();
        var pocket = new FakeCard(tmp, "POCKET");
        pocket.Add("DCIM/DJI_001/DJI_0001.MP4", At(23, 9));
        pocket.Add("DCIM/DJI_001/DJI_0002.MP4", At(23, 9, 40));
        pocket.Add("DCIM/DJI_001/DJI_0003.MP4", At(23, 15));

        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var service = new ShootDropService(new FolderVolumeProvider(pocket.Root), ProfileCatalog.BuiltIn(), new FixedClock());
        var preview = service.Preview(CopyRequest.FromSetup(Setup.ShootDay, "Day"), settings);

        var parts = ShootDropService.SuggestSplits(preview);
        Assert.Equal(new[] { "Part 1", "Part 2" }, parts.Select(p => p.ProjectName));
        Assert.Equal(new TimeWindow(T(9), T(9, 40)), parts[0].Window);
    }
}
