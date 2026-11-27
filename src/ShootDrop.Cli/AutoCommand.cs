using ShootDrop.Core;
using ShootDrop.Core.Agent;
using ShootDrop.Core.Autopilot;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Settings;

/// <summary><c>shootdrop auto</c>: the autopilot (FR-26) in a terminal. Same Core flow as the app.</summary>
static class AutoCommand
{
    public static async Task<int> RunAsync(ShootDropService service, AppSettings settings, SettingsStore store, string? note, bool yes, bool useAgent)
    {
        var auto = new AutoCopy(service);
        var proposal = auto.Propose(settings);
        if (!proposal.HasWork)
        {
            Console.WriteLine("No new recordings on the plugged-in cards.");
            return 0;
        }

        if (note is not null) settings.ShootNote = note;
        var facts = NamingFacts.For(proposal, settings.ShootNote, settings.ProjectsRoot);
        var naming = SessionNamer.FromNote(facts);
        if (useAgent)
        {
            var key = AnthropicModel.FindKey(Path.GetDirectoryName(store.Path)!);
            if (key is null) Console.WriteLine("(--agent: no ANTHROPIC_API_KEY found, using the note rules only)");
            else naming = await new NamingAgent(new AnthropicModel(key, settings.AgentModel)).RunAsync(facts);
        }

        Console.WriteLine($"Found {proposal.Sessions.Count} session(s) on {string.Join(" + ", proposal.Cards.Select(c => c.Name))}:");
        var names = new List<string?>();
        for (var i = 0; i < proposal.Sessions.Count; i++)
        {
            var s = proposal.Sessions[i];
            var name = naming.NameFor(i) ?? s.DefaultName;
            names.Add(name);
            Console.WriteLine($"  {i + 1}. {AutoCopy.Describe(s)}  →  {name}");
        }
        foreach (var line in naming.Trace) Console.WriteLine("     · " + line);

        var storage = StorageCheck.Evaluate(settings.ProjectsRoot, proposal.TotalBytes, StorageCheck.FreeBytes, Directory.Exists);
        Console.WriteLine(storage.Message);
        if (storage.Blocks) return 1;

        var preview = auto.Preview(proposal, names, new HashSet<int>(), settings);
        if (!preview.Plan.CanRun)
        {
            Console.Error.WriteLine("Some cards have no folder yet: " + string.Join(", ", preview.Plan.Unrouted.Select(c => c.Name)) + ". Set them up in the app first.");
            return 1;
        }
        if (!yes)
        {
            Console.Write("Start copying? Cards are only read, nothing is overwritten. [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) return 0;
        }

        var result = await service.RunAsync(preview, settings, new Progress<CopyProgress>(_ => { }));
        AutoCopy.Remember(settings, result.Files);
        store.Save(settings);
        Console.WriteLine($"Copied {result.Copied}, already there {result.Skipped}, failed {result.Failed}.");
        foreach (var r in result.ReportPaths ?? [result.ReportPath]) Console.WriteLine("Report: " + r);
        return result.Failed == 0 ? 0 : 1;
    }
}
