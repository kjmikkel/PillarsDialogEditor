using System.Text;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectBlameServiceTests
{
    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"blame_{Guid.NewGuid():N}.dialogproject");

    private sealed record L(string Content, string Sha, string Author, long Time, string Summary);

    // Synthesises `git blame --line-porcelain` output for the given file lines.
    private static string Porcelain(params L[] lines)
    {
        var sb = new StringBuilder();
        var final = 0;
        foreach (var l in lines)
        {
            final++;
            sb.Append($"{l.Sha} {final} {final} 1\n");
            sb.Append($"author {l.Author}\n");
            sb.Append("author-mail <x@example.com>\n");
            sb.Append($"author-time {l.Time}\n");
            sb.Append("author-tz +0000\n");
            sb.Append($"committer {l.Author}\n");
            sb.Append("committer-mail <x@example.com>\n");
            sb.Append($"committer-time {l.Time}\n");
            sb.Append("committer-tz +0000\n");
            sb.Append($"summary {l.Summary}\n");
            sb.Append("filename test.dialogproject\n");
            sb.Append($"\t{l.Content}\n");
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
    public void AttributesNodeToMostRecentCommitInItsRange()
    {
        // Node 1 occupies lines 5-8; line 7 has the newest commit (C).
        var porcelain = Porcelain(
            new L("{",                              "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 1
            new L("  \"Patches\": {",               "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 2
            new L("    \"greeting\": {",            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 3
            new L("      \"AddedNodes\": [",        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 4
            new L("        {",                      "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Bob", 2000, "add node"),// 5
            new L("          \"NodeId\": 1,",       "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Bob", 2000, "add node"),// 6
            new L("          \"IsPlayerChoice\": false", "cccccccccccccccccccccccccccccccccccccccc", "Cara", 3000, "reword"),// 7
            new L("        }",                      "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Bob", 2000, "add node"),// 8
            new L("      ]",                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 9
            new L("    }",                          "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 10
            new L("  }",                            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"),   // 11
            new L("}",                              "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Ann", 1000, "init"));  // 12

        var result = new ProjectBlameService(GitWithBlame(porcelain)).Load(ProjPath());

        var node = Assert.Single(result, b => b.ConversationName == "greeting" && b.NodeId == 1);
        Assert.Equal("Cara", node.LastCommit.Author);
        Assert.Equal("cccccccc", node.LastCommit.ShortSha);
        Assert.Equal("reword", node.LastCommit.Subject);
    }

    [Fact]
    public void NotARepo_Throws()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal: not a git repository") };
        var ex = Assert.Throws<DiffException>(() => new ProjectBlameService(git).Load(ProjPath()));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }

    [Fact]
    public void FileNotInHead_ReturnsEmpty_NotAnError()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a => a is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, root + "\n", "")
                : new GitResult(128, "", "fatal: no such path 'x.dialogproject' in HEAD"),
        };
        Assert.Empty(new ProjectBlameService(git).Load(ProjPath()));
    }

    [Fact]
    public void BlameFailure_Throws()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a => a is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, root + "\n", "")
                : new GitResult(128, "", "fatal: something unexpected went wrong"),
        };
        Assert.Throws<DiffException>(() => new ProjectBlameService(git).Load(ProjPath()));
    }
}
