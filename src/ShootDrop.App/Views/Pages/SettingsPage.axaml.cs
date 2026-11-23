using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        AddHandler(PointerMovedEvent, OnDragMove, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragEnd, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // ---- Drag and drop in the folder editor ----------------------------------------------------------
    // In-app drag with pointer capture: press on a folder (or a camera in the routing list), move a few
    // pixels, and drop on a folder or on ‹project name›. Works the same on macOS, Windows and Linux.

    private const double DragThreshold = 6;
    private object? _dragItem;      // FolderRow or RouteRow
    private Point _dragStart;
    private bool _dragging;
    private object? _hover;         // FolderRow, or "root"

    private SettingsPageViewModel? Vm => DataContext as SettingsPageViewModel;

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: var item } c || !e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
        if (item is not (FolderRow or RouteRow)) return;
        _dragItem = item;
        _dragStart = e.GetPosition(this);
        _dragging = false;
        e.Pointer.Capture(c);
    }

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null) return;
        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _dragStart.X) + Math.Abs(pos.Y - _dragStart.Y) < DragThreshold) return;
            _dragging = true;
            DragGhostText.Text = _dragItem is FolderRow f ? "📁  " + f.Name : "→  " + ((RouteRow)_dragItem).Source;
            DragGhost.IsVisible = true;
        }
        Canvas.SetLeft(DragGhost, pos.X + 14);
        Canvas.SetTop(DragGhost, pos.Y + 10);
        SetHover(TargetAt(pos));
    }

    private void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem is null) return;
        var item = _dragItem;
        var target = _dragging ? TargetAt(e.GetPosition(this)) : null;
        var wasDragging = _dragging;
        EndDrag();
        e.Pointer.Capture(null);
        if (!wasDragging || target is null || Vm is not { } vm) return;

        var targetPath = target is FolderRow t ? t.Path : SettingsPageViewModel.ProjectRootTarget;
        if (item is FolderRow folder) vm.MoveFolder(folder.Path, targetPath);
        else if (item is RouteRow route) vm.RouteToFolder(route.Key, targetPath);
    }

    private void EndDrag()
    {
        _dragItem = null;
        _dragging = false;
        DragGhost.IsVisible = false;
        SetHover(null);
    }

    /// <summary>The folder row (or ‹project name›) under the pointer, if any.</summary>
    private object? TargetAt(Point pos)
    {
        if (this.InputHitTest(pos) is not Visual hit) return null;
        foreach (var v in hit.GetSelfAndVisualAncestors())
        {
            if (v is not Border { Tag: string tag } b) continue;
            if (tag == "root") return "root";
            if (tag == "folder" && b.DataContext is FolderRow row && !ReferenceEquals(row, _dragItem)) return row;
        }
        return null;
    }

    private void SetHover(object? target)
    {
        if (ReferenceEquals(target, _hover) || Equals(target, _hover)) return;
        if (_hover is FolderRow old) old.IsDropTarget = false;
        if (Vm is { } vm) vm.RootIsDropTarget = Equals(target, "root") && _dragItem is FolderRow;
        _hover = target;
        if (target is FolderRow row) row.IsDropTarget = true;
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
