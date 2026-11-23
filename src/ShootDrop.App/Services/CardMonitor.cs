using Avalonia.Threading;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Settings;

namespace ShootDrop.App.Services;

/// <summary>
/// Watches for cards being plugged in or removed (FR-01). Every 2 s it lists volume names, which is cheap;
/// only when that list changes does it run profile detection and raise <see cref="Changed"/>.
/// </summary>
public sealed class CardMonitor
{
    private readonly ShootDropService _service;
    private readonly AppSettings _settings;
    private readonly IVolumeProvider _volumes;
    private readonly DispatcherTimer _timer;
    private string _signature = "";

    public CardMonitor(ShootDropService service, AppSettings settings, IVolumeProvider volumes)
    {
        _service = service;
        _settings = settings;
        _volumes = volumes;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Poll(force: false);
    }

    public IReadOnlyList<DetectedCard> Cards { get; private set; } = [];

    public event EventHandler? Changed;

    public void Start()
    {
        Poll(force: true);
        _timer.Start();
    }

    /// <summary>Re-detect now (e.g. after settings change).</summary>
    public void Refresh() => Poll(force: true);

    private void Poll(bool force)
    {
        IReadOnlyList<VolumeInfo> list;
        try
        {
            list = _volumes.List();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var signature = string.Join("|", list.Select(v => v.Path).Order()) + "#" + _settings.ProjectsRoot;
        if (!force && signature == _signature) return;
        _signature = signature;
        Cards = _service.DetectCards(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
