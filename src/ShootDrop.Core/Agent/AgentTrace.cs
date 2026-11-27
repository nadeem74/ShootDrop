using System.Globalization;

namespace ShootDrop.Core.Agent;

/// <summary>
/// Observability (Phase 4): the last naming run, written as plain text next to the settings
/// (<c>last-naming.txt</c>) so the person can read why each session got its name.
/// </summary>
public static class AgentTrace
{
    public const string FileName = "last-naming.txt";

    public static string Format(NamingResult result, DateTime when) =>
        $"ShootDrop naming, {when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
        string.Join(Environment.NewLine, result.Trace.Select(t => "- " + t)) + Environment.NewLine;

    public static void Save(string directory, NamingResult result)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FileName), Format(result, DateTime.Now));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A trace is nice to have; never fail a copy over it.
        }
    }
}
