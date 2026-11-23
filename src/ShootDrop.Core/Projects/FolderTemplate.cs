namespace ShootDrop.Core.Projects;

/// <summary>
/// The folders every new project gets (FR-17). Paths use '/' and are relative to the project,
/// e.g. "Video/Pocket 4P".
/// </summary>
public sealed record FolderTemplate(string Name, IReadOnlyList<string> Folders)
{
    /// <summary>Default, based on the folders the original copy script used.</summary>
    public static FolderTemplate MyTemplate { get; } = new("My template",
        ["Audio/DJI Mic", "Video/Pocket 4P", "Video/Camera", "Video/iPhone", "Images"]);

    public static FolderTemplate ARollBRoll { get; } = new("A-roll / B-roll",
        ["Video/A-roll", "Video/B-roll", "Audio", "Images", "Exports"]);

    public static FolderTemplate OneFolder { get; } = new("One folder", ["Footage"]);

    /// <summary>Reads every subfolder of an existing template folder, e.g. "Project template".</summary>
    public static FolderTemplate FromDirectory(string directory)
    {
        var root = Path.GetFullPath(directory);
        var folders = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            })
            .Select(d => Path.GetRelativePath(root, d).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new FolderTemplate(Path.GetFileName(root), folders);
    }

    public bool Contains(string folder) =>
        Folders.Any(f => string.Equals(f.Trim('/'), folder.Trim('/'), StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the template with a folder added; parents are implied by the path.</summary>
    public FolderTemplate With(string folder) =>
        Contains(folder) ? this : this with { Folders = [.. Folders, folder.Trim('/')] };

    /// <summary>Removes a folder and everything inside it.</summary>
    public FolderTemplate Without(string folder)
    {
        var f = folder.Trim('/');
        return this with
        {
            Folders = Folders.Where(x =>
                !string.Equals(x, f, StringComparison.OrdinalIgnoreCase)
                && !x.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase)).ToList(),
        };
    }

    /// <summary>
    /// Moves a folder (and everything inside it) into <paramref name="newParent"/>; null or "" means the top
    /// level. Used by drag and drop in the folder editor. Throws <see cref="InvalidOperationException"/> with a
    /// plain-English reason when the move isn't possible.
    /// </summary>
    public FolderTemplate Move(string folder, string? newParent, out string newPath)
    {
        var from = folder.Trim('/');
        var parent = (newParent ?? "").Trim('/');
        var name = from.Split('/')[^1];
        var to = parent.Length == 0 ? name : parent + "/" + name;
        newPath = to;

        if (from.Length == 0) throw new InvalidOperationException("Pick a folder to move.");
        if (IsSameOrInside(parent, from)) throw new InvalidOperationException($"“{name}” can't go inside itself.");
        if (string.Equals(to, from, StringComparison.OrdinalIgnoreCase)) return this; // dropped where it already is
        if (AllFolders().Any(f => string.Equals(f, to, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"There is already a “{name}” folder there.");

        return this with { Folders = Folders.Select(f => Rebase(f, from, to)).ToList() };
    }

    /// <summary>"Video/Camera" with "Video" moved to "Footage/Video" becomes "Footage/Video/Camera"; other paths are unchanged.</summary>
    public static string Rebase(string path, string from, string to)
    {
        var p = path.Trim('/');
        if (string.Equals(p, from, StringComparison.OrdinalIgnoreCase)) return to;
        return p.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase) ? to + p[from.Length..] : p;
    }

    /// <summary>Every folder in the project, including parents that are only implied by a deeper path.</summary>
    public IReadOnlyList<string> AllFolders() =>
        Folders.SelectMany(f =>
            {
                var parts = f.Trim('/').Split('/');
                return Enumerable.Range(1, parts.Length).Select(i => string.Join('/', parts[..i]));
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
}

public static class ProjectCreator
{
    /// <summary>
    /// Creates the project folder. If <paramref name="templateDirectory"/> is given, it is copied
    /// (folders and files, like the old script's cp -R); otherwise the template's folders are created.
    /// Existing files are never overwritten.
    /// </summary>
    public static void Create(string projectRoot, FolderTemplate template, string? templateDirectory = null)
    {
        Directory.CreateDirectory(projectRoot);

        if (templateDirectory is not null && Directory.Exists(templateDirectory))
        {
            CopyDirectoryNoOverwrite(templateDirectory, projectRoot);
        }

        foreach (var folder in template.Folders)
        {
            Directory.CreateDirectory(ToPath(projectRoot, folder));
        }
    }

    public static string ToPath(string projectRoot, string folder) =>
        Path.Combine(projectRoot, folder.Trim('/').Replace('/', Path.DirectorySeparatorChar));

    private static void CopyDirectoryNoOverwrite(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(to, Path.GetRelativePath(from, file));
            if (!File.Exists(dest))
            {
                File.Copy(file, dest, overwrite: false);
            }
        }
    }
}
