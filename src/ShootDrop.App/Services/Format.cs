using System.Globalization;
using ShootDrop.Core.Media;

namespace ShootDrop.App.Services;

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1000 && i < units.Length - 1) { v /= 1000; i++; }
        return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[i];
    }

    public static string Files(int n) => n == 1 ? "1 file" : $"{n} files";

    public static string Kinds(IEnumerable<FileKind> kinds)
    {
        var names = kinds.Order().Select(k => k switch
        {
            FileKind.Video => "Video",
            FileKind.Audio => "Audio",
            FileKind.Photo => "Photos",
            FileKind.Proxy => "Proxies",
            _ => k.ToString(),
        }).ToList();
        return names.Count == 0 ? "No file types" : string.Join(" + ", names);
    }

    public static string Folder(string folder) => folder.Replace("/", " / ");

    public static string Duration(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{Math.Max(1, (int)t.TotalSeconds)} s"
        : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} min {t.Seconds} s"
        : $"{(int)t.TotalHours} h {t.Minutes} min";
}
