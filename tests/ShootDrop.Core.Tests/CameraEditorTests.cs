using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Tests;

public class CameraEditorTests
{
    [Fact]
    public void A_gopro_card_suggests_its_dcim_tag_and_file_prefix()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "NO NAME");
        card.Add("DCIM/100GOPRO/GX010001.MP4", Days.At(23));
        card.Add("DCIM/100GOPRO/GX010002.MP4", Days.At(23));
        card.Add("DCIM/100GOPRO/GOPR0003.JPG", Days.At(23));

        var m = ProfileSuggester.Suggest(card.Root, "NO NAME");

        Assert.Equal(new[] { "GOPRO" }, m.DcimFolderContains);
        Assert.Equal(new[] { "GX" }, m.FilePrefixes);
        Assert.Empty(m.VolumeNameContains); // "NO NAME" says nothing about the camera
        Assert.False(m.AudioOnlyWithoutDcim);
    }

    [Fact]
    public void A_sony_card_suggests_its_marker_folder_and_volume_name()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "SONY ZV-E10");
        card.AddFolder("PRIVATE/M4ROOT/CLIP");
        card.Add("DCIM/100MSDCF/DSC00001.JPG", Days.At(23));

        var m = ProfileSuggester.Suggest(card.Root, "SONY ZV-E10");

        Assert.Contains("PRIVATE/M4ROOT", m.MarkerPaths);
        Assert.Contains("MSDCF", m.DcimFolderContains);
        Assert.Equal(new[] { "sony zv-e10" }, m.VolumeNameContains);
    }

    [Fact]
    public void An_audio_recorder_without_dcim_is_suggested_as_audio_only()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "ZOOM H1N");
        card.Add("STEREO/FOLDER01/ZOOM0001.WAV", Days.At(23));

        var m = ProfileSuggester.Suggest(card.Root, "ZOOM H1N");

        Assert.True(m.AudioOnlyWithoutDcim);
        Assert.Equal(new[] { "ZOOM" }, m.FilePrefixes);
    }

    [Fact]
    public void Suggesting_never_changes_the_card()
    {
        using var tmp = new TempDir();
        var card = new FakeCard(tmp, "CARD");
        card.Add("DCIM/100GOPRO/GX010001.MP4", Days.At(23));
        var before = card.Fingerprint();

        ProfileSuggester.Suggest(card.Root, "CARD");

        Assert.Equal(before, card.Fingerprint());
    }

    [Fact]
    public void A_camera_added_in_the_app_is_saved_loaded_and_deleted()
    {
        using var tmp = new TempDir();
        var dir = Path.Combine(tmp.Path, "profiles");
        var id = ProfileCatalog.NewId("GoPro HERO 12!", ProfileCatalog.Load(dir).Select(p => p.Id));
        Assert.Equal("gopro-hero-12", id);

        ProfileCatalog.Save(dir, new CameraProfile
        {
            Id = id, Name = "GoPro HERO 12", DefaultFolder = "Video/GoPro",
            Match = new ProfileMatch { DcimFolderContains = ["GOPRO"] },
        });
        var loaded = ProfileCatalog.Load(dir).Single(p => p.Id == id);
        Assert.Equal("Video/GoPro", loaded.DefaultFolder);
        Assert.Equal(new[] { "GOPRO" }, loaded.Match.DcimFolderContains);

        ProfileCatalog.Delete(dir, id);
        Assert.DoesNotContain(ProfileCatalog.Load(dir), p => p.Id == id);
    }

    [Fact]
    public void Editing_a_built_in_camera_overrides_it_and_deleting_the_edit_restores_it()
    {
        using var tmp = new TempDir();
        var dir = Path.Combine(tmp.Path, "profiles");
        var pocket = ProfileCatalog.BuiltIn().Single(p => p.Id == "dji-osmo-pocket");

        ProfileCatalog.Save(dir, pocket with { DefaultFolder = "Video/A-roll" });
        Assert.Equal("Video/A-roll", ProfileCatalog.Load(dir).Single(p => p.Id == pocket.Id).DefaultFolder);

        ProfileCatalog.Delete(dir, pocket.Id);
        Assert.Equal(pocket.DefaultFolder, ProfileCatalog.Load(dir).Single(p => p.Id == pocket.Id).DefaultFolder);
        Assert.True(ProfileCatalog.IsBuiltIn(pocket.Id));
    }

    [Fact]
    public void Removing_and_re_adding_file_types()
    {
        var s = new AppSettings();
        s.RemoveExtension("MTS");                 // built-in
        s.AddExtension(".braw", FileKind.Video);  // new
        var types = s.BuildFileTypes();
        Assert.Null(types.KindOf("clip.MTS"));
        Assert.Equal(FileKind.Video, types.KindOf("clip.BRAW"));

        s.RemoveExtension("braw");
        s.AddExtension("mts", FileKind.Video);    // brings the built-in back
        types = s.BuildFileTypes();
        Assert.Null(types.KindOf("clip.braw"));
        Assert.Equal(FileKind.Video, types.KindOf("clip.mts"));
        Assert.Empty(s.RemovedExtensions);
        Assert.Empty(s.ExtraExtensions);
    }
}
