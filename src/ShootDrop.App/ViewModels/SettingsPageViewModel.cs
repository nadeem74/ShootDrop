using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core.Background;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;

namespace ShootDrop.App.ViewModels;

public sealed partial class FolderRow(string name, string path, int depth, string feeds, Action<string> remove) : ObservableObject
{
    /// <summary>True while something is dragged over this folder (drag and drop in the folder editor).</summary>
    [ObservableProperty] private bool _isDropTarget;

    public string Name { get; } = name;
    public string Path { get; } = path;
    public Avalonia.Thickness Indent { get; } = new(depth * 22, 0, 0, 0);
    public string Feeds { get; } = feeds;
    public bool HasFeeds => Feeds.Length > 0;
    public IRelayCommand RemoveCommand { get; } = new RelayCommand(() => remove(path));
}

/// <summary>One line of the routing table (FR-18): a source and the folder it goes to.</summary>
public sealed partial class RouteRow : ObservableObject
{
    private readonly Action<RouteRow> _changed;

    public RouteRow(string key, string source, string what, IEnumerable<string> options, string current, Action<RouteRow> changed)
    {
        Key = key;
        Source = source;
        What = what;
        Options = new ObservableCollection<string>(options);
        _selected = current.Length == 0 ? SettingsPageViewModel.DontCopy : current;
        _changed = changed;
    }

    public string Key { get; }
    public string Source { get; }
    public string What { get; }
    public ObservableCollection<string> Options { get; }
    [ObservableProperty] private string? _selected;

    partial void OnSelectedChanged(string? value) => _changed(this);
}

/// <summary>One camera in Settings › Cameras, with Edit and (for cameras the user added or changed) Delete/Reset.</summary>
public sealed class ProfileRow(CameraProfile profile, string recognisedBy, string folder, bool builtIn, bool edited,
    Action<ProfileRow> edit, Action<ProfileRow> delete)
{
    public CameraProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string RecognisedBy { get; } = recognisedBy;
    public string Folder { get; } = folder;
    public string Badge { get; } = builtIn ? (edited ? "Built-in, edited" : "Built-in") : "Added by you";
    public bool CanDelete { get; } = !builtIn || edited;
    public string DeleteLabel { get; } = builtIn ? "Reset" : "Delete";
    public IRelayCommand EditCommand => new RelayCommand(() => edit(this));
    public IRelayCommand DeleteCommand => new RelayCommand(() => delete(this));
}

/// <summary>A plugged-in drive the camera editor can read to fill in the form.</summary>
public sealed record CardChoice(string Name, string Path)
{
    public override string ToString() => Name;
}

public sealed class RememberedCard(string card, string folder, Action<string> forget)
{
    public string Card { get; } = card;
    public string Folder { get; } = Format.Folder(folder);
    public IRelayCommand ForgetCommand { get; } = new RelayCommand(() => forget(card));
}

public sealed record ExtensionGroup(string Kind, IReadOnlyList<ExtensionChip> Items);

/// <summary>One extension shown as a chip with a remove button.</summary>
public sealed class ExtensionChip(string extension, Action<string> remove)
{
    public string Name { get; } = extension.TrimStart('.').ToUpperInvariant();
    public IRelayCommand RemoveCommand => new RelayCommand(() => remove(extension));
}

/// <summary>Settings: General, Project folders (FR-17/18), Cameras, File types, Shortcut &amp; background (FR-21/23/24).</summary>
public sealed partial class SettingsPageViewModel : PageViewModel
{
    public const string DontCopy = "Don't copy";
    public const string WithVideo = "With the video";
    private const string TopLevel = "Top level";
    private readonly ShootDropContext _ctx;
    private bool _loading;

    public SettingsPageViewModel(ShootDropContext ctx, string tab)
    {
        _ctx = ctx;
        _tabIndex = tab switch { "Folders" => 1, "Cameras" => 2, "File types" => 3, "Shortcut" => 4, _ => 0 };
        _projectsRoot = ctx.Settings.ProjectsRoot;
        _templateDirectory = ctx.Settings.TemplateDirectory ?? "";
        _newKind = "Video";
        LoadFolders();
        LoadCameras();
        LoadTypes();
    }

    [ObservableProperty] private int _tabIndex;

    // ---- General ---------------------------------------------------------------------------
    [ObservableProperty] private string _projectsRoot;
    [ObservableProperty] private string _templateDirectory;
    public bool HasTemplateDirectory => TemplateDirectory.Length > 0;

    public bool OpenFinderWhenDone
    {
        get => _ctx.Settings.OpenFinderWhenDone;
        set { _ctx.Settings.OpenFinderWhenDone = value; _ctx.Save(); OnPropertyChanged(); }
    }

    public bool EjectWhenDone
    {
        get => _ctx.Settings.EjectWhenDone;
        set { _ctx.Settings.EjectWhenDone = value; _ctx.Save(); OnPropertyChanged(); }
    }

    [RelayCommand]
    private async Task ChooseRootAsync()
    {
        var path = await _ctx.Platform.PickFolderAsync("Where should projects go?");
        if (path is null) return;
        ProjectsRoot = path;
        _ctx.Settings.ProjectsRoot = path;
        _ctx.Save();
        _ctx.Monitor.Refresh();
    }

    [RelayCommand]
    private async Task ChooseTemplateDirectoryAsync()
    {
        var path = await _ctx.Platform.PickFolderAsync("Folder to copy into every new project");
        if (path is null) return;
        TemplateDirectory = path;
        _ctx.Settings.TemplateDirectory = path;
        _ctx.Save();
        OnPropertyChanged(nameof(HasTemplateDirectory));
    }

    [RelayCommand]
    private void ClearTemplateDirectory()
    {
        TemplateDirectory = "";
        _ctx.Settings.TemplateDirectory = null;
        _ctx.Save();
        OnPropertyChanged(nameof(HasTemplateDirectory));
    }

    // ---- Shortcut & background (v0.2) -----------------------------------------------------------
    public IReadOnlyList<string> ArrivalChoices { get; } =
        ["Show Ready to copy", "Copy automatically with the last setup", "Do nothing"];

    public bool KeepRunningInMenuBar
    {
        get => _ctx.Settings.Background.KeepRunningInMenuBar;
        set { _ctx.Settings.Background.KeepRunningInMenuBar = value; _ctx.Save(); OnPropertyChanged(); }
    }

    public bool StartAtLogin
    {
        get => _ctx.Settings.Background.StartAtLogin;
        set { _ctx.Background?.SetStartAtLogin(value); OnPropertyChanged(); }
    }

    public bool CanStartAtLogin => BackgroundController.CanStartAtLogin;
    public bool CannotStartAtLogin => !CanStartAtLogin;

    public bool ShortcutEnabled
    {
        get => _ctx.Settings.Background.ShortcutEnabled;
        set
        {
            _ctx.Settings.Background.ShortcutEnabled = value;
            _ctx.Save();
            _ctx.Background?.ApplyShortcut();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutStatus));
        }
    }

    public Shortcut Shortcut => _ctx.Settings.Background.Shortcut;
    public IReadOnlyList<string> ShortcutKeys => Shortcut.Parts();
    public string ShortcutLabel => $"{Shortcut} opens Ready to copy from any app";
    public bool IsDefaultShortcut => Shortcut == Shortcut.Default;
    public bool CanResetShortcut => !IsDefaultShortcut && !IsRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotRecording), nameof(CanResetShortcut))]
    private bool _isRecording;

    public bool NotRecording => !IsRecording;

    [ObservableProperty] private string _recordHint = "";
    [ObservableProperty] private string _shortcutMessage = "";

    public string ShortcutStatus =>
        !OperatingSystem.IsMacOS() ? "The shortcut works on macOS for now."
        : !ShortcutEnabled ? "Off."
        : _ctx.Background?.ShortcutActive == true ? $"Ready: press {Shortcut} in any app."
        : $"Another app is using {Shortcut}. Choose a different shortcut.";

    [RelayCommand]
    private void ChangeShortcut()
    {
        ShortcutMessage = "";
        RecordHint = "Press the new shortcut now…  (Esc to cancel)";
        IsRecording = true;
        _ctx.Background?.PauseShortcut(true); // so the old shortcut doesn't fire while recording
    }

    [RelayCommand]
    private void CancelRecording() => StopRecording();

    [RelayCommand]
    private void ResetShortcut() => TrySave(Shortcut.Default);

    private void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _ctx.Background?.PauseShortcut(false);
        RefreshShortcut();
    }

    /// <summary>Called by the view for each key press while recording. Returns true if the key was used.</summary>
    public bool Record(string? key, ShortcutModifiers modifiers, bool escape)
    {
        if (!IsRecording) return false;
        if (escape && modifiers == ShortcutModifiers.None)
        {
            StopRecording();
            return true;
        }
        if (key is null) return modifiers != ShortcutModifiers.None; // only ⌃⌥⇧⌘ so far: keep waiting
        var candidate = new Shortcut(modifiers, key);
        if (candidate.Problem is { } problem)
        {
            RecordHint = $"{candidate}: {problem}";
            return true;
        }
        IsRecording = false;
        _ctx.Background?.PauseShortcut(false);
        TrySave(candidate);
        return true;
    }

    /// <summary>Saves the shortcut only if macOS accepts it; otherwise keeps the old one.</summary>
    private void TrySave(Shortcut shortcut)
    {
        var bg = _ctx.Settings.Background;
        var old = bg.Shortcut;
        bg.Shortcut = shortcut;
        var ok = _ctx.Background?.ApplyShortcut() ?? true;
        if (!ok && OperatingSystem.IsMacOS() && bg.ShortcutEnabled)
        {
            bg.Shortcut = old;
            _ctx.Background?.ApplyShortcut();
            ShortcutMessage = $"{shortcut} is already used by another app or by macOS. Try another one.";
        }
        else
        {
            _ctx.Save();
            ShortcutMessage = shortcut == old ? "" : $"Saved. {shortcut} now opens Ready to copy.";
        }
        RefreshShortcut();
    }

    private void RefreshShortcut()
    {
        foreach (var name in new[] { nameof(Shortcut), nameof(ShortcutKeys), nameof(ShortcutLabel), nameof(IsDefaultShortcut),
                     nameof(CanResetShortcut), nameof(ShortcutStatus) })
            OnPropertyChanged(name);
    }

    public override void OnHidden() => StopRecording();

    public int ArrivalIndex
    {
        get => (int)_ctx.Settings.Background.WhenCardsPluggedIn;
        set
        {
            _ctx.Settings.Background.WhenCardsPluggedIn = (CardArrivalAction)Math.Clamp(value, 0, 2);
            _ctx.Save();
            _ctx.Background?.BuildMenu();
            OnPropertyChanged();
        }
    }

    // ---- Project folders -------------------------------------------------------------------
    public ObservableCollection<FolderRow> Folders { get; } = [];
    public ObservableCollection<RouteRow> Routes { get; } = [];
    public ObservableCollection<string> Parents { get; } = [];
    [ObservableProperty] private string _templateName = "";
    [ObservableProperty] private string _newFolderName = "";
    [ObservableProperty] private string? _newFolderParent;
    [ObservableProperty] private string _example = "";

    private void LoadFolders()
    {
        _loading = true;
        var s = _ctx.Settings;
        TemplateName = s.Template.Name;
        var all = s.Template.Folders.SelectMany(Prefixes).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

        var routes = SourceRoutes();
        Folders.Clear();
        foreach (var f in all)
        {
            var feeds = string.Join(", ", routes.Where(r => string.Equals(r.Folder, f, StringComparison.OrdinalIgnoreCase)).Select(r => r.Source));
            Folders.Add(new FolderRow(f.Split('/')[^1], f, f.Count(c => c == '/'), feeds.Length > 0 ? "← " + feeds : "", RemoveFolder));
        }

        Parents.Clear();
        Parents.Add(TopLevel);
        foreach (var f in all) Parents.Add(f);
        NewFolderParent = TopLevel;

        var options = new List<string> { DontCopy };
        options.AddRange(all);
        Routes.Clear();
        foreach (var r in routes)
        {
            var rowOptions = r.Key == "kind:Audio" ? new[] { WithVideo }.Concat(options).ToList() : options;
            Routes.Add(new RouteRow(r.Key, r.Source, r.What, rowOptions, r.Folder, OnRouteChanged));
        }
        UpdateExample();
        _loading = false;
    }

    private sealed record SourceRoute(string Key, string Source, string What, string Folder);

    private List<SourceRoute> SourceRoutes()
    {
        var s = _ctx.Settings;
        var list = new List<SourceRoute>();
        foreach (var p in ProfileCatalog.Load(_ctx.Store.ProfilesDirectory))
        {
            var folder = s.Routing.ByProfile.TryGetValue(p.Id, out var f) ? f : p.DefaultFolder;
            list.Add(new SourceRoute("profile:" + p.Id, p.Name, p.Match.AudioOnlyWithoutDcim ? "Audio" : "Video", folder));
        }
        foreach (var (card, folder) in s.Routing.ByCard)
            list.Add(new SourceRoute("card:" + card, card, "Card you routed by hand", folder));
        list.Add(new SourceRoute("kind:Audio", "Audio files from any card", "AAC, WAV: e.g. the Pocket's mic track",
            s.Routing.ByKind.TryGetValue(FileKind.Audio, out var audio) ? audio : WithVideo));
        list.Add(new SourceRoute("kind:Photo", "Photos from any card", "JPG, HEIC, RW2, DNG", s.Routing.ByKind.GetValueOrDefault(FileKind.Photo, "")));
        list.Add(new SourceRoute("kind:Proxy", "Proxy files", "LRF from DJI", s.Routing.ByKind.GetValueOrDefault(FileKind.Proxy, "")));
        return list;
    }

    private static IEnumerable<string> Prefixes(string folder)
    {
        var parts = folder.Trim('/').Split('/');
        for (var i = 1; i <= parts.Length; i++) yield return string.Join('/', parts[..i]);
    }

    private void OnRouteChanged(RouteRow row)
    {
        if (_loading) return;
        var value = row.Selected is null or DontCopy ? "" : row.Selected;
        var r = _ctx.Settings.Routing;
        var (type, key) = (row.Key.Split(':', 2)[0], row.Key.Split(':', 2)[1]);
        switch (type)
        {
            case "profile": r.ByProfile[key] = value; break;
            case "card": r.ByCard[key] = value; break;
            case "kind" when row.Selected == WithVideo: r.ByKind.Remove(Enum.Parse<FileKind>(key)); break;
            case "kind": r.ByKind[Enum.Parse<FileKind>(key)] = value; break;
        }
        _ctx.Save();
        LoadFolders();
    }

    private void UpdateExample()
    {
        var pocket = Routes.FirstOrDefault(r => r.Key == "profile:dji-osmo-pocket");
        Example = pocket?.Selected is { } f && f != DontCopy
            ? $"Video Projects / Sinhagad trip / {Format.Folder(f)} / DJI_20261122_0012.MP4"
            : "Pocket clips are not copied with this setup.";
    }

    private void SetTemplate(FolderTemplate t)
    {
        _ctx.Settings.Template = t;
        _ctx.Save();
        LoadFolders();
    }

    // ---- Drag and drop in the folder tree ---------------------------------------------------------
    public const string ProjectRootTarget = "";

    [ObservableProperty] private string _folderMessage = "";

    /// <summary>True while something is dragged over the project itself (move to the top level).</summary>
    [ObservableProperty] private bool _rootIsDropTarget;

    /// <summary>A folder was dropped on <paramref name="target"/> (a folder path, or "" for the project itself).</summary>
    public void MoveFolder(string folder, string target)
    {
        var routesBefore = SourceRoutes();
        FolderTemplate moved;
        string newPath;
        try
        {
            moved = _ctx.Settings.Template.Move(folder, target, out newPath);
        }
        catch (InvalidOperationException e)
        {
            FolderMessage = e.Message;
            return;
        }
        if (ReferenceEquals(moved, _ctx.Settings.Template)) return;

        // Everything that was going into the moved folder follows it, including cameras that only used their
        // profile's default folder (written down explicitly now, so they don't recreate the old folder).
        var r = _ctx.Settings.Routing;
        foreach (var route in routesBefore)
        {
            if (route.Folder.Length == 0) continue;
            var rebased = FolderTemplate.Rebase(route.Folder, folder, newPath);
            if (rebased != route.Folder.Trim('/')) SetRoute(route.Key, rebased);
        }
        foreach (var (card, value) in r.ByCard.ToList()) r.ByCard[card] = FolderTemplate.Rebase(value, folder, newPath);

        FolderMessage = $"Moved “{folder.Split('/')[^1]}” to {(target.Length == 0 ? "the top of the project" : "“" + target + "”")}.";
        SetTemplate(moved with { Name = "Custom" });
    }

    /// <summary>A source (camera, card or file type) was dropped on a folder: its files go there from now on.</summary>
    public void RouteToFolder(string routeKey, string target)
    {
        if (target.Length == 0)
        {
            FolderMessage = "Drop it on a folder, not on the project itself.";
            return;
        }
        var row = Routes.FirstOrDefault(x => x.Key == routeKey);
        if (row is null) return;
        row.Selected = target; // OnRouteChanged saves and refreshes
        FolderMessage = $"{row.Source} now goes to {Format.Folder(target)}.";
    }

    private void SetRoute(string routeKey, string folder)
    {
        var r = _ctx.Settings.Routing;
        var (type, key) = (routeKey.Split(':', 2)[0], routeKey.Split(':', 2)[1]);
        switch (type)
        {
            case "profile": r.ByProfile[key] = folder; break;
            case "card": r.ByCard[key] = folder; break;
            case "kind": r.ByKind[Enum.Parse<FileKind>(key)] = folder; break;
        }
    }

    private void RemoveFolder(string path) => SetTemplate(_ctx.Settings.Template.Without(path) with { Name = "Custom" });

    [RelayCommand]
    private void AddFolder()
    {
        var name = NewFolderName.Trim().Replace('/', '-').Replace(':', '-');
        if (name.Length == 0) return;
        var parent = NewFolderParent is null or TopLevel ? "" : NewFolderParent + "/";
        NewFolderName = "";
        SetTemplate(_ctx.Settings.Template.With(parent + name) with { Name = "Custom" });
    }

    [RelayCommand]
    private void UsePreset(string which)
    {
        var r = _ctx.Settings.Routing;
        switch (which)
        {
            case "roll":
                r.ByProfile["dji-osmo-pocket"] = "Video/A-roll";
                r.ByProfile["panasonic-lumix"] = "Video/B-roll";
                r.ByProfile["dji-mic"] = "Audio";
                r.ByKind[FileKind.Audio] = "Audio";
                r.ByKind[FileKind.Photo] = "Images";
                SetTemplate(FolderTemplate.ARollBRoll);
                break;
            case "one":
                foreach (var id in new[] { "dji-osmo-pocket", "panasonic-lumix", "dji-mic" }) r.ByProfile[id] = "Footage";
                r.ByKind[FileKind.Audio] = "Footage";
                r.ByKind[FileKind.Photo] = "Footage";
                SetTemplate(FolderTemplate.OneFolder);
                break;
            default:
                r.ByProfile.Remove("dji-osmo-pocket");
                r.ByProfile.Remove("panasonic-lumix");
                r.ByProfile.Remove("dji-mic");
                r.ByKind[FileKind.Audio] = Routing.DefaultAudioFolder;
                r.ByKind[FileKind.Photo] = "Images";
                SetTemplate(FolderTemplate.MyTemplate);
                break;
        }
    }

    [RelayCommand]
    private async Task ImportTemplateAsync()
    {
        var path = await _ctx.Platform.PickFolderAsync("Choose a folder whose structure to copy");
        if (path is null) return;
        SetTemplate(FolderTemplate.FromDirectory(path));
    }

    // ---- Cameras: list, add, edit, delete -------------------------------------------------------
    public ObservableCollection<ProfileRow> Profiles { get; } = [];
    public ObservableCollection<RememberedCard> Remembered { get; } = [];
    public bool HasRemembered => Remembered.Count > 0;

    private string ProfilesDir => _ctx.Store.ProfilesDirectory;

    private void LoadCameras()
    {
        Profiles.Clear();
        var userIds = UserProfileIds();
        foreach (var p in ProfileCatalog.Load(ProfilesDir).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var builtIn = ProfileCatalog.IsBuiltIn(p.Id);
            Profiles.Add(new ProfileRow(p, Describe(p.Match),
                Format.Folder(_ctx.Settings.Routing.ByProfile.GetValueOrDefault(p.Id, p.DefaultFolder)),
                builtIn, builtIn && userIds.Contains(p.Id), EditCamera, DeleteCamera));
        }
        Remembered.Clear();
        foreach (var (card, folder) in _ctx.Settings.Routing.ByCard) Remembered.Add(new RememberedCard(card, folder, Forget));
        OnPropertyChanged(nameof(HasRemembered));
    }

    private HashSet<string> UserProfileIds()
    {
        var builtIn = ProfileCatalog.BuiltIn().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        return ProfileCatalog.Load(ProfilesDir)
            .Where(p => !builtIn.TryGetValue(p.Id, out var b) || !SameProfile(b, p))
            .Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameProfile(CameraProfile a, CameraProfile b) =>
        a.Name == b.Name && a.DefaultFolder == b.DefaultFolder && Describe(a.Match) == Describe(b.Match)
        && a.Match.VolumeNameContains.SequenceEqual(b.Match.VolumeNameContains);

    private static string Describe(ProfileMatch m)
    {
        var parts = new List<string>();
        if (m.MarkerPaths.Length > 0) parts.Add(string.Join(", ", m.MarkerPaths) + " folder");
        if (m.FilePrefixes.Length > 0) parts.Add(string.Join(", ", m.FilePrefixes) + " file names");
        if (m.DcimFolderContains.Length > 0) parts.Add("DCIM folders with " + string.Join("/", m.DcimFolderContains));
        if (m.AudioOnlyWithoutDcim) parts.Add("audio files, no DCIM folder");
        if (m.VolumeNameContains.Length > 0) parts.Add("card named " + string.Join("/", m.VolumeNameContains));
        return parts.Count == 0 ? "nothing yet" : string.Join("; ", parts);
    }

    private void Forget(string card)
    {
        _ctx.Settings.Routing.ByCard.Remove(card);
        _ctx.Save();
        LoadCameras();
        LoadFolders();
    }

    // The editor form
    private string? _editingId;
    [ObservableProperty] private bool _isEditingCamera;
    [ObservableProperty] private string _cameraEditorTitle = "";
    [ObservableProperty] private string _camName = "";
    [ObservableProperty] private string _camFolder = "";
    [ObservableProperty] private string _camVolume = "";
    [ObservableProperty] private string _camDcim = "";
    [ObservableProperty] private string _camPrefixes = "";
    [ObservableProperty] private string _camMarkers = "";
    [ObservableProperty] private bool _camAudioOnly;
    [ObservableProperty] private string _camError = "";
    [ObservableProperty] private CardChoice? _selectedCard;
    [ObservableProperty] private string? _camFolderPick;
    public ObservableCollection<CardChoice> PluggedCards { get; } = [];
    public ObservableCollection<string> FolderChoices { get; } = [];
    public bool HasPluggedCards => PluggedCards.Count > 0;
    public bool NoPluggedCards => PluggedCards.Count == 0;
    public bool NotEditingCamera => !IsEditingCamera;

    partial void OnIsEditingCameraChanged(bool value) => OnPropertyChanged(nameof(NotEditingCamera));

    partial void OnCamFolderPickChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value)) CamFolder = value;
    }

    [RelayCommand]
    private void AddCamera() => OpenEditor(null);

    private void EditCamera(ProfileRow row) => OpenEditor(row.Profile);

    private void OpenEditor(CameraProfile? p)
    {
        _editingId = p?.Id;
        CameraEditorTitle = p is null ? "Add a camera" : $"Edit {p.Name}";
        CamName = p?.Name ?? "";
        CamFolder = p is null ? "" : _ctx.Settings.Routing.ByProfile.GetValueOrDefault(p.Id, p.DefaultFolder);
        FillMatch(p?.Match ?? new ProfileMatch());
        CamError = "";
        CamFolderPick = null;

        FolderChoices.Clear();
        foreach (var f in _ctx.Settings.Template.AllFolders().Order(StringComparer.OrdinalIgnoreCase)) FolderChoices.Add(f);

        PluggedCards.Clear();
        try
        {
            var projects = _ctx.Settings.ProjectsRoot;
            foreach (var v in new SystemVolumeProvider().List())
                if (projects.Length == 0 || !projects.StartsWith(v.Path, StringComparison.OrdinalIgnoreCase))
                    PluggedCards.Add(new CardChoice(v.Name, v.Path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        SelectedCard = PluggedCards.FirstOrDefault();
        OnPropertyChanged(nameof(HasPluggedCards));
        OnPropertyChanged(nameof(NoPluggedCards));
        IsEditingCamera = true;
    }

    private void FillMatch(ProfileMatch m)
    {
        CamVolume = string.Join(", ", m.VolumeNameContains);
        CamDcim = string.Join(", ", m.DcimFolderContains);
        CamPrefixes = string.Join(", ", m.FilePrefixes);
        CamMarkers = string.Join(", ", m.MarkerPaths);
        CamAudioOnly = m.AudioOnlyWithoutDcim;
    }

    /// <summary>Reads the chosen card (names only, read-only) and fills in how to recognise it.</summary>
    [RelayCommand]
    private async Task FillFromCardAsync()
    {
        if (SelectedCard is not { } card) return;
        var types = _ctx.Settings.BuildFileTypes();
        var m = await Task.Run(() => ProfileSuggester.Suggest(card.Path, card.Name, types));
        FillMatch(m);
        if (CamName.Length == 0) CamName = card.Name;
        if (CamFolder.Length == 0) CamFolder = m.AudioOnlyWithoutDcim ? "Audio/" + card.Name : "Video/" + card.Name;
        CamError = "";
        _ctx.Shell.ShowToast($"Filled in from {card.Name}. Check it, then Save.");
    }

    [RelayCommand]
    private void CancelCamera() => IsEditingCamera = false;

    [RelayCommand]
    private void SaveCamera()
    {
        var name = CamName.Trim();
        var folder = CamFolder.Trim().Trim('/');
        var match = new ProfileMatch
        {
            VolumeNameContains = Split(CamVolume),
            DcimFolderContains = Split(CamDcim),
            FilePrefixes = Split(CamPrefixes),
            MarkerPaths = Split(CamMarkers).Select(x => x.Replace('\\', '/').Trim('/')).ToArray(),
            AudioOnlyWithoutDcim = CamAudioOnly,
        };
        if (name.Length == 0) { CamError = "Give the camera a name."; return; }
        if (folder.Length == 0) { CamError = "Choose the folder its files go to."; return; }
        if (Describe(match) == "nothing yet")
        {
            CamError = "Add at least one way to recognise it, or use “Fill in from a plugged-in card”.";
            return;
        }

        var id = _editingId ?? ProfileCatalog.NewId(name, ProfileCatalog.Load(ProfilesDir).Select(p => p.Id));
        var profile = new CameraProfile { Id = id, Name = name, DefaultFolder = folder, Match = match };
        try
        {
            ProfileCatalog.Save(ProfilesDir, profile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CamError = "Couldn't save: " + e.Message;
            return;
        }
        _ctx.Settings.Routing.ByProfile[id] = folder;
        if (!_ctx.Settings.Template.AllFolders().Contains(folder, StringComparer.OrdinalIgnoreCase))
            _ctx.Settings.Template = _ctx.Settings.Template.With(folder) with { Name = "Custom" };
        _ctx.Save();
        CamerasChanged();
        IsEditingCamera = false;
        _ctx.Shell.ShowToast($"Saved {name}. Its files go to {Format.Folder(folder)}.");
    }

    private void DeleteCamera(ProfileRow row)
    {
        var p = row.Profile;
        var builtIn = ProfileCatalog.IsBuiltIn(p.Id);
        var route = _ctx.Settings.Routing.ByProfile.GetValueOrDefault(p.Id);
        ProfileCatalog.Delete(ProfilesDir, p.Id);
        if (!builtIn) _ctx.Settings.Routing.ByProfile.Remove(p.Id);
        _ctx.Save();
        CamerasChanged();
        _ctx.Shell.ShowToast(builtIn ? $"{p.Name} is back to the built-in settings." : $"Deleted {p.Name}.", "Undo", () =>
        {
            ProfileCatalog.Save(ProfilesDir, p);
            if (route is not null) _ctx.Settings.Routing.ByProfile[p.Id] = route;
            _ctx.Save();
            CamerasChanged();
        });
    }

    /// <summary>Cameras changed on disk: reload them everywhere and re-detect the plugged-in cards.</summary>
    private void CamerasChanged()
    {
        _ctx.Service.Profiles = ProfileCatalog.Load(ProfilesDir);
        LoadCameras();
        LoadFolders();
        _ctx.Monitor.Refresh();
    }

    private static string[] Split(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [RelayCommand] private void OpenProfilesFolder()
    {
        Directory.CreateDirectory(ProfilesDir);
        _ctx.Platform.OpenFolder(ProfilesDir);
    }

    // ---- File types: add and remove, with Undo --------------------------------------------------------
    public ObservableCollection<ExtensionGroup> TypeGroups { get; } = [];
    public ObservableCollection<string> Kinds { get; } = ["Video", "Audio", "Photo", "Proxy"];
    [ObservableProperty] private string _newExtension = "";
    [ObservableProperty] private string _newKind;

    private void LoadTypes()
    {
        TypeGroups.Clear();
        var catalog = _ctx.Settings.BuildFileTypes();
        foreach (var kind in Enum.GetValues<FileKind>())
        {
            var items = catalog.Extensions.Where(kv => kv.Value == kind).Select(kv => kv.Key)
                .Order(StringComparer.OrdinalIgnoreCase).Select(e => new ExtensionChip(e, RemoveExtension)).ToList();
            TypeGroups.Add(new ExtensionGroup(Format.Kinds([kind]), items));
        }
    }

    private void RemoveExtension(string ext)
    {
        var kind = _ctx.Settings.BuildFileTypes().Extensions.GetValueOrDefault(ext);
        _ctx.Settings.RemoveExtension(ext);
        _ctx.Save();
        LoadTypes();
        var shown = ext.TrimStart('.').ToUpperInvariant();
        _ctx.Shell.ShowToast($"Removed .{shown}. Those files won't be copied.", "Undo", () =>
        {
            _ctx.Settings.AddExtension(ext, kind);
            _ctx.Save();
            LoadTypes();
        });
    }

    [RelayCommand]
    private void AddExtension()
    {
        var ext = NewExtension.Trim().TrimStart('.');
        if (ext.Length == 0 || !Enum.TryParse<FileKind>(NewKind, out var kind)) return;
        if (ext.Length > 8 || !ext.All(char.IsAsciiLetterOrDigit))
        {
            _ctx.Shell.ShowToast("An extension is a few letters or numbers, like BRAW or R3D.");
            return;
        }
        _ctx.Settings.AddExtension(ext, kind);
        _ctx.Save();
        NewExtension = "";
        LoadTypes();
        _ctx.Shell.ShowToast($"Added .{ext.ToUpperInvariant()} as {Format.Kinds([kind])}.");
    }

    // ---- Navigation ------------------------------------------------------------------------
    [RelayCommand]
    private void Done()
    {
        _ctx.Monitor.Refresh();
        _ctx.Shell.GoHome();
    }
}
