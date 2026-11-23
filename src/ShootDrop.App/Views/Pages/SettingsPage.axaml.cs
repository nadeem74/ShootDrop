using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShootDrop.App.ViewModels;
using ShootDrop.Core.Background;

namespace ShootDrop.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        // Tunnel so the key press reaches the shortcut recorder before any focused control handles it.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsPageViewModel { IsRecording: true } vm) return;
        e.Handled = vm.Record(KeyName(e.Key), Modifiers(e.KeyModifiers), e.Key == Key.Escape);
    }

    /// <summary>The key's name as a <see cref="Shortcut"/> uses it: A–Z, 0–9, F1–F12; null for anything else.</summary>
    internal static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F12 => "F" + (key - Key.F1 + 1),
        _ => null,
    };

    /// <summary>Avalonia reports ⌘ Command as Meta and ⌥ Option as Alt on macOS.</summary>
    internal static ShortcutModifiers Modifiers(KeyModifiers m) =>
        (m.HasFlag(KeyModifiers.Control) ? ShortcutModifiers.Control : 0)
        | (m.HasFlag(KeyModifiers.Alt) ? ShortcutModifiers.Option : 0)
        | (m.HasFlag(KeyModifiers.Shift) ? ShortcutModifiers.Shift : 0)
        | (m.HasFlag(KeyModifiers.Meta) ? ShortcutModifiers.Command : 0);
}
