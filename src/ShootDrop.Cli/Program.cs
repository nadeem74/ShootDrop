using System.Globalization;
using ShootDrop.Core;
using ShootDrop.Core.Cards;
using ShootDrop.Core.Copying;
using ShootDrop.Core.Media;
using ShootDrop.Core.Settings;
using ShootDrop.Core.SmartBox;

// shootdrop — command-line front end on ShootDrop.Core (ADR-003).
// Usage: see PrintHelp() or run `shootdrop help`.

var opts = Options.Parse(args);
if (opts.Command is "help" or "" || opts.Has("help"))
{
    PrintHelp();
    return 0;
}

var store = opts.Get("settings") is { } settingsPath ? new SettingsStore(settingsPath) : SettingsStore.Default();
var settings = store.Load();
if (opts.Get("root") is { } root) settings.ProjectsRoot = root;
if (opts.Get("template-dir") is { } tdir) settings.TemplateDirectory = tdir;

var profiles = ProfileCatalog.Load(store.ProfilesDirectory);
IVolumeProvider volumes = opts.GetAll("source") is { Count: > 0 } sources
    ? new FolderVolumeProvider(sources.ToArray())
    : new SystemVolumeProvider();
var service = new ShootDropService(volumes, profiles);

try
{
    switch (opts.Command)
    {
        case "cards":
            return Cards();
        case "ask":
            return Ask(string.Join(' ', opts.Positional));
        case "setups":
            return Setups();
        case "plan":
            return Plan(run: false).GetAwaiter().GetResult();
        case "copy":
            return Plan(run: true).GetAwaiter().GetResult();
        case "auto":
            return AutoCommand.RunAsync(service, settings, store, opts.Get("note"), opts.Has("yes"), opts.Has("agent")).GetAwaiter().GetResult();
        case "mcp":
            return McpServer.RunAsync(service, settings, store).GetAwaiter().GetResult();
        case "config":
            store.Save(settings);
            Console.WriteLine($"Saved settings to {store.Path}");
            Console.WriteLine($"  projects root: {Display(settings.ProjectsRoot)}");
            Console.WriteLine($"  template dir:  {Display(settings.TemplateDirectory)}");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command '{opts.Command}'. Run `shootdrop help`.");
            return 2;
    }
}
catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

int Cards()
{
    var cards = service.DetectCards(settings);
    if (cards.Count == 0)
    {
        Console.WriteLine("No cards found. Insert a card, or pass --source <folder>.");
        return 0;
    }
    foreach (var c in cards)
    {
        Console.WriteLine($"{c.Name,-20} {(c.Profile?.Name ?? "Unknown device"),-28} {c.Volume.Path}");
    }
    return 0;
}

int Ask(string text)
{
    var cards = service.DetectCards(settings);
    var parsed = RuleParser.Parse(text, service.Today, KnownCards(cards));
    if (!parsed.UnderstoodAnything)
    {
        Console.WriteLine("Didn't find a date, card, file type or project name in that.");
        return 1;
    }
    Console.WriteLine($"date:    {parsed.Date?.Describe(service.Today) ?? "(unchanged)"}");
    Console.WriteLine($"cards:   {(parsed.AllCards ? "all" : parsed.CardKeys is null ? "(unchanged)" : string.Join(", ", parsed.CardKeys))}");
    Console.WriteLine($"types:   {(parsed.Kinds is null ? "(unchanged)" : string.Join(" + ", parsed.Kinds.Order()))}");
    Console.WriteLine($"project: {parsed.ProjectName ?? "(unchanged)"}");
    return 0;
}

int Setups()
{
    Console.WriteLine($"Last used: {Describe(settings.CurrentSetup)}");
    foreach (var s in settings.Setups)
    {
        Console.WriteLine($"  {s.Name,-22} {Describe(s)}");
    }
    return 0;
}

async Task<int> Plan(bool run)
{
    var request = BuildRequest();
    var preview = service.Preview(request, settings);
    var plan = preview.Plan;

    Console.WriteLine($"Project: {plan.ProjectRoot}");
    Console.WriteLine($"Dates:   {request.Date.Describe(service.Today)}");
    Console.WriteLine($"Types:   {string.Join(" + ", request.Kinds.Order())}");
    foreach (var group in plan.Items.GroupBy(i => (i.CardName, i.Folder)))
    {
        var bytes = group.Sum(i => i.Source.Size);
        Console.WriteLine($"  {group.Key.CardName,-18} -> {group.Key.Folder,-22} {group.Count(),5} files  {Human(bytes),10}");
    }
    foreach (var c in plan.Unrouted)
    {
        Console.WriteLine($"  {c.Name,-18} -> ?  Unknown device. Route it with: --route \"{c.CardId}=Video/Camera\"");
    }
    Console.WriteLine($"Total: {plan.Items.Count} files, {Human(plan.TotalBytes)}");

    if (!run) return 0;
    if (!plan.CanRun)
    {
        Console.Error.WriteLine(plan.Items.Count == 0 ? "Nothing to copy." : "Some cards have no destination folder yet (see above).");
        return 1;
    }
    if (!opts.Has("yes"))
    {
        Console.Write("Copy now? Cards are only read, nothing is overwritten. [y/N] ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) return 0;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\nStopping after the current file…");
    };

    var progress = new Progress<CopyProgress>(p =>
        Console.Write($"\r{p.Fraction,7:P0}  {Human(p.BytesDone)} / {Human(p.BytesTotal)}  file {p.FileNumber}/{p.FileCount}  {Trim(p.CurrentFile, 36),-36}"));

    var started = DateTime.Now;
    try
    {
        var result = await service.RunAsync(preview, settings, progress, cts.Token);
        store.Save(settings); // remembers "last used" (FR-20)
        Console.WriteLine();
        Console.WriteLine($"Done in {(DateTime.Now - started).TotalSeconds:0}s: {result.Copied} copied and verified, " +
                          $"{result.Skipped} already there, {result.Failed} failed.");
        foreach (var f in result.Files.Where(f => f.Outcome == CopyOutcome.Failed))
        {
            Console.WriteLine($"  FAILED {f.Item.Source.Path}: {f.Error}");
        }
        Console.WriteLine($"Report: {result.ReportPath}");
        return result.Failed > 0 ? 1 : 0;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\nCancelled. Finished files are kept; run again to copy the rest.");
        return 130;
    }
}

CopyRequest BuildRequest()
{
    var baseSetup = opts.Get("setup") is { } name
        ? settings.FindSetup(name) ?? throw new ArgumentException($"No setup called '{name}'. Run `shootdrop setups`.")
        : settings.CurrentSetup;

    var date = opts.Get("date") is { } d ? DateRule.Parse(d) : baseSetup.Date;
    IReadOnlySet<FileKind> kinds = opts.Get("types") is { } t ? ParseKinds(t) : baseSetup.Kinds.ToHashSet();
    var cardKeys = opts.Get("cards") is { } c
        ? (c.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        : baseSetup.CardKeys;
    var project = opts.Get("name") ?? ShootDropService.DefaultProjectName(service.Today);

    foreach (var route in opts.GetAll("route"))
    {
        var parts = route.Split('=', 2);
        if (parts.Length != 2) throw new ArgumentException($"--route expects CARD=Folder, got '{route}'.");
        settings.Routing.ByCard[parts[0].Trim()] = parts[1].Trim();
    }

    return new CopyRequest(project, date, kinds, cardKeys);
}

static HashSet<FileKind> ParseKinds(string text) =>
    text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(k => Enum.TryParse<FileKind>(k, ignoreCase: true, out var kind)
            ? kind
            : throw new ArgumentException($"Unknown type '{k}'. Use video, audio, photo, proxy."))
        .ToHashSet();

static IReadOnlyList<KnownCard> KnownCards(IReadOnlyList<DetectedCard> cards) =>
    cards.Select(c => new KnownCard(c.Profile?.Id ?? c.CardId, Aliases(c))).ToList();

static IReadOnlyList<string> Aliases(DetectedCard c)
{
    var list = new List<string> { c.Name };
    switch (c.Profile?.Id)
    {
        case "dji-osmo-pocket": list.AddRange(["pocket", "osmo"]); break;
        case "panasonic-lumix": list.AddRange(["lumix", "camera", "s9"]); break;
        case "dji-mic": list.AddRange(["mic", "dji mic", "recorder"]); break;
    }
    return list;
}

string Describe(Setup s) =>
    $"{(s.CardKeys is null ? "all cards" : string.Join(", ", s.CardKeys))} · {s.Date.Describe(service.Today)} · {string.Join(" + ", s.Kinds)}";

static string Human(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double v = bytes;
    var i = 0;
    while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
    return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[i];
}

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static string Display(string? s) => string.IsNullOrEmpty(s) ? "(not set)" : s;

static void PrintHelp() => Console.WriteLine("""
    shootdrop: copy footage from camera cards into a project folder, verified.

    Commands
      cards                      List plugged-in cards and what device each is
      plan  [options]            Show what would be copied, copy nothing
      copy  [options]            Copy and verify (asks first unless --yes)
      ask   "all cards, yesterday for Sinhagad trip"   Test the smart box rules
      setups                     List saved setups and the last one used
      config --root <dir> [--template-dir <dir>]       Save where projects go
      auto  [--note "..."] [--agent] [--yes]           Autopilot: new sessions, dated names, one question, copy
      mcp                        Offer ShootDrop's tools to AI apps over MCP (stdio)

    Options
      --name <project>           Project name (default: today's date, e.g. "23 Nov 2026")
      --date <rule>              today | yesterday | last3 | 2026-11-22 | 2026-11-21..2026-11-22
      --types <list>             video,audio,photo,proxy
      --cards <list|all>         Profile ids or card names, e.g. dji-osmo-pocket,dji-mic
      --setup <name|last>        Start from a saved setup (default: last used)
      --route "CARD=Folder"      Send an unknown card to a project folder (repeatable)
      --source <folder>          Treat a folder as a card instead of real cards (repeatable)
      --root <dir>               Projects folder for this run
      --settings <file>          Use another settings.json
      --yes                      Don't ask before copying
      --note "..."               auto: what you shot, e.g. "morning Sinhagad Fort, evening Cafe review"
      --agent                    auto: let Claude suggest names (needs ANTHROPIC_API_KEY)

    Cards are only ever read. Nothing in a project is overwritten.
    """);

/// <summary>Tiny argument parser: first word is the command, --key value pairs, --flag, rest positional.</summary>
sealed class Options
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    public string Command { get; private set; } = "";
    public List<string> Positional { get; } = [];

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "yes", "help", "agent" };

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                var key = a[2..];
                string value = "true";
                if (!Flags.Contains(key))
                {
                    if (i + 1 >= args.Length) throw new ArgumentException($"--{key} needs a value.");
                    value = args[++i];
                }
                if (!o._values.TryGetValue(key, out var list)) o._values[key] = list = [];
                list.Add(value);
            }
            else if (o.Command.Length == 0)
            {
                o.Command = a.ToLowerInvariant();
            }
            else
            {
                o.Positional.Add(a);
            }
        }
        return o;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v[^1] : null;
    public IReadOnlyList<string> GetAll(string key) => _values.TryGetValue(key, out var v) ? v : [];
}
