using System.Globalization;
using System.Text;

namespace ShootDrop.Core.Copying;

/// <summary>Writes copy-report.txt inside the project (FR-14). Each run adds a section.</summary>
public static class CopyReport
{
    public const string FileName = "copy-report.txt";

    public static string Write(string projectRoot, IReadOnlyList<CopyFileResult> results, DateTime when)
    {
        var path = Path.Combine(projectRoot, FileName);
        var sb = new StringBuilder();
        sb.AppendLine($"# ShootDrop copy, {when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"# copied={results.Count(r => r.Outcome == CopyOutcome.Copied)} " +
                      $"skipped={results.Count(r => r.Outcome == CopyOutcome.SkippedIdentical)} " +
                      $"failed={results.Count(r => r.Outcome == CopyOutcome.Failed)}");
        sb.AppendLine("outcome\tcard\tsource\tdestination\tbytes\txxhash64\terror");
        foreach (var r in results)
        {
            sb.Append(r.Outcome).Append('\t')
              .Append(r.Item.CardName).Append('\t')
              .Append(r.Item.Source.Path).Append('\t')
              .Append(Path.GetRelativePath(projectRoot, r.FinalPath)).Append('\t')
              .Append(r.Item.Source.Size.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(r.Checksum.ToString("x16", CultureInfo.InvariantCulture)).Append('\t')
              .AppendLine(r.Error ?? "");
        }
        sb.AppendLine();
        File.AppendAllText(path, sb.ToString());
        return path;
    }
}
