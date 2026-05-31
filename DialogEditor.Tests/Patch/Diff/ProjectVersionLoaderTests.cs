using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectVersionLoaderTests
{
    private sealed class FakeGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler(args);
        }
    }

    private static string ProjectJson(string name) =>
        DialogProjectSerializer.Serialize(DialogProject.Empty(name));

    [Fact]
    public void WorkingCopy_ReadsFileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc_{Guid.NewGuid():N}.dialogproject");
        File.WriteAllText(path, ProjectJson("WC"));
        try
        {
            var loader = new ProjectVersionLoader(new FakeGit());
            var project = loader.Load(new DiffEndpoint.WorkingCopy(), path);
            Assert.Equal("WC", project.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GitRef_RunsGitShowAndDeserializes()
    {
        var fake = new FakeGit();
        fake.Handler = args =>
            args is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, "C:/repo\n", "")
                : new GitResult(0, ProjectJson("AtRef"), "");

        var loader = new ProjectVersionLoader(fake);
        var project = loader.Load(new DiffEndpoint.GitRef("main"), "C:/repo/mods/my.dialogproject");

        Assert.Equal("AtRef", project.Name);
        Assert.Contains(fake.Calls, c => c.Length == 2 && c[0] == "show"
            && c[1] == "main:mods/my.dialogproject");
    }

    [Fact]
    public void GitRef_NonZeroExit_ThrowsDiffException()
    {
        var fake = new FakeGit
        {
            Handler = args => args is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, "C:/repo\n", "")
                : new GitResult(128, "", "fatal: invalid object name 'nope'")
        };
        var loader = new ProjectVersionLoader(fake);

        var ex = Assert.Throws<DiffException>((Action)(() =>
            loader.Load(new DiffEndpoint.GitRef("nope"), "C:/repo/mods/my.dialogproject")));
        Assert.Contains("nope", ex.Message);
    }

    // ── Kind tests ────────────────────────────────────────────────────────────

    [Fact]
    public void WorkingCopy_NotFound_KindIsFileNotFound()
    {
        var loader = new ProjectVersionLoader(new FakeGit());
        var path   = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.dialogproject");

        var ex = Assert.Throws<DiffException>((Action)(() =>
            loader.Load(new DiffEndpoint.WorkingCopy(), path)));
        Assert.Equal(DiffExceptionKind.FileNotFound, ex.Kind);
    }

    [Fact]
    public void WorkingCopy_LockedFile_ThrowsDiffExceptionWithKindReadFailed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"locked_{Guid.NewGuid():N}.dialogproject");
        File.WriteAllText(path, ProjectJson("Locked"));
        // Hold an exclusive lock on the file
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            var loader = new ProjectVersionLoader(new FakeGit());
            var ex = Assert.Throws<DiffException>((Action)(() =>
                loader.Load(new DiffEndpoint.WorkingCopy(), path)));
            Assert.Equal(DiffExceptionKind.ReadFailed, ex.Kind);
        }
        finally
        {
            fs.Close();
            File.Delete(path);
        }
    }

    [Fact]
    public void GitRef_RevParseFails_KindIsNotARepo()
    {
        var fake = new FakeGit
        {
            Handler = args => args is ["rev-parse", "--show-toplevel"]
                ? new GitResult(128, "", "fatal: not a git repository")
                : new GitResult(0, "", "")
        };
        var loader = new ProjectVersionLoader(fake);

        var ex = Assert.Throws<DiffException>((Action)(() =>
            loader.Load(new DiffEndpoint.GitRef("main"), "C:/repo/mods/my.dialogproject")));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }

    [Fact]
    public void GitRef_ShowFails_KindIsBadRef()
    {
        var fake = new FakeGit
        {
            Handler = args => args is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, "C:/repo\n", "")
                : new GitResult(128, "", "fatal: invalid object")
        };
        var loader = new ProjectVersionLoader(fake);

        var ex = Assert.Throws<DiffException>((Action)(() =>
            loader.Load(new DiffEndpoint.GitRef("badref"), "C:/repo/mods/my.dialogproject")));
        Assert.Equal(DiffExceptionKind.BadRef, ex.Kind);
    }
}
