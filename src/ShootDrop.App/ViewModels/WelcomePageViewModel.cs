using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShootDrop.Core.Projects;

namespace ShootDrop.App.ViewModels;

/// <summary>First run: where projects go, which folder structure, optional AI (v0.2).</summary>
public sealed partial class WelcomePageViewModel : PageViewModel
{
    private const string OldScriptRoot = "/Volumes/SSD 1TB/SSD Video Projects";
    private readonly ShootDropContext _ctx;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetStartedCommand))]
    private string _projectsRoot;

    [ObservableProperty] private int _structure; // 0 mine, 1 A/B roll, 2 one folder, 3 imported
    [ObservableProperty] private string _importedName = "";

    private FolderTemplate? _imported;
    private string? _importedDir;

    public WelcomePageViewModel(ShootDropContext ctx)
    {
        _ctx = ctx;
        _projectsRoot = ctx.Settings.ProjectsRoot.Length > 0 ? ctx.Settings.ProjectsRoot
            : Directory.Exists(OldScriptRoot) ? OldScriptRoot : "";
    }

    public bool IsMine { get => Structure == 0; set { if (value) Structure = 0; } }
    public bool IsRoll { get => Structure == 1; set { if (value) Structure = 1; } }
    public bool IsOne { get => Structure == 2; set { if (value) Structure = 2; } }
    public bool IsImported { get => Structure == 3; set { if (value && _imported is not null) Structure = 3; } }
    public bool HasImported => _imported is not null;

    partial void OnStructureChanged(int value)
    {
        OnPropertyChanged(nameof(IsMine));
        OnPropertyChanged(nameof(IsRoll));
        OnPropertyChanged(nameof(IsOne));
        OnPropertyChanged(nameof(IsImported));
    }

    [RelayCommand]
    private async Task ChooseRootAsync()
    {
        var path = await _ctx.Platform.PickFolderAsync("Where should projects go?");
        if (path is not null) ProjectsRoot = path;
    }

    [RelayCommand]
    private async Task ImportTemplateAsync()
    {
        var path = await _ctx.Platform.PickFolderAsync("Choose your project template folder");
        if (path is null) return;
        _imported = FolderTemplate.FromDirectory(path);
        _importedDir = path;
        ImportedName = $"{Path.GetFileName(path)} ({_imported.Folders.Count} folders)";
        OnPropertyChanged(nameof(HasImported));
        Structure = 3;
    }

    private bool CanGetStarted() => !string.IsNullOrWhiteSpace(ProjectsRoot);

    [RelayCommand(CanExecute = nameof(CanGetStarted))]
    private void GetStarted()
    {
        var s = _ctx.Settings;
        s.ProjectsRoot = ProjectsRoot.Trim();
        s.TemplateDirectory = Structure == 3 ? _importedDir : null;
        s.Template = Structure switch
        {
            1 => FolderTemplate.ARollBRoll,
            2 => FolderTemplate.OneFolder,
            3 => _imported ?? FolderTemplate.MyTemplate,
            _ => FolderTemplate.MyTemplate,
        };
        if (Structure == 1)
        {
            s.Routing.ByProfile["dji-osmo-pocket"] = "Video/A-roll";
            s.Routing.ByProfile["panasonic-lumix"] = "Video/B-roll";
            s.Routing.ByProfile["dji-mic"] = "Audio";
        }
        else if (Structure == 2)
        {
            foreach (var id in new[] { "dji-osmo-pocket", "panasonic-lumix", "dji-mic" }) s.Routing.ByProfile[id] = "Footage";
            s.Routing.ByKind[Core.Media.FileKind.Photo] = "Footage";
        }
        _ctx.Save();
        _ctx.Monitor.Refresh();
        _ctx.Shell.GoReady();
    }
}
