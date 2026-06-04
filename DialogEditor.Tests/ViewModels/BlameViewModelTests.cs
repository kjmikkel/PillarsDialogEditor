using System.Text;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class BlameViewModelTests
{
    public BlameViewModelTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"blamevm_{Guid.NewGuid():N}.dialogproject");

    private static string OneNodePorcelain()
    {
        string[] content =
        [
            "{", "  \"Patches\": {", "    \"greeting\": {", "      \"AddedNodes\": [",
            "        {", "          \"NodeId\": 1", "        }", "      ]", "    }", "  }", "}",
        ];
        var sb = new StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            var final = i + 1;
            sb.Append($"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2 {final} {final} 1\n");
            sb.Append("author Mia\nauthor-mail <m@x>\nauthor-time 1700000000\nauthor-tz +0000\n");
            sb.Append("committer Mia\ncommitter-mail <m@x>\ncommitter-time 1700000000\ncommitter-tz +0000\n");
            sb.Append("summary Add greeting\nfilename test.dialogproject\n");
            sb.Append($"\t{content[i]}\n");
        }
        return sb.ToString();
    }

    private static FakeGit GitWithBlame(string porcelain)
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        return new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "blame"       ? new GitResult(0, porcelain, "") :
                                                        new GitResult(0, "", ""),
        };
    }

    [Fact]
    public void LoadsAttributionRows()
    {
        var vm = new BlameViewModel(GitWithBlame(OneNodePorcelain()), ProjPath());

        Assert.True(vm.HasData);
        var row = Assert.Single(vm.Rows);
        Assert.Equal("greeting", row.ConversationName);
        Assert.Equal(1, row.NodeId);
        Assert.Equal("Mia", row.Author);
    }

    [Fact]
    public void RepoError_SetsStatus_NoData()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal: not a git repository") };
        var vm  = new BlameViewModel(git, ProjPath());

        Assert.False(vm.HasData);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void NoAttribution_SetsStatus()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a => a is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, root + "\n", "")
                : new GitResult(128, "", "fatal: no such path in HEAD"),
        };
        var vm = new BlameViewModel(git, ProjPath());

        Assert.False(vm.HasData);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }
}
