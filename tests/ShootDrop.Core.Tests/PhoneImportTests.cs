using ShootDrop.Core.Autopilot;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Phone;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Tests;

/// <summary>iPhone import over USB: choosing what to bring in, and the staging "card" (Phone/PhoneImport.cs).</summary>
public class PhoneImportTests
{
    private static DateTime T(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Local);

    private static PhoneClip Clip(string name, DateTime at, long size = 1000) =>
        new("iphone", $"{name}|{new DateTimeOffset(at).ToUnixTimeSeconds()}|{size}", name, size, at);

    [Fact]
    public void First_time_only_the_latest_day_then_only_newer_clips()
    {
        var settings = new AppSettings();
        var clips = new[] { Clip("IMG_1.MOV", T(20, 10)), Clip("IMG_2.MOV", T(26, 9)), Clip("IMG_3.MOV", T(26, 18)) };

        Assert.Equal(new[] { "IMG_2.MOV", "IMG_3.MOV" }, PhoneImport.SelectNew(clips, settings).Select(c => c.Name));

        settings.AutoCopiedUpTo[PhoneImport.CardName] = T(26, 9);
        Assert.Equal(new[] { "IMG_3.MOV" }, PhoneImport.SelectNew(clips, settings).Select(c => c.Name));
    }

    [Fact]
    public void Clips_with_the_same_name_are_told_apart_and_staged_ones_are_not_fetched_again()
    {
        var settings = new AppSettings();
        var a = Clip("IMG_1352.MOV", T(26, 9), 7_305_846);
        var b = Clip("IMG_1352.MOV", T(26, 11), 17_678);
        Assert.NotEqual(a.Id, b.Id);

        var staged = new HashSet<string> { "IMG_1352.MOV|7305846" };
        Assert.Equal(new[] { b.Id }, PhoneImport.SelectNew([a, b], settings, staged).Select(c => c.Id));
    }

    [Fact]
    public void Helper_lines_are_read_into_clips()
    {
        var c = PhoneImport.ParseClip("""{"device":"iphone","id":"IMG_1.MOV|1795000000|42","name":"IMG_1.MOV","size":42,"recorded":1795000000}""");
        Assert.NotNull(c);
        Assert.Equal(42, c!.Size);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000).LocalDateTime, c.Recorded);
        Assert.Null(PhoneImport.ParseClip("""{"status":"locked"}"""));
        Assert.Null(PhoneImport.ParseClip("""{"id":"x","path":"/tmp/x","size":1}"""));
        Assert.Null(PhoneImport.ParseClip("not json"));
    }

    [Fact]
    public async Task Staged_iphone_clips_appear_as_a_card_go_to_Video_iPhone_and_staging_is_cleared()
    {
        using var tmp = new TempDir();
        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var root = PhoneImport.StagingRoot(settings, tmp.Path);
        Assert.Equal(Path.Combine(tmp.Path, ".shootdrop-iphone"), root);

        var volumes = new StagingVolumeProvider(() => root);
        Assert.Empty(volumes.List()); // nothing staged yet: no card

        var media = PhoneImport.PrepareCard(root);
        foreach (var (name, at) in new[] { ("IMG_0001.MOV", T(26, 9, 10)), ("IMG_0002.MOV", T(26, 9, 40)), ("IMG_0003.MOV", T(26, 19, 5)) })
        {
            var path = Path.Combine(media, name);
            await File.WriteAllBytesAsync(path, new byte[2048]);
            File.SetLastWriteTime(path, at);
        }

        var service = new ShootDropService(volumes, ProfileCatalog.BuiltIn(), new FixedClock());
        var card = Assert.Single(service.DetectCards(settings));
        Assert.Equal("apple-iphone", card.Profile?.Id);

        var auto = new AutoCopy(service);
        var p = auto.Propose(settings);
        Assert.Equal(new[] { "2026-11-26 0910", "2026-11-26 1905" }, p.Sessions.Select(s => s.DefaultName));

        var result = await service.RunAsync(auto.Preview(p, ["Fort", "Night walk"], new HashSet<int>(), settings), settings);
        Assert.Equal(3, result.Copied);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(settings.ProjectsRoot, "Fort", "Video", "iPhone")).Length);

        PhoneImport.Clear(root);
        Assert.False(Directory.Exists(PhoneImport.CardFolder(root)));
        Assert.Empty(volumes.List());
    }

    [Fact]
    public void Clear_only_deletes_a_folder_marked_as_ShootDrop_staging()
    {
        using var tmp = new TempDir();
        var root = Path.Combine(tmp.Path, "staging");
        Directory.CreateDirectory(Path.Combine(PhoneImport.CardFolder(root), "DCIM"));
        PhoneImport.Clear(root);
        Assert.True(Directory.Exists(PhoneImport.CardFolder(root))); // no marker: not ours, left alone
    }
}
