using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Patch.Diff;

/// <summary>
/// GitHub issue 264 (F-27): History showed a commit as c991ef7 and Attribution as c991ef7c, so a
/// Modder cross-checking the two couldn't tell it was the same version. Every window now
/// abbreviates commit ids to CommitInfo.ShortLength characters.
/// </summary>
public class CommitIdLengthTests : IDisposable
{
    private sealed class RecordingGit(string root) : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return args is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") : new GitResult(0, "", "");
        }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ids_{Guid.NewGuid():N}.dialogproject");

    public CommitIdLengthTests()
    {
        Loc.Configure(new StubStringProvider());
        File.WriteAllText(_path, DialogProjectSerializer.Serialize(DialogProject.Empty("p")));
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch (Exception) { /* best-effort */ }
    }

    private static string Abbrev => $"--abbrev={CommitInfo.ShortLength}";

    [Fact]
    public void ShortLength_IsEight_AsAttributionAlwaysShowed()
        => Assert.Equal(8, CommitInfo.ShortLength);

    [Fact]
    public void History_AsksGitForTheSameLength()
    {
        var git = new RecordingGit(Path.GetTempPath());
        new ProjectHistoryService(git).Load(_path);
        Assert.Contains(Abbrev, Assert.Single(git.Calls, c => c[0] == "log"));
    }

    [Fact]
    public void CompareVersions_AsksGitForTheSameLength()
    {
        var git = new RecordingGit(Path.GetDirectoryName(_path)!);
        _ = new DiffViewModel(git, new StubDispatcher(), _path);
        Assert.Contains(Abbrev, Assert.Single(git.Calls, c => c[0] == "log"));
    }
}
