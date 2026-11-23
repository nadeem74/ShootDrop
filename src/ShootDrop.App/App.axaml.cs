using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShootDrop.App.Services;
using ShootDrop.App.ViewModels;
using ShootDrop.App.Views;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
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
            var store = SettingsStore.Default();
            var settings = store.Load();
            var volumes = new SystemVolumeProvider();
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
            desktop.Exit += (_, _) => _background.Dispose();

            shell.Start();
            _background.Start(Program.StartHidden);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
