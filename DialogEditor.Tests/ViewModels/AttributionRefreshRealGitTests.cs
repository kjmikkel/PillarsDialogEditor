using System.Reflection;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// Issue #12 end to end, against a real repository (#21). The unit tests prove the two
/// halves over FakeGit — BranchesViewModel raises HeadMoved after a commit, and
/// InvalidateAttribution makes the next lookup rebuild — but nothing joined them where a
/// commit really happens. Here the commit goes through the app's own path (the Branches
/// window's commit-then-switch, wired by MainWindowViewModel.CreateBranchesViewModel),
/// and the retried checkout is then refused for real by an untracked file, so the project
/// is never reloaded: the exact branch that #12 left stale.
/// </summary>
public class AttributionRefreshRealGitTests : IDisposable
{
    private readonly string _settingsPath;

    public AttributionRefreshRealGitTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"attr_realgit_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    private static DialogProject Project(string text)
    {
        var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
            [Node(1)], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [new NodeTranslation(1, text, "")] },
        };
        return new DialogProject("M", ConversationPatch.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { ["greeting"] = patch });
    }

    /// Same seam as MainWindowViewModelAttributionTests: the attribution cache keys off
    /// _projectPath, and a full LoadProjectAsync would drag in game data for no gain.
    private static void SetProjectPath(MainWindowViewModel vm, string path) =>
        typeof(MainWindowViewModel)
            .GetField("_projectPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, path);

    [Fact]
    public async Task CommitBeforeBlockedSwitch_RefreshesAttributionWithoutReopening()
    {
        using var repo = TempGitRepo.TryCreate();
        if (repo is null) return;   // no git on this machine — skip
        var path = repo.PathOf("m.dialogproject");

        // main: Ann writes the greeting.
        repo.SetAuthor("Ann", "ann@example.com");
        DialogProjectSerializer.SaveToFile(path, Project("Hi"));
        repo.CommitAll("Add greeting", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));

        // other: tracks notes.txt, so an untracked notes.txt on main blocks switching to it.
        repo.Run("checkout", "-q", "-b", "other");
        File.WriteAllText(repo.PathOf("notes.txt"), "tracked on other");
        repo.CommitAll("Add notes", new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        repo.Run("checkout", "-q", "main");

        // Open the project and read the attribution once, which fills the cache.
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        SetProjectPath(vm, path);
        vm.AttributionLoader = p => new ProjectBlameService(repo.Git).Load(p);   // as MainWindow wires it
        Assert.Equal("Ann", vm.Detail.AttributionLookup!("greeting", 1)?.LastCommit.Author);

        // Bob rewords the greeting (a tracked change, so the switch offers commit-all) and
        // leaves an untracked notes.txt that the retried checkout will refuse to overwrite.
        repo.SetAuthor("Bob", "bob@example.com");
        DialogProjectSerializer.SaveToFile(path, Project("Hello there"));
        File.WriteAllText(repo.PathOf("notes.txt"), "untracked on main");

        var branches = vm.CreateBranchesViewModel(new GitBranchService(repo.Git));
        Assert.NotNull(branches);
        branches.RequestCommitConfirmation = _ => Task.FromResult<string?>("Reword greeting");
        branches.Selected = branches.Branches.Single(b => b.Name == "other");

        await branches.SwitchCommand.ExecuteAsync(null);

        // Preconditions of the #12 branch: the commit landed, the switch did not.
        Assert.Equal("main", repo.Run("rev-parse", "--abbrev-ref", "HEAD").StdOut.Trim());
        Assert.Equal("Reword greeting", repo.Run("log", "-1", "--format=%s").StdOut.Trim());
        Assert.Equal(Loc.Get("Branches_StatusBlockedUntracked"), branches.StatusText);

        // The point: same open project, no reload — and the attribution has moved on.
        var node = vm.Detail.AttributionLookup!("greeting", 1);
        Assert.Equal("Bob", node?.LastCommit.Author);
        Assert.Equal("Reword greeting", node?.LastCommit.Subject);
    }

    [Fact]
    public void CreateBranchesViewModel_NoProjectOpen_ReturnsNull()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Assert.Null(vm.CreateBranchesViewModel(new GitBranchService(new ProcessGitRunner())));
    }
}
