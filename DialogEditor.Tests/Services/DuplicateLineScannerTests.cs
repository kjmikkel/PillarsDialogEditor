using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class DuplicateLineScannerTests
{
    private const string L = "the wind howls through the rigging tonight"; // 7 words

    private static ConversationPatch PatchWith(string conv, params (int Id, string Text)[] lines)
    {
        var entries = lines.Select(l => new NodeTranslation(l.Id, l.Text, "")).ToList();
        return new ConversationPatch(conv, ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>> { ["en"] = entries }
        };
    }

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var p = DialogProject.Empty("P");
        foreach (var patch in patches) p = p.WithPatch(patch);
        return p;
    }

    [Fact] // Exact across two conversations; case/whitespace normalized
    public void Exact_AcrossConversations_Grouped()
    {
        var project = Project(
            PatchWith("c1", (1, L)),
            PatchWith("c2", (2, "  THE WIND   howls through the rigging tonight ")));

        var report = DuplicateLineScanner.Scan(project, "en");

        var group = Assert.Single(report.Exact);
        Assert.Equal(2, group.Members.Count);
        Assert.Empty(report.Near);
    }

    [Fact] // One-word change stays above 0.85 → near pair
    public void Near_OneWordChange_Flagged()
    {
        var project = Project(PatchWith("c1",
            (1, L),
            (2, "the wind howls through the rigging today")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        var pair = Assert.Single(report.Near);
        Assert.True(pair.SimilarityPercent >= 85);
    }

    [Fact] // Clearly different long lines → nothing
    public void Near_BelowThreshold_NotFlagged()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "a merchant counts his coins beneath the lantern")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Fewer than 4 words → excluded from both tiers
    public void ShortLines_Excluded()
    {
        var project = Project(PatchWith("c1", (1, "hello there friend"), (2, "hello there friend")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // A candidate in an exact cluster is never also a near pair
    public void ExactMembers_NotAlsoNear()
    {
        var project = Project(PatchWith("c1",
            (1, L),
            (2, L),
            (3, "the wind howls through the rigging today")));  // near to L, but L is an exact cluster

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Single(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Ignored exact key filtered from the active report
    public void IgnoredExact_Filtered()
    {
        var norm = "the wind howls through the rigging tonight";
        var project = Project(PatchWith("c1", (1, L), (2, L)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Exact, [norm], norm));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
    }

    [Fact] // Ignored near key-pair filtered
    public void IgnoredNear_Filtered()
    {
        var a = "the wind howls through the rigging tonight";
        var b = "the wind howls through the rigging today";
        var keys = new[] { a, b }.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var project = Project(PatchWith("c1", (1, a), (2, b)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Near, keys, "«a» ~ «b»"));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Near);
    }
}
