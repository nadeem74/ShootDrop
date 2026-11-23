namespace ShootDrop.Core.Media;

/// <summary>The groups a user can tick under "File types".</summary>
public enum FileKind
{
    Video,
    Audio,
    Photo,
    Proxy,
}

/// <summary>Maps file extensions to a <see cref="FileKind"/>. Unknown extensions are ignored.</summary>
public sealed class FileTypeCatalog
{
    private readonly Dictionary<string, FileKind> _map;

    public FileTypeCatalog(IEnumerable<KeyValuePair<string, FileKind>> map)
    {
        _map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ext, kind) in map)
        {
            _map[Normalize(ext)] = kind;
        }
    }

    public static FileTypeCatalog Default { get; } = new(new Dictionary<string, FileKind>
    {
        [".mp4"] = FileKind.Video,
        [".mov"] = FileKind.Video,
        [".mxf"] = FileKind.Video,
        [".mts"] = FileKind.Video,
        [".m2ts"] = FileKind.Video,
        [".avi"] = FileKind.Video,
        [".wav"] = FileKind.Audio,
        [".mp3"] = FileKind.Audio,
        [".m4a"] = FileKind.Audio,
        [".aac"] = FileKind.Audio,
        [".flac"] = FileKind.Audio,
        [".jpg"] = FileKind.Photo,
        [".jpeg"] = FileKind.Photo,
        [".heic"] = FileKind.Photo,
        [".heif"] = FileKind.Photo,
        [".dng"] = FileKind.Photo,
        [".rw2"] = FileKind.Photo,
        [".arw"] = FileKind.Photo,
        [".cr3"] = FileKind.Photo,
        [".nef"] = FileKind.Photo,
        [".raf"] = FileKind.Photo,
        [".lrf"] = FileKind.Proxy,
    });

    public IReadOnlyDictionary<string, FileKind> Extensions => _map;

    public FileKind? KindOf(string path)
    {
        var ext = Path.GetExtension(path);
        return _map.TryGetValue(ext, out var kind) ? kind : null;
    }

    /// <summary>Returns a copy with an extra extension, e.g. a user's "BRAW" as Video.</summary>
    public FileTypeCatalog With(string extension, FileKind kind)
    {
        var copy = new Dictionary<string, FileKind>(_map, StringComparer.OrdinalIgnoreCase)
        {
            [Normalize(extension)] = kind,
        };
        return new FileTypeCatalog(copy);
    }

    private static string Normalize(string ext) => ext.StartsWith('.') ? ext : "." + ext;
}
