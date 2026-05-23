using DialogEditor.Core.GameData;
using DialogEditor.Patch;

const string Version = "1.0.0";

const string Help = """
  dialog-patcher — apply Pillars of Eternity dialog patch projects to game files

  Usage:
    dialog-patcher <game-dir> <project.dialogproject> [project2.dialogproject ...] [options]

  Arguments:
    game-dir                Root directory of a PoE1 or PoE2 installation.
    project.dialogproject   One or more .dialogproject files to apply.
                            When multiple projects are given they are merged in
                            order (later projects win on any contested field).

  Options:
    -f, --force     Apply patches even when a field's current value does not
                    match the patch's expected baseline. Use when the game has
                    been updated since the patch was created.
    -v, --verbose   Print each conversation as it is patched.
    -q, --quiet     Suppress all output except errors.
    --dry-run       Validate and plan the apply without writing any files.
    --version       Print version and exit.
    -h, --help      Show this help.

  Exit codes:
    0   All patches applied (or dry-run completed) successfully.
    1   Patch conflict detected. Re-run with --force to override.
    2   Argument, file, or game-detection error.
  """;

// ── Parse arguments ───────────────────────────────────────────────────────────

bool force   = Has("-f", "--force");
bool verbose = Has("-v", "--verbose");
bool quiet   = Has("-q", "--quiet");
bool dryRun  = Has("--dry-run");

if (Has("-h", "--help"))   { Console.WriteLine(Help); return 0; }
if (Has("--version"))      { Console.WriteLine($"dialog-patcher {Version}"); return 0; }

var positional = args.Where(a => !a.StartsWith('-')).ToArray();

if (positional.Length < 2)
{
    Error("Missing required arguments: <game-dir> <project.dialogproject>");
    Console.Error.WriteLine();
    Console.Error.WriteLine(Help);
    return 2;
}

var gameDir      = positional[0];
var projectPaths = positional[1..];

// ── Validate paths ────────────────────────────────────────────────────────────

if (!Directory.Exists(gameDir))
{
    Error($"Game directory not found: {gameDir}");
    return 2;
}

foreach (var path in projectPaths)
{
    if (!File.Exists(path))
    {
        Error($"Project file not found: {path}");
        return 2;
    }
}

// ── Detect game ───────────────────────────────────────────────────────────────

var provider = GameDataProviderFactory.Detect(gameDir);
if (provider is null)
{
    Error($"Could not detect a Pillars of Eternity installation at: {gameDir}");
    Error("Expected a PoE1 or PoE2 game root directory.");
    return 2;
}

Info($"Game:    {provider.GameName}");

// ── Load projects ─────────────────────────────────────────────────────────────

var projects = new List<DialogProject>();
foreach (var path in projectPaths)
{
    try
    {
        var p = DialogProjectSerializer.LoadFromFile(path);
        projects.Add(p);
        Info($"Project: {p.Name}  ({p.Patches.Count} patch(es))  [{path}]");
    }
    catch (Exception ex)
    {
        Error($"Could not load project '{path}': {ex.Message}");
        return 2;
    }
}

// ── Merge projects (if more than one) ────────────────────────────────────────

var merged = projects[0];
for (int i = 1; i < projects.Count; i++)
    merged = merged.MergeWith(projects[i]);

if (merged.Patches.Count == 0)
{
    Info("Nothing to apply.");
    return 0;
}

// ── Collect all conversation names to patch ───────────────────────────────────

var allConvNames = merged.Patches.Keys
    .Concat(merged.NewConversations ?? [])
    .Distinct()
    .OrderBy(n => n)
    .ToList();

if (dryRun)
{
    Info($"Dry run: would patch {allConvNames.Count} conversation(s).");
    foreach (var name in allConvNames)
        Verbose($"  {name}");
    return 0;
}

// ── Apply ─────────────────────────────────────────────────────────────────────

int applied = 0;
int skipped = 0;

try
{
    foreach (var convName in allConvNames)
    {
        if (!merged.Patches.TryGetValue(convName, out var patch))
            continue;

        var isNew = merged.IsNewConversation(convName);
        var file  = provider.FindConversation(convName)
                 ?? (isNew ? provider.BuildNewConversationFile(convName) : null);

        if (file is null)
        {
            Console.Error.WriteLine($"Warning: conversation not found on disk, skipping: {convName}");
            skipped++;
            continue;
        }

        if (!File.Exists(file.ConversationPath))
            provider.InitializeConversationFile(file);

        var conversation = provider.LoadConversation(file);
        var baseSnap     = ConversationSnapshotBuilder.Build(conversation);
        var result       = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: force);
        provider.SaveConversation(file, result);

        applied++;
        Verbose($"  patched: {convName}");
    }
}
catch (PatchConflictException ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Patch conflict detected:");
    Console.Error.WriteLine($"  Node ID:  {ex.NodeId}");
    Console.Error.WriteLine($"  Field:    {ex.FieldName}");
    Console.Error.WriteLine($"  Expected: {ex.ExpectedFrom}");
    Console.Error.WriteLine($"  Found:    {ex.ActualValue}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("The game file has changed since this patch was created.");
    Console.Error.WriteLine("Re-run with --force to apply the patch's target value anyway.");
    return 1;
}
catch (Exception ex)
{
    Error($"Unexpected error: {ex.Message}");
    if (verbose)
        Console.Error.WriteLine(ex.ToString());
    return 2;
}

// ── Summary ───────────────────────────────────────────────────────────────────

var suffix = dryRun ? " (dry run)" : string.Empty;
if (skipped > 0)
    Info($"Done: {applied} patched, {skipped} skipped (conversation not found){suffix}.");
else
    Info($"Done: {applied} conversation(s) patched successfully{suffix}.");

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

bool Has(params string[] flags) => flags.Any(f => args.Contains(f));
void Info(string msg)    { if (!quiet) Console.WriteLine(msg); }
void Verbose(string msg) { if (verbose && !quiet) Console.WriteLine(msg); }
void Error(string msg)   => Console.Error.WriteLine($"Error: {msg}");
