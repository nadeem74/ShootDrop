using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ShootDrop.App.ViewModels;
using ShootDrop.App.Views;
using ShootDrop.Core;
using ShootDrop.Core.Background;
using ShootDrop.Core.Cards;
using ShootDrop.Platform.Mac;

namespace ShootDrop.App.Services;

/// <summary>
/// Runs ShootDrop from the menu bar (v0.2, ADR-006): the menu bar menu (FR-21), hiding instead of quitting
/// (FR-24), start at login (FR-24), the ⌃⌥⌘C shortcut (FR-23), and what happens when cards are plugged in.
/// All copy decisions come from Core (<see cref="CardArrival"/>); this class only wires them to the desktop.
/// </summary>
public sealed class BackgroundController : IDisposable
{
    private const string LastUsed = "Last used";

    private readonly Application _app;
    private readonly MainWindow _window;
    private readonly ShootDropContext _ctx;
    private readonly Action _quit;
    private readonly TrayIcon _tray;
    // One menu for the app's lifetime: on macOS, Avalonia can't swap in a new NativeMenu on a tray icon
    // ("The menu being updated does not match"), so Render() replaces the items of this one instead.
    private readonly NativeMenu _menu = new();
    private IReadOnlyList<DetectedCard> _knownCards = [];
    private bool _firstScan = true;
    private bool _quitting;
    private string _menuSetup = LastUsed;
    private CopyPreview? _menuPreview;
    private int _menuVersion;
    private GlobalHotKey? _hotKey;

    public BackgroundController(Application app, MainWindow window, ShootDropContext ctx, Action quit)
    {
        _app = app;
        _window = window;
        _ctx = ctx;
        _quit = quit;

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ShootDrop/Assets/tray.png"))),
            ToolTipText = "ShootDrop",
            Menu = _menu,
        };
        TrayIcon.SetIcons(app, new TrayIcons { _tray });

        _window.Closing += OnWindowClosing;
        _ctx.Monitor.Changed += OnCardsChanged;
        _ctx.Shell.PropertyChanged += OnShellChanged;
    }

    private BackgroundSettings Settings => _ctx.Settings.Background;

    private bool CopyRunning => _ctx.Shell.Current is CopyingPageViewModel;

    /// <summary>Called once at start-up, after the first page is chosen.</summary>
    public void Start(bool startHidden)
    {
        ApplyShortcut();
        RefreshLoginItem();
        BuildMenu();
        if (startHidden && Settings.KeepRunningInMenuBar && _ctx.Settings.ProjectsRoot.Length > 0) HideWindow();
        else ShowWindow();
    }

    // ---- Window: hide instead of quit ----------------------------------------------------------

    public void ShowWindow()
    {
        if (OperatingSystem.IsMacOS()) DockIcon.Show();
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void HideWindow()
    {
        _window.Hide();
        if (OperatingSystem.IsMacOS()) DockIcon.Hide();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        var appQuitting = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (_quitting || appQuitting || !Settings.KeepRunningInMenuBar)
        {
            Quit();
            return;
        }
        e.Cancel = true; // keep running in the menu bar; a running copy carries on
        HideWindow();
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _quit();
    }

    // ---- Cards plugged in ----------------------------------------------------------------------

    private void OnCardsChanged(object? sender, EventArgs e)
    {
        var before = _knownCards;
        var now = _ctx.Monitor.Cards;
        _knownCards = now;
        BuildMenu();

        if (_firstScan)
        {
            // Cards already plugged in when ShootDrop starts are not "new": no pop-up, no automatic copy.
            _firstScan = false;
            return;
        }
        if (_ctx.Settings.ProjectsRoot.Length == 0) return; // first-run setup not done yet

        switch (CardArrival.OnCardsChanged(before, now, Settings.WhenCardsPluggedIn, CopyRunning))
        {
            case ArrivalDecision.ShowReadyWindow:
                OpenReady();
                break;
            case ArrivalDecision.StartCopy:
                _ = AutoCopyAsync();
                break;
        }
    }

    private async Task AutoCopyAsync()
    {
        CopyPreview preview;
        try
        {
            preview = await _ctx.PreviewAsync(CopyRequest.FromSetup(_ctx.Settings.CurrentSetup, _ctx.DefaultProjectName));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException)
        {
            OpenReady();
            return;
        }
        if (CopyRunning) return;
        if (CardArrival.ConfirmAutoCopy(_ctx.Monitor.Cards, preview.Plan) == ArrivalDecision.StartCopy)
        {
            _ctx.Shell.GoCopying(preview);
            ShowWindow();
        }
        else
        {
            OpenReady();
        }
    }

    /// <summary>Show Ready to copy, unless the user is in the middle of something else in the window.</summary>
    public void OpenReady()
    {
        var current = _ctx.Shell.Current;
        var busy = _window.IsVisible && current is SettingsPageViewModel or ReviewPageViewModel or CopyingPageViewModel or WelcomePageViewModel;
        if (!busy) _ctx.Shell.GoReady();
        ShowWindow();
    }

    // ---- Keyboard shortcut ⌃⌥⌘C ----------------------------------------------------------------

    public void ApplyShortcut()
    {
        if (!OperatingSystem.IsMacOS()) return;
        _hotKey?.Dispose();
        _hotKey = null;
        if (!Settings.ShortcutEnabled) return;
        _hotKey = GlobalHotKey.Register(GlobalHotKey.KeyC, GlobalHotKey.Control | GlobalHotKey.Option | GlobalHotKey.Cmd,
            () => Dispatcher.UIThread.Post(OnShortcut));
    }

    /// <summary>True if the shortcut is registered (false if another app already uses ⌃⌥⌘C).</summary>
    public bool ShortcutActive => _hotKey is not null;

    private void OnShortcut()
    {
        if (CopyRunning || _ctx.Shell.Current is WelcomePageViewModel) ShowWindow();
        else OpenReady();
    }

    // ---- Start at login --------------------------------------------------------------------------

    /// <summary>Running as ShootDrop.app (not `dotnet run`): only then is a login item meaningful.</summary>
    public static bool CanStartAtLogin =>
        OperatingSystem.IsMacOS() && Environment.ProcessPath?.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) == true;

    public void SetStartAtLogin(bool on)
    {
        Settings.StartAtLogin = on && CanStartAtLogin;
        _ctx.Save();
        RefreshLoginItem();
        BuildMenu();
    }

    /// <summary>Keeps the login item in step with the setting, and pointing at where the app is now.</summary>
    private void RefreshLoginItem()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            if (Settings.StartAtLogin && CanStartAtLogin) LoginItem.Enable(Environment.ProcessPath!);
            else if (!Settings.StartAtLogin) LoginItem.Disable();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- The menu bar menu (FR-21) ---------------------------------------------------------------

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Current)) BuildMenu();
    }

    public void SettingsChanged()
    {
        _ctx.Monitor.Refresh();
        BuildMenu();
    }

    private Core.Settings.Setup MenuSetup() =>
        _menuSetup == LastUsed ? _ctx.Settings.CurrentSetup : _ctx.Settings.FindSetup(_menuSetup) ?? _ctx.Settings.CurrentSetup;

    /// <summary>Rebuilds the menu now, then fills in "Copy now · size" once the cards are scanned.</summary>
    public void BuildMenu()
    {
        var version = ++_menuVersion;
        _menuPreview = null;
        Render();
        if (_ctx.Monitor.Cards.Count == 0 || CopyRunning || _ctx.Settings.ProjectsRoot.Length == 0) return;
        _ = Task.Run(async () =>
        {
            CopyPreview? preview = null;
            try
            {
                preview = await _ctx.PreviewAsync(CopyRequest.FromSetup(MenuSetup(), _ctx.DefaultProjectName));
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException)
            {
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _menuVersion) return;
                _menuPreview = preview;
                Render();
            });
        });
    }

    private void Render()
    {
        var menu = new List<NativeMenuItemBase>();
        var cards = _ctx.Monitor.Cards;

        menu.Add(Disabled(cards.Count switch
        {
            0 => "No cards plugged in",
            1 => "1 card: " + cards[0].Name,
            _ => $"{cards.Count} cards: " + string.Join(" · ", cards.Select(c => c.Name)),
        }));
        menu.Add(new NativeMenuItemSeparator());

        if (_ctx.Settings.ProjectsRoot.Length == 0)
        {
            menu.Add(Item("Finish setting up ShootDrop…", ShowWindow));
        }
        else if (CopyRunning)
        {
            menu.Add(Item("Copying… show progress", ShowWindow));
        }
        else
        {
            var plan = _menuPreview?.Plan;
            var setup = MenuSetup();
            var copyNow = Item(plan is { CanRun: true }
                    ? $"Copy now · {Format.Bytes(plan.TotalBytes)} → “{_ctx.DefaultProjectName}”"
                    : cards.Count == 0 ? "Copy now" : plan is null ? "Copy now (checking cards…)" : "Copy now (nothing to copy)",
                () => _ = CopyNowAsync());
            copyNow.IsEnabled = plan is { CanRun: true };
            menu.Add(copyNow);
            menu.Add(Disabled($"   {setup.Date.Describe(_ctx.Today)} · {Format.Kinds(setup.Kinds)}"));
            menu.Add(Item("Copy now with a different name…", OpenReady));

            var setups = new NativeMenu();
            foreach (var name in new[] { LastUsed }.Concat(_ctx.Settings.Setups.Select(s => s.Name)))
            {
                var pick = name;
                setups.Add(Radio(name, _menuSetup == name, () => { _menuSetup = pick; BuildMenu(); }));
            }
            menu.Add(new NativeMenuItem("Setup") { Menu = setups });
        }

        menu.Add(new NativeMenuItemSeparator());
        var arrival = new NativeMenu();
        foreach (var (action, label) in new[]
                 {
                     (CardArrivalAction.ShowReadyWindow, "Show Ready to copy"),
                     (CardArrivalAction.CopyAutomatically, "Copy automatically with the last setup"),
                     (CardArrivalAction.DoNothing, "Do nothing"),
                 })
        {
            var a = action;
            arrival.Add(Radio(label, Settings.WhenCardsPluggedIn == a, () =>
            {
                Settings.WhenCardsPluggedIn = a;
                _ctx.Save();
                BuildMenu();
            }));
        }
        menu.Add(new NativeMenuItem("When cards are plugged in") { Menu = arrival });

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Open ShootDrop", OpenReady));
        menu.Add(Item("More options…", () => { if (!CopyRunning) _ctx.Shell.GoMain(); ShowWindow(); }));
        menu.Add(Item("Settings…", () => { if (!CopyRunning) _ctx.Shell.GoSettings("Shortcut"); ShowWindow(); }));
        if (CanStartAtLogin)
        {
            var login = new NativeMenuItem("Start at login")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = Settings.StartAtLogin,
            };
            login.Click += (_, _) => SetStartAtLogin(!Settings.StartAtLogin);
            menu.Add(login);
        }
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit ShootDrop", Quit));

        _menu.Items.Clear();
        foreach (var item in menu) _menu.Items.Add(item);
    }

    private async Task CopyNowAsync()
    {
        if (CopyRunning) return;
        var preview = await _ctx.PreviewAsync(CopyRequest.FromSetup(MenuSetup(), _ctx.DefaultProjectName));
        if (!preview.Plan.CanRun || CopyRunning)
        {
            OpenReady();
            return;
        }
        _ctx.Shell.GoCopying(preview);
        ShowWindow();
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => click();
        return item;
    }

    private static NativeMenuItem Radio(string header, bool on, Action click)
    {
        var item = Item(header, click);
        item.ToggleType = NativeMenuItemToggleType.Radio;
        item.IsChecked = on;
        return item;
    }

    private static NativeMenuItem Disabled(string header) => new(header) { IsEnabled = false };

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS()) _hotKey?.Dispose();
        _tray.Dispose();
    }
}
