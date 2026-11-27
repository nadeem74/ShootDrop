using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ShootDrop.App.Services;

/// <summary>Things only the operating system can do: pick folders, open Finder, eject cards, free space.</summary>
public interface IPlatform
{
    Task<string?> PickFolderAsync(string title);
    void OpenFolder(string path);
    Task<bool> EjectAsync(string volumePath);
    long? FreeBytes(string path);

    /// <summary>A system notification, e.g. "Copied 42 clips". Best effort: does nothing where unsupported.</summary>
    void Notify(string title, string text);
}

public sealed class DesktopPlatform : IPlatform
{
    private readonly Func<TopLevel?> _topLevel;

    public DesktopPlatform(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> PickFolderAsync(string title)
    {
        var top = _topLevel();
        if (top is null) return null;
        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        var opener = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "explorer" : "xdg-open";
        try
        {
            Process.Start(new ProcessStartInfo(opener) { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public async Task<bool> EjectAsync(string volumePath)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("diskutil")
            {
                ArgumentList = { "eject", volumePath },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public long? FreeBytes(string path) => Core.Copying.StorageCheck.FreeBytes(path);

    public void Notify(string title, string text)
    {
        if (!OperatingSystem.IsMacOS()) return;
        static string Quote(string v) => "\"" + v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        try
        {
            Process.Start(new ProcessStartInfo("osascript")
            {
                ArgumentList = { "-e", $"display notification {Quote(text)} with title {Quote(title)}" },
                UseShellExecute = false,
            });
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
