using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShootDrop.App.ViewModels;
using ShootDrop.App.Views;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Settings;

namespace ShootDrop.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = SettingsStore.Default();
            var service = new ShootDropService(new SystemVolumeProvider(), ProfileCatalog.Load(store.ProfilesDirectory));
            desktop.MainWindow = new ReadyWindow
            {
                DataContext = new ReadyViewModel(service, store),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
