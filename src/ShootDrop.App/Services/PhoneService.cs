using System.Diagnostics;
using Avalonia.Threading;
using ShootDrop.App.ViewModels;
using ShootDrop.Core.Phone;

namespace ShootDrop.App.Services;

/// <summary>
/// iPhone over USB (macOS). Watches for an iPhone being plugged in, brings its new videos into the staging "card",
/// then lets the normal card flow take over (the autopilot screen opens because a new card appeared).
/// Reading the phone only: nothing on it is changed or deleted.
/// </summary>
public sealed class PhoneService(ShootDropContext ctx, string settingsDirectory) : IDisposable
{
    private readonly IPhoneHelper? _helper = IPhoneHelper.Find();
    private Process? _watch;
    private bool _busy;
    private DateTime _startedUtc;

    /// <summary>True when the helper is installed (macOS app bundle).</summary>
    public bool Available => _helper is not null;

    private string StagingRoot => PhoneImport.StagingRoot(ctx.Settings, settingsDirectory);

    public void Start()
    {
        // An iPhone already plugged in when ShootDrop starts (e.g. at login) isn't "new": no pop-up for it,
        // the same rule as for cards. Use the menu bar's "Copy from iPhone" for that.
        _startedUtc = DateTime.UtcNow;
        _watch = _helper?.Watch((added, device) =>
        {
            if (added && DateTime.UtcNow - _startedUtc > TimeSpan.FromSeconds(10)) Dispatcher.UIThread.Post(() => _ = ImportAsync());
        });
    }

    /// <summary>Brings the iPhone's new videos in, then refreshes the cards so the autopilot screen opens.</summary>
    public async Task ImportAsync()
    {
        if (_busy || _helper is null || ctx.Settings.ProjectsRoot.Length == 0) return;
        _busy = true;
        ctx.Background?.ShowWindow();
        try
        {
            Say("Reading your iPhone…");
            var clips = await _helper.ListAsync(() => Dispatcher.UIThread.Post(() =>
            {
                Say("Unlock your iPhone (and tap Trust if it asks) to copy its videos.");
                ctx.Platform.Notify("ShootDrop", "Unlock your iPhone to copy its videos.");
            }));
            var root = StagingRoot;
            var fresh = PhoneImport.SelectNew(clips, ctx.Settings, PhoneImport.Staged(root));
            if (fresh.Count == 0)
            {
                Say(PhoneImport.Staged(root).Count > 0 ? "iPhone videos are ready to copy." : "Nothing new on the iPhone.");
                ctx.Monitor.Refresh();
                return;
            }

            var storage = ctx.CheckStorage(2 * fresh.Sum(c => c.Size)); // staged copy + project copy, same drive
            if (storage.Blocks)
            {
                Say(storage.Message);
                return;
            }

            var folder = PhoneImport.PrepareCard(root);
            var got = await _helper.DownloadAsync(fresh, folder,
                (n, total) => Dispatcher.UIThread.Post(() => Say($"Bringing in iPhone videos: {n} of {total}…")));
            if (got < fresh.Count) Say($"{fresh.Count - got} iPhone video(s) couldn't be read; the rest are ready.");
            ctx.Monitor.Refresh(); // the staging card appears → the autopilot screen opens
        }
        catch (InvalidOperationException e)
        {
            Say("iPhone: " + e.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Empties the staging card once its clips are in projects.</summary>
    public void ClearStaging()
    {
        PhoneImport.Clear(StagingRoot);
        ctx.Monitor.Refresh();
    }

    private void Say(string text) => ctx.Shell.ShowToast(text);

    public void Dispose()
    {
        try { _watch?.Kill(); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _watch?.Dispose();
    }
}
