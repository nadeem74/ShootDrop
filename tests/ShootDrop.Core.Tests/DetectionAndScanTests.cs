using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;

namespace ShootDrop.Core.Tests;

public class DetectionAndScanTests
{
    private static readonly IReadOnlyList<CameraProfile> Profiles = ProfileCatalog.BuiltIn();

    private static DetectedCard Detect(FakeCard card, string name) =>
        new ProfileDetector(Profiles, FileTypeCatalog.Default).Detect(new VolumeInfo(card.Root, name));

    [Fact]
    public void Built_in_profiles_are_embedded()
    {
        Assert.Contains(Profiles, p => p.Id == "dji-osmo-pocket");
        Assert.Contains(Profiles, p => p.Id == "panasonic-lumix");
        Assert.Contains(Profiles, p => p.Id == "dji-mic");
    }

    [Fact]
    public void Dji_file_names_mean_pocket()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "Untitled");
        card.Add("DCIM/DJI_001/DJI_20261123101500_0001_D.MP4", Days.At(23));
        Assert.Equal("dji-osmo-pocket", Detect(card, "Untitled").Profile?.Id);
    }

    [Fact]
    public void Panasonic_marker_folder_means_lumix()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "NO NAME");
        card.AddFolder("PRIVATE/PANASONIC");
        card.Add("DCIM/100_PANA/P1020001.MOV", Days.At(23));
        Assert.Equal("panasonic-lumix", Detect(card, "NO NAME").Profile?.Id);
    }

    [Fact]
    public void Audio_without_dcim_means_mic_even_if_named_dji()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "DJI MIC");
        card.Add("DJI_Audio_001/DJI_01_20261123_101500.WAV", Days.At(23));
        Assert.Equal("dji-mic", Detect(card, "DJI MIC").Profile?.Id);
    }

    [Fact]
    public void Unrecognised_card_is_unknown()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "NO NAME");
        card.Add("DCIM/100GOPRO/GX010001.MP4", Days.At(22));
        Assert.Null(Detect(card, "NO NAME").Profile);
    }

    [Fact]
    public void Scan_keeps_only_chosen_dates_and_types_and_skips_junk()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "POCKET");
        card.Add("DCIM/DJI_001/A.MP4", Days.At(23));
        card.Add("DCIM/DJI_001/A.LRF", Days.At(23));
        card.Add("DCIM/DJI_001/B.MP4", Days.At(22));
        card.Add("DCIM/DJI_001/C.JPG", Days.At(23));
        card.Add("DCIM/DJI_001/._A.MP4", Days.At(23));
        card.Add("DCIM/DJI_001/notes.txt", Days.At(23));

        var scanner = new CardScanner(FileTypeCatalog.Default);
        var today = scanner.Scan(card.Root, new HashSet<FileKind> { FileKind.Video, FileKind.Audio }, Days.Today, Days.Today);
        Assert.Equal(new[] { "A.MP4" }, today.Select(f => f.Name));

        var both = scanner.Scan(card.Root, new HashSet<FileKind> { FileKind.Video, FileKind.Photo }, Days.Today.AddDays(-1), Days.Today);
        Assert.Equal(new[] { "A.MP4", "B.MP4", "C.JPG" }, both.Select(f => f.Name).Order());
    }

    [Fact]
    public void Clips_just_before_and_after_midnight_land_on_their_own_day()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "POCKET");
        card.Add("DCIM/late.MP4", new DateTime(2026, 11, 22, 23, 59, 0, DateTimeKind.Local));
        card.Add("DCIM/early.MP4", new DateTime(2026, 11, 23, 0, 1, 0, DateTimeKind.Local));

        var files = new CardScanner(FileTypeCatalog.Default)
            .Scan(card.Root, new HashSet<FileKind> { FileKind.Video }, Days.Today, Days.Today);
        Assert.Equal(new[] { "early.MP4" }, files.Select(f => f.Name));
    }
}
