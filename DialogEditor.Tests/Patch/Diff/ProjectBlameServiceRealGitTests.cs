using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;
using Xunit.Abstractions;

namespace DialogEditor.Tests.Patch.Diff;

// End-to-end attribution against a real temporary git repo. Exercises ProcessGitRunner
// + `git blame --line-porcelain` + UTF-8/BOM decoding — the path FakeGit can't cover.
// Skips quietly when git is unavailable.
public class ProjectBlameServiceRealGitTests(ITestOutputHelper output)
{
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

    private static bool GitAvailable(IGitRunner git, string dir)
    {
        try { return git.Run(dir, "--version").Ok; }
        catch (DiffException) { return false; }
    }

    [Fact]
    public void RealGitBlame_AttributesNodeToLatestCommit()
    {
        var git = new ProcessGitRunner();
        var dir = Path.Combine(Path.GetTempPath(), "blamesmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "m.dialogproject");

        try
        {
            if (!GitAvailable(git, dir)) return;   // no git on this machine — skip

            Assert.True(git.Run(dir, "init").Ok);
            git.Run(dir, "config", "user.email", "ann@example.com");
            git.Run(dir, "config", "user.name", "Ann");

            // The file is saved with a UTF-8 BOM (Encoding.UTF8); blame must survive it.
            DialogProjectSerializer.SaveToFile(path, Project("Hi"));
            Assert.True(git.Run(dir, "add", "-A").Ok);
            // Explicit, distinct author dates — git author-time is second-granular, so
            // two commits in the same wall-clock second would tie ambiguously.
            Assert.True(git.Run(dir, "commit", "-m", "Add greeting", "--date=2026-01-01T10:00:00").Ok);

            // Second commit by a different author changes the node's text.
            git.Run(dir, "config", "user.email", "bob@example.com");
            git.Run(dir, "config", "user.name", "Bob");
            DialogProjectSerializer.SaveToFile(path, Project("Hello there"));
            Assert.True(git.Run(dir, "add", "-A").Ok);
            Assert.True(git.Run(dir, "commit", "-m", "Reword greeting", "--date=2026-02-01T10:00:00").Ok);

            var result = new ProjectBlameService(git).Load(path);

            foreach (var b in result)
                output.WriteLine($"{b.ConversationName}/{b.NodeId} -> {b.LastCommit.Author} " +
                                 $"{b.LastCommit.ShortSha} {b.LastCommit.Date:O} \"{b.LastCommit.Subject}\"");

            var node = Assert.Single(result, b => b.ConversationName == "greeting" && b.NodeId == 1);
            Assert.Equal("Bob", node.LastCommit.Author);
            Assert.Equal("Reword greeting", node.LastCommit.Subject);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
