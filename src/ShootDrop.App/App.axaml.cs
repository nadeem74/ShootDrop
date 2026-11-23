using Avalonia;
using Avalonia.Controls;
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
    private ShellViewModel? _shell;
    private MainWindow? _window;

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

            _window = new MainWindow();
            var platform = new DesktopPlatform(() => _window);
            var ctx = new ShootDropContext(service, store, settings, platform, monitor);
            _shell = new ShellViewModel(ctx);
            _window.DataContext = _shell;
            desktop.MainWindow = _window;
            _shell.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnTrayReady(object? sender, EventArgs e)
    {
        _shell?.GoReady();
        ShowWindow();
    }

    private void OnTrayMain(object? sender, EventArgs e)
    {
        _shell?.GoMain();
        ShowWindow();
    }

    private void OnTraySettings(object? sender, EventArgs e)
    {
        _shell?.GoSettings();
        ShowWindow();
    }

    private void OnTrayQuit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
