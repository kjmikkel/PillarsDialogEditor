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
}
