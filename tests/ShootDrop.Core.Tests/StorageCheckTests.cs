using ShootDrop.Core.Copying;

namespace ShootDrop.Core.Tests;

public class StorageCheckTests
{
    private const long GB = 1_000_000_000;

    [Fact]
    public void Missing_external_drive_blocks_the_copy()
    {
        var s = StorageCheck.Evaluate("/Volumes/SSD 1TB/SSD Video Projects", 10 * GB, _ => 500 * GB, _ => false);
        Assert.Equal(StorageProblem.DriveMissing, s.Problem);
        Assert.True(s.Blocks);
        Assert.Contains("“SSD 1TB” isn't connected", s.Message);
    }

    [Fact]
    public void Too_little_space_blocks_the_copy()
    {
        var s = StorageCheck.Evaluate("/Volumes/SSD 1TB/SSD Video Projects", 28_600_000_000, _ => 12 * GB, _ => true);
        Assert.Equal(StorageProblem.NotEnoughSpace, s.Problem);
        Assert.True(s.Blocks);
        Assert.Contains("12.0 GB free, this copy needs 28.6 GB", s.Message);
    }

    [Fact]
    public void Enough_space_is_fine_and_free_space_is_read_on_the_drive_root()
    {
        string? asked = null;
        var s = StorageCheck.Evaluate("/Volumes/SSD 1TB/SSD Video Projects", 10 * GB, p => { asked = p; return 200 * GB; }, _ => true);
        Assert.Equal(StorageProblem.None, s.Problem);
        Assert.False(s.Blocks);
        Assert.Equal("/Volumes/SSD 1TB", asked);
    }

    [Fact]
    public void Unknown_free_space_does_not_block()
    {
        var s = StorageCheck.Evaluate("/Users/me/Movies/Projects", GB, _ => null, _ => true);
        Assert.Equal(StorageProblem.Unknown, s.Problem);
        Assert.False(s.Blocks);
    }

    [Theory]
    [InlineData("/Volumes/SSD 1TB/Projects", "/Volumes/SSD 1TB")]
    [InlineData("/media/nadeem/T7/Projects", "/media/nadeem/T7")]
    [InlineData("/run/media/nadeem/T7/Projects", "/run/media/nadeem/T7")]
    [InlineData(@"E:\Projects", @"E:\")]
    [InlineData(@"C:\Users\me\Videos", null)]
    [InlineData("/Users/me/Movies", null)]
    public void External_drive_roots(string path, string? root) => Assert.Equal(root, StorageCheck.ExternalDriveRoot(path));
}

public class WindowSizeSettingTests
{
    [Fact]
    public void Window_size_starts_unset_and_survives_a_save()
    {
        using var tmp = new TempDir();
        var store = new ShootDrop.Core.Settings.SettingsStore(Path.Combine(tmp.Path, "settings.json"));
        var s = store.Load();
        Assert.Equal(0, s.WindowWidth);
        s.WindowWidth = 1280;
        s.WindowHeight = 900;
        store.Save(s);
        var again = store.Load();
        Assert.Equal(1280, again.WindowWidth);
        Assert.Equal(900, again.WindowHeight);
    }
}

public class ServiceDefaultsTests
{
    [Fact]
    public void Starts_at_login_and_opens_the_autopilot_for_cards_by_default()
    {
        var s = new ShootDrop.Core.Settings.AppSettings();
        Assert.True(s.Background.StartAtLogin);
        Assert.True(s.Background.KeepRunningInMenuBar);
        Assert.Equal(ShootDrop.Core.Background.CardArrivalAction.AskOnce, s.Background.WhenCardsPluggedIn);
    }
}
