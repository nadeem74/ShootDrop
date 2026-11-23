using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.App.Services;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;
using ShootDrop.Core.Projects;

namespace ShootDrop.App.ViewModels;

public sealed class FolderRow(string name, string path, int depth, string feeds, Action<string> remove)
{
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

public sealed record ProfileRow(string Name, string RecognisedBy, string Folder);

public sealed class RememberedCard(string card, string folder, Action<string> forget)
{
    public string Card { get; } = card;
    public string Folder { get; } = Format.Folder(folder);
    public IRelayCommand ForgetCommand { get; } = new RelayCommand(() => forget(card));
}

public sealed record ExtensionGroup(string Kind, string Extensions);

/// <summary>Settings: General, Project folders (FR-17/18), Cameras, File types, Shortcut (v0.2).</summary>
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

    // ---- Cameras ---------------------------------------------------------------------------
    public ObservableCollection<ProfileRow> Profiles { get; } = [];
    public ObservableCollection<RememberedCard> Remembered { get; } = [];
    public bool HasRemembered => Remembered.Count > 0;

    private void LoadCameras()
    {
        Profiles.Clear();
        foreach (var p in ProfileCatalog.Load(_ctx.Store.ProfilesDirectory))
            Profiles.Add(new ProfileRow(p.Name, Describe(p.Match), Format.Folder(_ctx.Settings.Routing.ByProfile.GetValueOrDefault(p.Id, p.DefaultFolder))));
        Remembered.Clear();
        foreach (var (card, folder) in _ctx.Settings.Routing.ByCard) Remembered.Add(new RememberedCard(card, folder, Forget));
        OnPropertyChanged(nameof(HasRemembered));
    }

    private static string Describe(ProfileMatch m)
    {
        var parts = new List<string>();
        if (m.MarkerPaths.Length > 0) parts.Add(string.Join(", ", m.MarkerPaths) + " folder");
        if (m.FilePrefixes.Length > 0) parts.Add(string.Join(", ", m.FilePrefixes) + " file names");
        if (m.DcimFolderContains.Length > 0) parts.Add("DCIM folders with " + string.Join("/", m.DcimFolderContains));
        if (m.AudioOnlyWithoutDcim) parts.Add("audio files, no DCIM folder");
        return parts.Count == 0 ? "card name" : string.Join("; ", parts);
    }

    private void Forget(string card)
    {
        _ctx.Settings.Routing.ByCard.Remove(card);
        _ctx.Save();
        LoadCameras();
        LoadFolders();
    }

    [RelayCommand] private void OpenProfilesFolder()
    {
        Directory.CreateDirectory(_ctx.Store.ProfilesDirectory);
        _ctx.Platform.OpenFolder(_ctx.Store.ProfilesDirectory);
    }

    // ---- File types ------------------------------------------------------------------------
    public ObservableCollection<ExtensionGroup> TypeGroups { get; } = [];
    public ObservableCollection<string> Kinds { get; } = ["Video", "Audio", "Photo", "Proxy"];
    [ObservableProperty] private string _newExtension = "";
    [ObservableProperty] private string _newKind;

    private void LoadTypes()
    {
        TypeGroups.Clear();
        var catalog = _ctx.Settings.BuildFileTypes();
        foreach (var g in catalog.Extensions.GroupBy(kv => kv.Value).OrderBy(g => g.Key))
            TypeGroups.Add(new ExtensionGroup(Format.Kinds([g.Key]), string.Join("  ", g.Select(kv => kv.Key.TrimStart('.').ToUpperInvariant()).Order())));
    }

    [RelayCommand]
    private void AddExtension()
    {
        var ext = NewExtension.Trim().TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0 || !Enum.TryParse<FileKind>(NewKind, out var kind)) return;
        _ctx.Settings.ExtraExtensions[ext] = kind;
        _ctx.Save();
        NewExtension = "";
        LoadTypes();
    }

    // ---- Navigation ------------------------------------------------------------------------
    [RelayCommand]
    private void Done()
    {
        _ctx.Monitor.Refresh();
        _ctx.Shell.GoHome();
    }
}
