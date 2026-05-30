using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class DiffWindowTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public DiffWindowTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best-effort */ }
    }

    private string WriteTempProject(DialogProject project)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diffwin_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        File.WriteAllText(path, DialogProjectSerializer.Serialize(project));
        return path;
    }

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    /// <summary>
    /// Builds a FakeGit that returns refContent for "show" and empty OK for branch/log.
    /// </summary>
    private static FakeGit MakeFakeGit(string projectDir, string? refContent,
        string branchOutput = "main\n")
        => new(args =>
        {
            if (args is ["rev-parse", "--show-toplevel"])
                return new GitResult(0, projectDir + "\n", "");

            if (args.Length == 2 && args[0] == "show")
            {
                if (refContent is null)
                    return new GitResult(128, "", "fatal: bad ref");
                return new GitResult(0, refContent, "");
            }

            if (args.Length >= 1 && args[0] == "branch")
                return new GitResult(0, branchOutput, "");

            if (args.Length >= 1 && args[0] == "log")
                return new GitResult(0, "", "");

            return new GitResult(0, "", "");
        });

    [AvaloniaFact]
    public void ChangedList_ShowsOneItem_WhenOneConversationDiffers()
    {
        // Disk project has node 1 + node 2; git ref has only node 1
        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(2)], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1)], [], []));
        var refJson = DialogProjectSerializer.Serialize(refProject);

        var git = MakeFakeGit(dir, refContent: refJson);
        var vm  = new DiffViewModel(git, path);

        var window = new DiffWindow(vm);
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("ChangedList")!.ItemCount);
    }

    private sealed class FakeGit(Func<string[], GitResult> handler) : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => handler(args);
    }
}
