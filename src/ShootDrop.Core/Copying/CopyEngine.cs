using System.Buffers;
using System.IO.Hashing;

namespace ShootDrop.Core.Copying;

public enum CopyOutcome
{
    /// <summary>Copied and verified by checksum.</summary>
    Copied,

    /// <summary>The same file (same size and checksum) was already in the project.</summary>
    SkippedIdentical,

    Failed,
}

public sealed record CopyFileResult(CopyItem Item, CopyOutcome Outcome, string FinalPath, ulong Checksum, string? Error = null);

public sealed record CopyProgress(int FileNumber, int FileCount, string CurrentFile, long BytesDone, long BytesTotal)
{
    public double Fraction => BytesTotal == 0 ? 1 : (double)BytesDone / BytesTotal;
}

/// <summary>
/// Copies a plan safely (ADR-003, ADR-004):
/// sources are only read; each file is written as .part, re-read and checked with xxHash64,
/// then renamed; nothing in the project is overwritten.
/// </summary>
public sealed class CopyEngine
{
    public int BufferSize { get; init; } = 4 * 1024 * 1024;

    public async Task<IReadOnlyList<CopyFileResult>> RunAsync(
        CopyPlan plan,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CopyFileResult>(plan.Items.Count);
        long bytesBefore = 0;
        var total = plan.TotalBytes;

        for (var i = 0; i < plan.Items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = plan.Items[i];
            var number = i + 1;
            var fileStart = bytesBefore;
            progress?.Report(new CopyProgress(number, plan.Items.Count, item.Source.Name, fileStart, total));

            results.Add(await CopyOneAsync(item, done =>
                progress?.Report(new CopyProgress(number, plan.Items.Count, item.Source.Name, fileStart + done, total)),
                cancellationToken).ConfigureAwait(false));

            bytesBefore += item.Source.Size;
            progress?.Report(new CopyProgress(number, plan.Items.Count, item.Source.Name, bytesBefore, total));
        }

        return results;
    }

    private async Task<CopyFileResult> CopyOneAsync(CopyItem item, Action<long> onBytes, CancellationToken ct)
    {
        var dest = item.Destination;
        string? part = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (File.Exists(dest))
            {
                var sourceHash = await HashFileAsync(item.Source.Path, ct).ConfigureAwait(false);
                if (new FileInfo(dest).Length == item.Source.Size
                    && await HashFileAsync(dest, ct).ConfigureAwait(false) == sourceHash)
                {
                    return new CopyFileResult(item, CopyOutcome.SkippedIdentical, dest, sourceHash);
                }
                dest = NextFreeName(dest);
            }

            part = dest + ".part";
            ulong writtenHash;
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                await using var src = new FileStream(item.Source.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var dst = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.Asynchronous);

                var hasher = new XxHash64();
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    hasher.Append(buffer.AsSpan(0, read));
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    onBytes(done);
                }

                await dst.FlushAsync(ct).ConfigureAwait(false);
                dst.Flush(flushToDisk: true);
                writtenHash = hasher.GetCurrentHashAsUInt64();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var verifyHash = await HashFileAsync(part, ct).ConfigureAwait(false);
            if (verifyHash != writtenHash)
            {
                throw new IOException("Checksum mismatch: the copy on disk differs from the card.");
            }

            File.Move(part, dest, overwrite: false);
            part = null;
            File.SetLastWriteTime(dest, item.Source.Modified);
            return new CopyFileResult(item, CopyOutcome.Copied, dest, writtenHash);
        }
        catch (OperationCanceledException)
        {
            TryDelete(part);
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(part);
            return new CopyFileResult(item, CopyOutcome.Failed, dest, 0, e.Message);
        }
    }

    public static async Task<ulong> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hasher = new XxHash64();
        await hasher.AppendAsync(stream, ct).ConfigureAwait(false);
        return hasher.GetCurrentHashAsUInt64();
    }

    internal static string NextFreeName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".part")) return candidate;
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
