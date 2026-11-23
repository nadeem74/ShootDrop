using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Tests;

public class CopyTests
{
    private static CameraProfile Profile(string id) => ProfileCatalog.BuiltIn().Single(p => p.Id == id);

    private static CardFiles Files(FakeCard card, string name, CameraProfile? profile, params FileKind[] kinds) =>
        new(new DetectedCard(new VolumeInfo(card.Root, name), profile),
            new CardScanner(FileTypeCatalog.Default).Scan(card.Root, kinds.ToHashSet(), Days.Today, Days.Today));

    [Fact]
    public void Planner_routes_by_profile_and_kind_and_flags_unknown_cards()
    {
        using var tmp = new TempDir();
        var pocket = new FakeCard(tmp, "POCKET");
        pocket.Add("DCIM/DJI_001/DJI_0001.MP4", Days.At(23));
        pocket.Add("DCIM/DJI_001/DJI_0002.JPG", Days.At(23));
        pocket.Add("DCIM/DJI_001/DJI_0001.LRF", Days.At(23));
        var gopro = new FakeCard(tmp, "NO NAME");
        gopro.Add("DCIM/100GOPRO/GX010001.MP4", Days.At(23));

        var plan = CopyPlanner.Build(Path.Combine(tmp.Path, "Projects", "Trip"),
            [
                Files(pocket, "POCKET", Profile("dji-osmo-pocket"), FileKind.Video, FileKind.Photo, FileKind.Proxy),
                Files(gopro, "NO NAME", null, FileKind.Video),
            ],
            Routing.Default());

        Assert.Equal(2, plan.Items.Count); // proxy skipped by default
        Assert.Contains(plan.Items, i => i.Folder == "Video/Pocket 4P" && i.Source.Name == "DJI_0001.MP4");
        Assert.Contains(plan.Items, i => i.Folder == "Images" && i.Source.Name == "DJI_0002.JPG");
        Assert.Single(plan.Unrouted);
        Assert.False(plan.CanRun);
    }

    [Fact]
    public void Pocket_audio_track_goes_to_the_audio_folder_not_with_the_video()
    {
        using var tmp = new TempDir();
        var pocket = new FakeCard(tmp, "DJI");
        pocket.Add("DCIM/DJI_002/DJI_20261123144348_0568_D.MP4", Days.At(23));
        pocket.Add("DCIM/DJI_002/DJI_20261123144348_0568_D.AAC", Days.At(23));

        var plan = CopyPlanner.Build(Path.Combine(tmp.Path, "P"),
            [Files(pocket, "DJI", Profile("dji-osmo-pocket"), FileKind.Video, FileKind.Audio)], Routing.Default());

        Assert.Equal("Video/Pocket 4P", plan.Items.Single(i => i.Source.Name.EndsWith(".MP4")).Folder);
        Assert.Equal("Audio/DJI Mic", plan.Items.Single(i => i.Source.Name.EndsWith(".AAC")).Folder);
    }

    [Fact]
    public void Old_settings_are_upgraded_to_route_audio_separately()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        File.WriteAllText(path, """{ "projectsRoot": "/x", "routing": { "byKind": { "Photo": "Images", "Proxy": "" } } }""");

        var s = new SettingsStore(path).Load();

        Assert.Equal("Audio/DJI Mic", s.Routing.ByKind[FileKind.Audio]);
        Assert.Equal(AppSettings.CurrentVersion, s.Version);
    }

    [Fact]
    public void Same_name_from_two_cards_gets_the_card_name_added()
    {
        using var tmp = new TempDir();
        var a = new FakeCard(tmp, "POCKET A");
        a.Add("DCIM/DJI_0001.MP4", Days.At(23), seed: 1);
        var b = new FakeCard(tmp, "POCKET B");
        b.Add("DCIM/DJI_0001.MP4", Days.At(23), seed: 2);

        var plan = CopyPlanner.Build(Path.Combine(tmp.Path, "P"),
            [Files(a, "POCKET A", Profile("dji-osmo-pocket"), FileKind.Video), Files(b, "POCKET B", Profile("dji-osmo-pocket"), FileKind.Video)],
            Routing.Default());

        var names = plan.Items.Select(i => Path.GetFileName(i.Destination)).ToList();
        Assert.Equal(new[] { "DJI_0001.MP4", "DJI_0001_POCKET-B.MP4" }, names);
    }

    [Fact]
    public async Task Copy_verifies_keeps_the_card_unchanged_and_skips_on_rerun()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "POCKET");
        card.Add("DCIM/DJI_0001.MP4", Days.At(23), bytes: 3 * 1024 * 1024 + 17);
        card.Add("DCIM/DJI_0002.MP4", Days.At(23), bytes: 10);
        var before = card.Fingerprint();

        var plan = CopyPlanner.Build(Path.Combine(tmp.Path, "P"),
            [Files(card, "POCKET", Profile("dji-osmo-pocket"), FileKind.Video)], Routing.Default());
        var engine = new CopyEngine { BufferSize = 64 * 1024 };

        var first = await engine.RunAsync(plan);
        Assert.All(first, r => Assert.Equal(CopyOutcome.Copied, r.Outcome));
        foreach (var r in first)
        {
            Assert.Equal(await CopyEngine.HashFileAsync(r.Item.Source.Path), await CopyEngine.HashFileAsync(r.FinalPath));
            Assert.Equal(r.Item.Source.Modified, File.GetLastWriteTime(r.FinalPath));
        }
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(tmp.Path, "P"), "*.part", SearchOption.AllDirectories));
        Assert.Equal(before, card.Fingerprint());

        var second = await engine.RunAsync(plan);
        Assert.All(second, r => Assert.Equal(CopyOutcome.SkippedIdentical, r.Outcome));
    }

    [Fact]
    public async Task Different_file_with_same_name_is_never_overwritten()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "POCKET");
        card.Add("DCIM/DJI_0001.MP4", Days.At(23), seed: 1);
        var plan = CopyPlanner.Build(Path.Combine(tmp.Path, "P"),
            [Files(card, "POCKET", Profile("dji-osmo-pocket"), FileKind.Video)], Routing.Default());

        var existing = plan.Items[0].Destination;
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "someone else's file");

        var result = (await new CopyEngine().RunAsync(plan)).Single();

        Assert.Equal(CopyOutcome.Copied, result.Outcome);
        Assert.NotEqual(existing, result.FinalPath);
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task Cancelling_leaves_no_half_copied_file()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "POCKET");
        card.Add("DCIM/BIG.MP4", Days.At(23), bytes: 2 * 1024 * 1024);
        var plan = CopyPlanner.Build(Path.Combine(tmp.Path, "P"),
            [Files(card, "POCKET", Profile("dji-osmo-pocket"), FileKind.Video)], Routing.Default());

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.BytesDone > 0) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CopyEngine { BufferSize = 64 * 1024 }.RunAsync(plan, progress, cts.Token));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(tmp.Path, "P"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Service_end_to_end_creates_template_folders_report_and_remembers_setup()
    {
        using var tmp = new TempDir();
        var pocket = new FakeCard(tmp, "POCKET");
        pocket.Add("DCIM/DJI_001/DJI_0001.MP4", Days.At(23));
        var mic = new FakeCard(tmp, "DJI MIC");
        mic.Add("DJI_Audio_001/REC_0001.WAV", Days.At(23));
        mic.Add("DJI_Audio_001/REC_0000.WAV", Days.At(20)); // other day, not copied

        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var service = new ShootDropService(new FolderVolumeProvider(pocket.Root, mic.Root), ProfileCatalog.BuiltIn(), new FixedClock());

        var preview = service.Preview(CopyRequest.FromSetup(Setup.ShootDay, "Sinhagad trip"), settings);
        Assert.True(preview.Plan.CanRun);
        var result = await service.RunAsync(preview, settings);

        var project = Path.Combine(settings.ProjectsRoot, "Sinhagad trip");
        Assert.Equal(2, result.Copied);
        Assert.True(File.Exists(Path.Combine(project, "Video", "Pocket 4P", "DJI_0001.MP4")));
        Assert.True(File.Exists(Path.Combine(project, "Audio", "DJI Mic", "REC_0001.WAV")));
        Assert.False(File.Exists(Path.Combine(project, "Audio", "DJI Mic", "REC_0000.WAV")));
        Assert.True(Directory.Exists(Path.Combine(project, "Video", "iPhone"))); // empty template folder still made
        Assert.True(File.Exists(Path.Combine(project, CopyReport.FileName)));
        Assert.Equal(DateRule.Today, settings.LastUsed?.Date);
    }

    [Fact]
    public void Disks_without_dcim_or_a_known_profile_are_not_listed_as_cards()
    {
        using var tmp = new TempDir();
        var installer = new FakeCard(tmp, "LUMIXApplications");
        installer.Add("LUMIX Tether.app/Contents/Info.plist", Days.At(23));
        var gopro = new FakeCard(tmp, "NO NAME");
        gopro.Add("DCIM/100GOPRO/GX010001.MP4", Days.At(23));
        var mic = new FakeCard(tmp, "DJI MIC");
        mic.Add("DJI_Audio_001/REC_0001.WAV", Days.At(23));

        var service = new ShootDropService(new FolderVolumeProvider(installer.Root, gopro.Root, mic.Root), ProfileCatalog.BuiltIn(), new FixedClock());
        var names = service.DetectCards(new AppSettings()).Select(c => c.Name).Order().ToList();

        Assert.Equal(new[] { "DJI MIC", "NO NAME" }, names);
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var tmp = new TempDir();
        var store = new SettingsStore(Path.Combine(tmp.Path, "settings.json"));
        var s = new AppSettings { ProjectsRoot = "/Volumes/SSD 1TB/SSD Video Projects" };
        s.SaveSetup(new Setup("Pocket vlog", ["dji-osmo-pocket", "dji-mic"], DateRule.LastDays(3), [FileKind.Video, FileKind.Audio]));
        s.Routing.ByCard["NO NAME"] = "Video/B-roll";
        store.Save(s);

        var back = store.Load();
        Assert.Equal(s.ProjectsRoot, back.ProjectsRoot);
        var vlog = back.FindSetup("pocket vlog");
        Assert.NotNull(vlog);
        Assert.Equal(DateRule.LastDays(3), vlog!.Date);
        Assert.Equal(new[] { "dji-osmo-pocket", "dji-mic" }, vlog.CardKeys!);
        Assert.Equal("Video/B-roll", back.Routing.ByCard["NO NAME"]);
        Assert.Equal("", back.Routing.ByKind[FileKind.Proxy]);
    }

    /// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<CopyProgress> onReport) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => onReport(value);
    }
}
