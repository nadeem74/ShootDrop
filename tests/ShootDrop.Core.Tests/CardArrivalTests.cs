using ShootDrop.Core.Background;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Tests;

public class CardArrivalTests
{
    private static CameraProfile Pocket => ProfileCatalog.BuiltIn().Single(p => p.Id == "dji-osmo-pocket");

    private static DetectedCard Card(string path, CameraProfile? profile) =>
        new(new VolumeInfo(path, Path.GetFileName(path)), profile);

    private static CopyPlan PlanFor(TempDir tmp, FakeCard card, DetectedCard detected) =>
        CopyPlanner.Build(Path.Combine(tmp.Path, "Projects", "Today"),
            [new CardFiles(detected, new CardScanner(FileTypeCatalog.Default)
                .Scan(card.Root, new HashSet<FileKind> { FileKind.Video }, Days.Today, Days.Today))],
            Routing.Default());

    [Fact]
    public void A_newly_plugged_card_opens_the_autopilot_by_default()
    {
        var pocket = Card("/Volumes/POCKET", Pocket);
        var decision = CardArrival.OnCardsChanged([], [pocket], new BackgroundSettings().WhenCardsPluggedIn, copyRunning: false);
        Assert.Equal(ArrivalDecision.ShowAutopilot, decision);
        Assert.Equal(ArrivalDecision.ShowReadyWindow, CardArrival.OnCardsChanged([], [pocket], CardArrivalAction.ShowReadyWindow, false));
    }

    [Fact]
    public void Removing_a_card_or_no_change_does_nothing()
    {
        var pocket = Card("/Volumes/POCKET", Pocket);
        var mic = Card("/Volumes/MIC", null);
        Assert.Equal(ArrivalDecision.None, CardArrival.OnCardsChanged([pocket, mic], [pocket], CardArrivalAction.ShowReadyWindow, false));
        Assert.Equal(ArrivalDecision.None, CardArrival.OnCardsChanged([pocket], [pocket], CardArrivalAction.CopyAutomatically, false));
    }

    [Fact]
    public void Nothing_happens_while_a_copy_is_running_or_when_the_user_chose_do_nothing()
    {
        var pocket = Card("/Volumes/POCKET", Pocket);
        Assert.Equal(ArrivalDecision.None, CardArrival.OnCardsChanged([], [pocket], CardArrivalAction.CopyAutomatically, copyRunning: true));
        Assert.Equal(ArrivalDecision.None, CardArrival.OnCardsChanged([], [pocket], CardArrivalAction.DoNothing, copyRunning: false));
    }

    [Fact]
    public void Auto_copy_starts_only_for_known_cards_with_something_to_copy()
    {
        using var tmp = new TempDir();
        var fake = new FakeCard(tmp, "POCKET");
        fake.Add("DCIM/DJI_001/DJI_0001.MP4", Days.At(23, 10));
        var pocket = Card(fake.Root, Pocket);

        Assert.Equal(ArrivalDecision.StartCopy,
            CardArrival.OnCardsChanged([], [pocket], CardArrivalAction.CopyAutomatically, false));
        Assert.Equal(ArrivalDecision.StartCopy, CardArrival.ConfirmAutoCopy([pocket], PlanFor(tmp, fake, pocket)));
    }

    [Fact]
    public void Auto_copy_falls_back_to_the_ready_window_for_an_unknown_card()
    {
        using var tmp = new TempDir();
        var fake = new FakeCard(tmp, "NO NAME");
        fake.Add("DCIM/100GOPRO/GX010001.MP4", Days.At(23, 10));
        var unknown = Card(fake.Root, null);

        Assert.Equal(ArrivalDecision.ShowReadyWindow, CardArrival.ConfirmAutoCopy([unknown], PlanFor(tmp, fake, unknown)));
    }

    [Fact]
    public void Auto_copy_falls_back_to_the_ready_window_when_nothing_was_recorded_today()
    {
        using var tmp = new TempDir();
        var fake = new FakeCard(tmp, "POCKET");
        fake.Add("DCIM/DJI_001/DJI_0001.MP4", Days.At(18, 10));
        var pocket = Card(fake.Root, Pocket);

        Assert.Equal(ArrivalDecision.ShowReadyWindow, CardArrival.ConfirmAutoCopy([pocket], PlanFor(tmp, fake, pocket)));
    }

    [Fact]
    public void Background_settings_round_trip_and_default_to_the_safe_choice()
    {
        using var tmp = new TempDir();
        var store = new SettingsStore(Path.Combine(tmp.Path, "settings.json"));
        Assert.Equal(CardArrivalAction.AskOnce, store.Load().Background.WhenCardsPluggedIn);

        var s = store.Load();
        s.Background.StartAtLogin = true;
        s.Background.WhenCardsPluggedIn = CardArrivalAction.CopyAutomatically;
        store.Save(s);

        var back = store.Load();
        Assert.True(back.Background.StartAtLogin);
        Assert.True(back.Background.KeepRunningInMenuBar);
        Assert.Equal(CardArrivalAction.CopyAutomatically, back.Background.WhenCardsPluggedIn);
        Assert.Equal(AppSettings.CurrentVersion, back.Version);
    }
}
