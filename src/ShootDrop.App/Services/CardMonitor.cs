using Avalonia.Threading;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Settings;

namespace ShootDrop.App.Services;

/// <summary>
/// Watches for cards being plugged in or removed (FR-01) at near-zero cost while idle (v0.2).
/// The OS tells us when the folder where drives appear changes (/Volumes on macOS, /media on Linux), and a slow
/// safety check runs every 15 s in case a notification is missed. Only when the list of volumes really changed
/// does it run profile detection and raise <see cref="Changed"/>. Windows has no such folder, so it checks every 3 s.
/// </summary>
public sealed class CardMonitor : IDisposable
{
    private readonly ShootDropService _service;
    private readonly AppSettings _settings;
    private readonly IVolumeProvider _volumes;
    private readonly DispatcherTimer _safetyCheck;
    private readonly DispatcherTimer _settle;
    private readonly List<FileSystemWatcher> _watchers = [];
    private string _signature = "";

    public CardMonitor(ShootDropService service, AppSettings settings, IVolumeProvider volumes)
    {
        _service = service;
        _settings = settings;
        _volumes = volumes;
        _safetyCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _safetyCheck.Tick += (_, _) => Poll(force: false);
        // A card takes a moment to finish mounting; wait for the notifications to settle before scanning.
        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Poll(force: false);
        };
    }

    public IReadOnlyList<DetectedCard> Cards { get; private set; } = [];

    public event EventHandler? Changed;

    public void Start()
    {
        Poll(force: true);
        foreach (var root in WatchRoots())
        {
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                };
                w.Created += OnVolumeFolderChanged;
                w.Deleted += OnVolumeFolderChanged;
                w.Renamed += OnVolumeFolderChanged;
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
            }
        }
        if (_watchers.Count == 0) _safetyCheck.Interval = TimeSpan.FromSeconds(3);
        _safetyCheck.Start();
    }

    /// <summary>Re-detect now (e.g. after settings change).</summary>
    public void Refresh() => Poll(force: true);

    private void OnVolumeFolderChanged(object sender, FileSystemEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _settle.Stop();
            _settle.Start();
        });

    private static IEnumerable<string> WatchRoots()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/Volumes";
        }
        else if (OperatingSystem.IsLinux())
        {
            var user = Environment.UserName;
            foreach (var p in new[] { "/media", Path.Combine("/media", user), Path.Combine("/run/media", user), "/mnt" })
                if (Directory.Exists(p)) yield return p;
        }
    }

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

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _safetyCheck.Stop();
    }
}
