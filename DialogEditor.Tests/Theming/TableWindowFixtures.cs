using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 178: the app's tables, each opened with a row or two, so the layout checks see row cells
/// as well as headers. Values are long where the setup lets us choose them, so a cell that fails
/// to trim pushes into its neighbour. The setups mirror the window and view-model tests they are
/// named after.
/// </summary>
internal static class TableWindowFixtures
{
    private const string LongName = "a_very_long_conversation_name_that_runs_past_its_column";
    private const string LongText = "A line of dialogue long enough to run past the end of its column and need trimming";
    private const char US = '\u001f';

    public static readonly IReadOnlyDictionary<string, Func<Window>> All = new Dictionary<string, Func<Window>>
    {
        ["FindInProject"]  = FindInProject,
        ["Blame"]          = Blame,
        ["History"]        = History,
        ["SpeakerLines"]   = SpeakerLines,
        ["RepDisposition"] = RepDisposition,
        ["BatchVoImport"]  = BatchVoImport,
        ["VoValidation"]   = VoValidation,
    };

    // These view models finish their work on the UI thread, so blocking it with GetResult()
    // deadlocks; pump the headless dispatcher until the task is done instead.
    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Fixture setup did not finish.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }

    // ── Find in Project (FindInProjectWindowTests) ───────────────────────────────

    private static Window FindInProject()
    {
        var node = new ConversationNode(1, false, SpeakerCategory.Npc, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb",
            "", [], [], [], "Conversation", "None");
        var conv = new Conversation(LongName, [node], new StringTable([new StringEntry(1, "The Watcher. " + LongText, "")]));
        var provider = new FakeGameDataProvider("poe2", "en", conv);
        var project = DialogProject.Empty("P").WithPatch(
            new ConversationPatch(LongName, ConversationPatch.CurrentSchemaVersion, [], [], []));
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null)) { SearchText = "Watcher" };
        vm.SearchCommand.Execute(null);
        return new FindInProjectWindow(vm);
    }

    // ── Git windows (BlameWindowTests, HistoryWindowTests) ───────────────────────

    private sealed class FakeGit(Func<string[], GitResult> handler) : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => handler(args);
    }

    private static Window Blame()
    {
        string[] content =
        [
            "{", "  \"Patches\": {", $"    \"{LongName}\": {{", "      \"AddedNodes\": [",
            "        {", "          \"NodeId\": 1", "        }", "      ]", "    }", "  }", "}",
        ];
        var sb = new StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            var line = i + 1;
            sb.Append($"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2 {line} {line} 1\n");
            sb.Append("author Mia Longsurname-Hyphenated\nauthor-mail <m@x>\nauthor-time 1700000000\nauthor-tz +0000\n");
            sb.Append("committer Mia\ncommitter-mail <m@x>\ncommitter-time 1700000000\ncommitter-tz +0000\n");
            sb.Append($"summary {LongText}\nfilename test.dialogproject\n");
            sb.Append($"\t{content[i]}\n");
        }
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit(a =>
            a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
            a.Length > 0 && a[0] == "blame"       ? new GitResult(0, sb.ToString(), "") :
                                                    new GitResult(0, "", ""));
        return new BlameWindow(new BlameViewModel(git, Path.Combine(root, $"blamewin_{Guid.NewGuid():N}.dialogproject")));
    }

    private static Window History()
    {
        var log = string.Join(US, "a1b2c3d", "a1b2c3d4", "Mia Longsurname-Hyphenated", "2026-05-30T10:00:00+00:00", LongText);
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit(a =>
            a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
            a.Length > 0 && a[0] == "log"         ? new GitResult(0, log, "") :
                                                    new GitResult(0, "", ""));
        return new HistoryWindow(new HistoryViewModel(git, Path.Combine(root, $"histwin_{Guid.NewGuid():N}.dialogproject")));
    }

    // ── Speaker Lines (SpeakerLineBrowserWindowTests) ─────────────────────────────

    private static Window SpeakerLines()
    {
        var rows = new[]
        {
            new SpeakerLineRow("g1", LongName, 1234, LineVariant.Female, LongText, LineOrigin.Vanilla),
        };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), new FakeGameDataProvider("poe2", "en"), "en", () => (null, null), null,
            scanner: (_, _, _) => rows);
        Wait(vm.ScanAsync());
        return new SpeakerLineBrowserWindow(vm);
    }

    // ── Rep/Disposition Balance (RepDispositionBalanceViewModelTests) ─────────────

    private static Window RepDisposition()
    {
        GameDataNameService.Register("Disposition", new[]
        {
            new NamedEntry("Benevolent", "Benevolent"),
            new NamedEntry("Cruel", "Cruel"),
        });
        var node = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            [], new ConditionNode[] { new ConditionLeaf("Boolean DispositionEqual(Axis, Rank)", new[] { "Benevolent", "2" }, false, "And") }, []);
        var open = new ConversationEditSnapshot(new[] { node });
        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"),
            () => ("open", open)) { Source = BalanceSource.ProjectChanges, Scope = BalanceScope.Current };
        Wait(vm.RefreshCommand.ExecuteAsync(null));
        return new RepDispositionBalanceWindow(vm);
    }

    // ── Batch VO Import (BatchVoImportViewModelTests) ─────────────────────────────

    private sealed class NoImporter : IVoImporter
    {
        public bool IsWwiseAvailable => true;
        public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NoPlayer : IVoAudioPlayer
    {
        public bool IsAvailable => true;
#pragma warning disable CS0067 // never raised: nothing plays in a layout test
        public event Action? PlaybackStopped;
#pragma warning restore CS0067
        public void Play(string path) { }
        public void Stop() { }
    }

    private static Window BatchVoImport()
    {
        BatchVoRowViewModel Row(VoPresence status) =>
            new(LongName, 1234, LongText, status, $"C:/dest/{LongName}_1234.wem", $"C:/dest/{LongName}_1234_fem.wem");
        var vm = new BatchVoImportViewModel([Row(VoPresence.Found), Row(VoPresence.Missing)], new NoImporter());
        return new BatchVoImportDialog(vm, new NoPlayer());
    }

    // ── VO Validation (VoValidationViewModelTests) ────────────────────────────────

    private static Window VoValidation()
    {
        var gameRoot = Path.Combine(Path.GetTempPath(), $"VoTableTest_{Guid.NewGuid():N}");
        var nodes = new[]
        {
            new NodeEditSnapshot(1234, false, SpeakerCategory.Npc, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "",
                LongText, "", "Conversation", "None", "", "", "", true, false, [], [], []),
        };
        var vm = new VoValidationViewModel(nodes, LongName, gameRoot, "poe2");
        Wait(vm.RunAsync());
        return new VoValidationWindow(vm);
    }
}
