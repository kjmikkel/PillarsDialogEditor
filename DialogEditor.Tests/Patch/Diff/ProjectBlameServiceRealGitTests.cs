using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
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

    [Fact]
    public void RealGitBlame_AttributesNodeToLatestCommit()
    {
        using var repo = TempGitRepo.TryCreate();
        if (repo is null) return;   // no git on this machine — skip
        var path = repo.PathOf("m.dialogproject");

        repo.SetAuthor("Ann", "ann@example.com");
        // The file is saved with a UTF-8 BOM (Encoding.UTF8); blame must survive it.
        DialogProjectSerializer.SaveToFile(path, Project("Hi"));
        repo.CommitAll("Add greeting", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));

        // Second commit by a different author changes the node's text.
        repo.SetAuthor("Bob", "bob@example.com");
        DialogProjectSerializer.SaveToFile(path, Project("Hello there"));
        repo.CommitAll("Reword greeting", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero));

        var result = new ProjectBlameService(repo.Git).Load(path);

        foreach (var b in result)
            output.WriteLine($"{b.ConversationName}/{b.NodeId} -> {b.LastCommit.Author} " +
                             $"{b.LastCommit.ShortSha} {b.LastCommit.Date:O} \"{b.LastCommit.Subject}\"");

        var node = Assert.Single(result, b => b.ConversationName == "greeting" && b.NodeId == 1);
        Assert.Equal("Bob", node.LastCommit.Author);
        Assert.Equal("Reword greeting", node.LastCommit.Subject);
    }
}
