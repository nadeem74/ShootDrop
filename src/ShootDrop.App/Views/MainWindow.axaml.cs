using System.ComponentModel;
using Avalonia.Controls;
using ShootDrop.App.ViewModels;

namespace ShootDrop.App.Views;

public partial class MainWindow : Window
{
    private ShellViewModel? _shell;
    private bool? _compact;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null) _shell.PropertyChanged -= OnShellChanged;
            _shell = DataContext as ShellViewModel;
            if (_shell is not null) _shell.PropertyChanged += OnShellChanged;
        };
    }

    /// <summary>The Ready page is a small window; every other page uses the full window.</summary>
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.Current) || _shell?.Current is null) return;
        var compact = _shell.Current.Compact;
        if (_compact == compact) return;
        _compact = compact;
        if (compact)
        {
            MinWidth = 420;
            Width = 460;
            Height = 720;
        }
        else
        {
            Width = 1120;
            Height = 860;
            MinWidth = 760;
        }
    }
}
