using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;

namespace ShootDrop.Core.Tests;

/// <summary>Resuming a copy cut short by a pulled card, a power cut or the app closing.</summary>
public class ResumeTests
{
    private static (ShootDropService Service, AppSettings Settings, FakeCard Pocket, CopyJournal Journal) Arrange(TempDir tmp, int files = 5)
    {
        var pocket = new FakeCard(tmp, "POCKET");
        for (var i = 1; i <= files; i++) pocket.Add($"DCIM/DJI_001/DJI_{i:0000}.MP4", Days.At(23).AddMinutes(i * 10), bytes: 200_000 + i);
        var settings = new AppSettings { ProjectsRoot = Path.Combine(tmp.Path, "Projects") };
        var service = new ShootDropService(new FolderVolumeProvider(pocket.Root), ProfileCatalog.BuiltIn(), new FixedClock());
        return (service, settings, pocket, CopyJournal.InDirectory(Path.Combine(tmp.Path, "app")));
    }

    private static async Task CopyUntilFileNumber(ShootDropService service, CopyPreview preview, AppSettings settings, CopyJournal journal, int stopAt)
    {
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.FileNumber == stopAt) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(preview, settings, progress, cts.Token, journal));
    }

    [Fact]
    public async Task An_interrupted_copy_is_remembered_and_resumes_where_it_stopped()
    {
        using var tmp = new TempDir();
        var (service, settings, pocket, journal) = Arrange(tmp);
        var before = pocket.Fingerprint();
        var preview = service.Preview(CopyRequest.FromSetup(Settings.Setup.ShootDay, "Fort"), settings);

        await CopyUntilFileNumber(service, preview, settings, journal, stopAt: 3); // 2 files finished

        var unfinished = journal.Load()!;
        Assert.Equal(5, unfinished.Total);
        Assert.Equal(2, unfinished.DoneCount);
        Assert.Equal(new[] { "Fort" }, unfinished.ProjectNames);

        // A power cut also leaves a half-written .part behind.
        var third = unfinished.Remaining[0].Destination;
        await File.WriteAllTextAsync(third + ".part", "half a file");

        var resume = service.PrepareResume(unfinished, settings);
        Assert.Equal(3, resume.Preview.Plan.Items.Count);
        Assert.Equal(2, resume.AlreadyDone);
        Assert.Equal(0, resume.MissingSources);

        var result = await service.RunAsync(resume.Preview, settings, journal: journal);
        Assert.Equal(5, result.Files.Count);          // the whole copy, not just the rest
        Assert.Equal(5, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.False(journal.Exists);                 // finished: nothing left to resume
        Assert.False(File.Exists(third + ".part"));
        var dir = Path.Combine(settings.ProjectsRoot, "Fort", "Video", "Pocket 4P");
        Assert.Equal(5, Directory.GetFiles(dir, "*.MP4").Length);
        Assert.Empty(Directory.GetFiles(dir, "*.part"));
        Assert.Equal(5, (await File.ReadAllLinesAsync(Path.Combine(settings.ProjectsRoot, "Fort", CopyReport.FileName))).Count(l => l.Contains(".MP4")));
        Assert.Equal(before, pocket.Fingerprint());   // card untouched
    }

    [Fact]
    public async Task A_completed_copy_leaves_no_journal()
    {
        using var tmp = new TempDir();
        var (service, settings, _, journal) = Arrange(tmp, files: 2);
        var preview = service.Preview(CopyRequest.FromSetup(Settings.Setup.ShootDay, "Fort"), settings);

        await service.RunAsync(preview, settings, journal: journal);

        Assert.False(journal.Exists);
        Assert.Null(journal.Load());
    }

    [Fact]
    public async Task Files_whose_card_is_unplugged_wait_for_the_card()
    {
        using var tmp = new TempDir();
        var (service, settings, pocket, journal) = Arrange(tmp);
        var preview = service.Preview(CopyRequest.FromSetup(Settings.Setup.ShootDay, "Fort"), settings);
        await CopyUntilFileNumber(service, preview, settings, journal, stopAt: 2);

        Directory.Move(pocket.Root, pocket.Root + "-unplugged");
        var resume = service.PrepareResume(journal.Load()!, settings);
        Assert.Empty(resume.Preview.Plan.Items);
        Assert.Equal(4, resume.MissingSources);
        Assert.Equal(new[] { "POCKET" }, resume.MissingCards);

        Directory.Move(pocket.Root + "-unplugged", pocket.Root);
        resume = service.PrepareResume(journal.Load()!, settings);
        Assert.Equal(4, resume.Preview.Plan.Items.Count);
        var result = await service.RunAsync(resume.Preview, settings, journal: journal);
        Assert.Equal(5, result.Copied);
        Assert.False(journal.Exists);
    }

    [Fact]
    public async Task A_split_copy_resumes_into_the_same_projects()
    {
        using var tmp = new TempDir();
        var (service, settings, _, journal) = Arrange(tmp, files: 4);
        var splits = new[]
        {
            new ProjectSplit("Fort", new TimeWindow(null, new TimeOnly(12, 25))),
            new ProjectSplit("Cafe", new TimeWindow(new TimeOnly(12, 26), null)),
        };
        var preview = service.Preview(CopyRequest.FromSetup(Settings.Setup.ShootDay, "Day") with { Splits = splits }, settings);
        Assert.Equal(new[] { 2, 2 }, preview.Plan.AllParts.Select(p => p.Items.Count));
        await CopyUntilFileNumber(service, preview, settings, journal, stopAt: 2);

        var resume = service.PrepareResume(journal.Load()!, settings);
        Assert.True(resume.Preview.Plan.IsSplit);
        var result = await service.RunAsync(resume.Preview, settings, journal: journal);

        Assert.Equal(4, result.Copied);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(settings.ProjectsRoot, "Fort", "Video", "Pocket 4P")).Length);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(settings.ProjectsRoot, "Cafe", "Video", "Pocket 4P")).Length);
        Assert.True(File.Exists(Path.Combine(settings.ProjectsRoot, "Cafe", CopyReport.FileName)));
        Assert.False(journal.Exists);
    }

    [Fact]
    public async Task A_line_cut_off_by_a_power_cut_is_ignored()
    {
        using var tmp = new TempDir();
        var (service, settings, _, journal) = Arrange(tmp);
        var preview = service.Preview(CopyRequest.FromSetup(Settings.Setup.ShootDay, "Fort"), settings);
        await CopyUntilFileNumber(service, preview, settings, journal, stopAt: 3);

        await File.AppendAllTextAsync(journal.Path, "{\"destination\":\"/half"); // no end: power went out mid-line
        var unfinished = journal.Load()!;
        Assert.Equal(2, unfinished.DoneCount);
    }

    /// <summary>Progress that reports on the calling thread, so the test can cancel at an exact file.</summary>
    private sealed class SyncProgress(Action<CopyProgress> report) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => report(value);
    }
}
