using System.Text.Json;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Copying;

/// <summary>One file of a journalled copy: where it comes from and where it goes.</summary>
public sealed record JournalItem(
    string Source, long Size, DateTime Modified, FileKind Kind,
    string Destination, string CardName, string Folder, string ProjectRoot)
{
    public CopyItem ToItem() => new(new SourceFile(Source, Size, Modified, Kind), Destination, CardName, Folder);
}

public sealed record JournalPart(string Name, string ProjectRoot, TimeWindow? Window);

/// <summary>What the copy was: enough to show it, plan the rest again and write complete reports.</summary>
public sealed record JournalHeader(
    int Version, DateTime Started, string ProjectName, Setup Setup, IReadOnlyList<ProjectSplit>? Splits,
    IReadOnlyList<JournalPart> Parts, IReadOnlyList<JournalItem> Items);

/// <summary>A file that finished (copied and verified, or already there).</summary>
public sealed record JournalDone(string Destination, string FinalPath, ulong Checksum, CopyOutcome Outcome);

/// <summary>A copy that didn't finish, read back from the journal.</summary>
public sealed record UnfinishedCopy(JournalHeader Header, IReadOnlyList<JournalDone> Done)
{
    private Dictionary<string, JournalDone>? _doneByDestination;

    private Dictionary<string, JournalDone> DoneByDestination => _doneByDestination ??=
        Done.GroupBy(d => d.Destination, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    public DateTime Started => Header.Started;
    public int Total => Header.Items.Count;

    /// <summary>Files that finished and are still in the project.</summary>
    public int DoneCount => Header.Items.Count(i => IsDone(i));

    public IReadOnlyList<string> ProjectNames => Header.Parts.Select(p => p.Name).ToList();

    /// <summary>Files still to copy: not finished, or finished but no longer in the project.</summary>
    public IReadOnlyList<JournalItem> Remaining => Header.Items.Where(i => !IsDone(i)).ToList();

    /// <summary>The finished files as copy results, so reports list every file of the whole copy.</summary>
    public IReadOnlyList<CopyFileResult> DoneResults
    {
        get
        {
            var done = DoneByDestination;
            return Header.Items.Where(IsDone)
                .Select(i => new CopyFileResult(i.ToItem(), done[i.Destination].Outcome, done[i.Destination].FinalPath, done[i.Destination].Checksum))
                .ToList();
        }
    }

    private bool IsDone(JournalItem item) =>
        DoneByDestination.TryGetValue(item.Destination, out var d) && File.Exists(d.FinalPath);
}

/// <summary>
/// Remembers a copy while it runs, so a copy cut short (card pulled out, power cut, app closed) can be resumed.
/// One file: the first line describes the copy, then one line per finished file, appended and flushed as each
/// file finishes. A line cut off by a power cut is ignored. The file is removed when the copy completes.
/// </summary>
public sealed class CopyJournal(string path)
{
    public const string FileName = "unfinished-copy.jsonl";
    private const int Version = 1;

    public string Path { get; } = path;

    public static CopyJournal InDirectory(string directory) => new(System.IO.Path.Combine(directory, FileName));

    public bool Exists => File.Exists(Path);

    /// <summary>The unfinished copy, or null when there is none or the journal can't be read.</summary>
    public UnfinishedCopy? Load()
    {
        if (!File.Exists(Path)) return null;
        try
        {
            var lines = File.ReadAllLines(Path);
            if (lines.Length == 0) return null;
            var header = JsonSerializer.Deserialize<JournalHeader>(lines[0], Json.Options);
            if (header is null || header.Version != Version || header.Items.Count == 0) return null;
            var done = new List<JournalDone>();
            foreach (var line in lines.Skip(1))
            {
                try
                {
                    if (JsonSerializer.Deserialize<JournalDone>(line, Json.Options) is { } d) done.Add(d);
                }
                catch (JsonException)
                {
                    // the last line can be cut off by a power cut; that file is simply copied again
                }
            }
            return new UnfinishedCopy(header, done);
        }
        catch (Exception e) when (e is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Starts a journal for a new copy (replacing any old one).</summary>
    internal void Begin(CopyPreview preview, DateTime when)
    {
        var plan = preview.Plan;
        var parts = plan.AllParts.Where(p => p.Items.Count > 0).ToList();
        var items = parts.SelectMany(p => p.Items.Select(i => new JournalItem(
            i.Source.Path, i.Source.Size, i.Source.Modified, i.Source.Kind, i.Destination, i.CardName, i.Folder, p.ProjectRoot))).ToList();
        var header = new JournalHeader(Version, when, preview.Request.ProjectName, preview.Request.ToSetup("Unfinished copy"),
            preview.Request.Splits, parts.Select(p => new JournalPart(p.Name, p.ProjectRoot, p.Window)).ToList(), items);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(header, Json.Options).ReplaceLineEndings(" ") + "\n");
        File.Move(tmp, Path, overwrite: true);
    }

    /// <summary>Notes a finished file. Failed files are not noted, so a resume copies them again.</summary>
    internal void Record(CopyFileResult result)
    {
        if (result.Outcome == CopyOutcome.Failed) return;
        var line = JsonSerializer.Serialize(new JournalDone(result.Item.Destination, result.FinalPath, result.Checksum, result.Outcome), Json.Options)
            .ReplaceLineEndings(" ");
        using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 4096, leaveOpen: true))
        {
            writer.Write(line + "\n");
        }
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Forgets the unfinished copy. Files already copied stay in the project.</summary>
    public void Discard()
    {
        try { File.Delete(Path); } catch (IOException) { }
    }
}
