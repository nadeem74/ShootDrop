using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShootDrop.App.Services;
using ShootDrop.App.ViewModels;
using ShootDrop.App.Views;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Phone;
using ShootDrop.Core.Settings;

namespace ShootDrop.App;

public partial class App : Application
{
    private BackgroundController? _background;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Test mode (SHOOTDROP_TEST_DIR): settings live in that folder and every subfolder of its "cards"
            // folder acts as a plugged-in card. Lets anyone try ShootDrop with fake cards, never real ones.
            var testDir = Environment.GetEnvironmentVariable("SHOOTDROP_TEST_DIR");
            var testing = !string.IsNullOrWhiteSpace(testDir);
            var store = testing ? new SettingsStore(Path.Combine(testDir!, "settings.json")) : SettingsStore.Default();
            var settings = store.Load();
            IVolumeProvider cardsSource = testing ? new TestCardsVolumeProvider(Path.Combine(testDir!, "cards")) : new SystemVolumeProvider();
            // Plus the iPhone staging "card": videos brought in from a connected iPhone show up like a card.
            var settingsDir = Path.GetDirectoryName(store.Path)!;
            IVolumeProvider volumes = new CompositeVolumeProvider(cardsSource,
                new StagingVolumeProvider(() => settings.ProjectsRoot.Length > 0 ? PhoneImport.StagingRoot(settings, settingsDir) : null));
            var service = new ShootDropService(volumes, ProfileCatalog.Load(store.ProfilesDirectory));
            var monitor = new CardMonitor(service, settings, volumes);

            // No desktop.MainWindow: ShootDrop decides itself when the window shows, and keeps running
            // in the menu bar when it is closed (ShutdownMode.OnExplicitShutdown in Program).
            var window = new MainWindow();
            var platform = new DesktopPlatform(() => window);
            var ctx = new ShootDropContext(service, store, settings, platform, monitor);
            var shell = new ShellViewModel(ctx);
            window.DataContext = shell;

            _background = new BackgroundController(this, window, ctx, () => desktop.Shutdown());
            ctx.Background = _background;
            ctx.Phone = new PhoneService(ctx, settingsDir);
            desktop.Exit += (_, _) => { _background.Dispose(); ctx.Phone.Dispose(); };
            if (!testing) ctx.Phone.Start();

            shell.Start();
            _background.Start(Program.StartHidden);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Test mode: each subfolder of <paramref name="folder"/> is a card. Re-read on every scan.</summary>
internal sealed class TestCardsVolumeProvider(string folder) : IVolumeProvider
{
    public IReadOnlyList<VolumeInfo> List() =>
        Directory.Exists(folder)
            ? Directory.GetDirectories(folder).Order(StringComparer.Ordinal).Select(d => new VolumeInfo(d, Path.GetFileName(d))).ToList()
            : [];
}
