using CommunityToolkit.Mvvm.ComponentModel;
using ShootDrop.App.Services;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Settings;
using ShootDrop.Core.SmartBox;

namespace ShootDrop.App.ViewModels;

/// <summary>Everything the screens share: the core service, settings, card watcher, OS helpers, navigation.</summary>
public sealed class ShootDropContext
{
    public ShootDropContext(ShootDropService service, SettingsStore store, AppSettings settings, IPlatform platform, CardMonitor monitor)
    {
        Service = service;
        Store = store;
        Settings = settings;
        Platform = platform;
        Monitor = monitor;
    }

    public ShootDropService Service { get; }
    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public IPlatform Platform { get; }
    public CardMonitor Monitor { get; }
    public ShellViewModel Shell { get; set; } = null!;

    /// <summary>Menu bar, shortcut and login item; null in design-time previews.</summary>
    public BackgroundController? Background { get; set; }

    public DateOnly Today => Service.Today;

    public void Save() => Store.Save(Settings);

    /// <summary>Scans the cards off the UI thread.</summary>
    public Task<CopyPreview> PreviewAsync(CopyRequest request)
    {
        var cards = Monitor.Cards;
        return Task.Run(() => Service.Preview(request, Settings, cards));
    }

    public string DefaultProjectName => ShootDropService.DefaultProjectName(Today);

    /// <summary>Names the smart box understands for each plugged-in card.</summary>
    public IReadOnlyList<KnownCard> KnownCards() =>
        Monitor.Cards.Select(c => new KnownCard(c.CardId, Aliases(c))).ToList();

    private static IReadOnlyList<string> Aliases(DetectedCard c)
    {
        var list = new List<string> { c.Name };
        switch (c.Profile?.Id)
        {
            case "dji-osmo-pocket": list.AddRange(["pocket", "osmo", "dji"]); break;
            case "panasonic-lumix": list.AddRange(["lumix", "camera", "s9"]); break;
            case "dji-mic": list.AddRange(["mic", "dji mic", "recorder", "audio recorder"]); break;
        }
        return list;
    }
}

public abstract class PageViewModel : ObservableObject
{
    /// <summary>True for the small "Ready to copy" window size.</summary>
    public virtual bool Compact => false;

    public virtual void OnShown()
    {
    }

    public virtual void OnHidden()
    {
    }
}

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ShootDropContext _ctx;
    private PageViewModel? _home;

    [ObservableProperty] private PageViewModel? _current;

    // ---- Toast ------------------------------------------------------------------------------------
    private readonly Avalonia.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private Action? _toastHandler;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private string? _toastText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToastAction))]
    private string? _toastAction;

    public bool HasToast => !string.IsNullOrEmpty(ToastText);
    public bool HasToastAction => !string.IsNullOrEmpty(ToastAction);

    /// <summary>Shows a short message at the bottom of the window for a few seconds, optionally with an action (e.g. Undo).</summary>
    public void ShowToast(string text, string? actionLabel = null, Action? action = null)
    {
        ToastText = text;
        ToastAction = action is null ? null : actionLabel;
        _toastHandler = action;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(action is null ? 4 : 8);
        _toastTimer.Start();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToastAct()
    {
        var handler = _toastHandler;
        DismissToast();
        handler?.Invoke();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void DismissToast()
    {
        _toastTimer.Stop();
        _toastHandler = null;
        ToastText = null;
        ToastAction = null;
    }

    public ShellViewModel(ShootDropContext ctx)
    {
        _ctx = ctx;
        ctx.Shell = this;
        _toastTimer.Tick += (_, _) => DismissToast();
    }

    public void Start()
    {
        _ctx.Monitor.Start();
        if (string.IsNullOrWhiteSpace(_ctx.Settings.ProjectsRoot)) GoWelcome();
        else GoReady();
    }

    private void Show(PageViewModel page)
    {
        Current?.OnHidden();
        Current = page;
        page.OnShown();
    }

    public void GoWelcome() => Show(new WelcomePageViewModel(_ctx));

    public void GoReady() => Show(_home = new ReadyPageViewModel(_ctx));

    public void GoMain() => Show(_home = new MainPageViewModel(_ctx));

    /// <summary>Back to whichever of Ready / Main the user came from.</summary>
    public void GoHome()
    {
        if (_home is MainPageViewModel) GoMain();
        else GoReady();
    }

    public void GoReview(CopyPreview preview) => Show(new ReviewPageViewModel(_ctx, preview));

    public void GoCopying(CopyPreview preview) => Show(new CopyingPageViewModel(_ctx, preview));

    public void GoDone(DonePageViewModel done) => Show(done);

    public void GoSettings(string tab = "General") => Show(new SettingsPageViewModel(_ctx, tab));
}
