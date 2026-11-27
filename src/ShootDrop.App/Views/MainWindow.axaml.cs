using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ShootDrop.App.ViewModels;

namespace ShootDrop.App.Views;

/// <summary>
/// One window, two sizes: the small "Ready to copy" window, and the full window for everything else.
/// Switching keeps the window where it was (same top, same centre), never larger than the screen, and the
/// full size is remembered exactly as the user last left it, so it doesn't jump between visits.
/// </summary>
public partial class MainWindow : Window
{
    private const double CompactWidth = 460, CompactHeight = 720;
    private const double FullWidth = 1120, FullHeight = 860;
    private const double FullMinWidth = 900, FullMinHeight = 620;

    private ShellViewModel? _shell;
    private bool? _compact;
    private bool _applying;
    private readonly DispatcherTimer _saveSize = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null) _shell.PropertyChanged -= OnShellChanged;
            _shell = DataContext as ShellViewModel;
            if (_shell is not null)
            {
                _shell.PropertyChanged += OnShellChanged;
                ApplyMode();
            }
        };
        Opened += (_, _) =>
        {
            _compact = null; // the screen is known now: place the window properly
            ApplyMode();
        };
        SizeChanged += (_, _) =>
        {
            if (_applying || _compact != false || WindowState != WindowState.Normal) return;
            _saveSize.Stop();
            _saveSize.Start();
        };
        _saveSize.Tick += (_, _) =>
        {
            _saveSize.Stop();
            RememberFullSize();
        };
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Current)) ApplyMode();
    }

    private void ApplyMode()
    {
        if (_shell?.Current is null) return;
        var compact = _shell.Current.Compact;
        if (_compact == compact) return;
        if (_compact == false) RememberFullSize();
        _compact = compact;
        if (WindowState is WindowState.Maximized or WindowState.FullScreen) WindowState = WindowState.Normal;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var area = screen?.WorkingArea;
        var maxW = area is { } a1 ? a1.Width / scale - 32 : double.MaxValue;
        var maxH = area is { } a2 ? a2.Height / scale - 32 : double.MaxValue;

        double w, h;
        var settings = _shell.Context.Settings;
        if (compact)
        {
            w = Math.Min(CompactWidth, maxW);
            h = Math.Min(CompactHeight, maxH);
        }
        else
        {
            w = Math.Min(settings.WindowWidth >= FullMinWidth ? settings.WindowWidth : FullWidth, maxW);
            h = Math.Min(settings.WindowHeight >= FullMinHeight ? settings.WindowHeight : FullHeight, maxH);
        }

        _applying = true;
        // Keep the window's top edge and horizontal centre where they are, so it grows or shrinks in place.
        var centreX = Position.X + (int)(Bounds.Width * scale / 2);
        var top = Position.Y;
        MinWidth = 0;
        MinHeight = 0;
        Width = w;
        Height = h;
        MinWidth = compact ? 420 : Math.Min(FullMinWidth, w);
        MinHeight = compact ? 560 : Math.Min(FullMinHeight, h);

        if (area is { } a && IsVisible)
        {
            var pw = (int)(w * scale);
            var ph = (int)(h * scale);
            var x = Math.Clamp(centreX - pw / 2, a.X + 8, Math.Max(a.X + 8, a.Right - pw - 8));
            var y = Math.Clamp(top, a.Y + 8, Math.Max(a.Y + 8, a.Bottom - ph - 8));
            Position = new PixelPoint(x, y);
        }
        _applying = false;
    }

    private void RememberFullSize()
    {
        if (_shell is null || _compact != false || WindowState != WindowState.Normal) return;
        var settings = _shell.Context.Settings;
        var w = (int)Math.Round(Bounds.Width);
        var h = (int)Math.Round(Bounds.Height);
        if (w < FullMinWidth || h < FullMinHeight || (w == settings.WindowWidth && h == settings.WindowHeight)) return;
        settings.WindowWidth = w;
        settings.WindowHeight = h;
        _shell.Context.Save();
    }
}
