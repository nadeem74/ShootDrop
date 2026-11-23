namespace ShootDrop.Core.Projects;

/// <summary>
/// The folders every new project gets (FR-17). Paths use '/' and are relative to the project,
/// e.g. "Video/Pocket 4P".
/// </summary>
public sealed record FolderTemplate(string Name, IReadOnlyList<string> Folders)
{
    /// <summary>Default, based on the folders Nadeem's original script copied into.</summary>
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
