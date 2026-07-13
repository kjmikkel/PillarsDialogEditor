using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectStaleDataScannerTests
{
    private static ConversationPatch Patch(
        string name,
        IReadOnlyList<int> deleted,
        IReadOnlyDictionary<int, string>? comments = null,
        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>? translations = null)
        => new(name, ConversationPatch.CurrentSchemaVersion, [], deleted, [])
           {
               NodeComments = comments ?? new Dictionary<int, string>(),
               Translations = translations ?? new Dictionary<string, IReadOnlyList<NodeTranslation>>(),
           };

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var project = DialogProject.Empty("Test");
        foreach (var p in patches) project = project.WithPatch(p);
        return project;
    }

    [Fact]
    public void Comment_ForDeletedNode_ReportedConfirmed()
    {
        var project = Project(Patch("conv_a", deleted: [7],
            comments: new Dictionary<int, string> { [7] = "old note", [3] = "live note" }));

        var row = Assert.Single(ProjectStaleDataScanner.Scan(project));
        Assert.Equal(("conv_a", 7, StaleDataKind.Comment, (string?)null, StaleConfidence.Confirmed),
            (row.ConversationName, row.NodeId, row.Kind, row.Language, row.Confidence));
    }

    [Fact]
    public void Translation_ForDeletedNode_ReportedPerLanguage()
    {
        var project = Project(Patch("conv_a", deleted: [9],
            translations: new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(9, "gone", ""), new NodeTranslation(2, "stays", "")],
                ["de"] = [new NodeTranslation(9, "weg", "")],
            }));

        var rows = ProjectStaleDataScanner.Scan(project);
        Assert.Equal(
            [("conv_a", 9, "en"), ("conv_a", 9, "de")],
            rows.Select(r => (r.ConversationName, r.NodeId, r.Language)).ToList());
        Assert.All(rows, r => Assert.Equal(StaleConfidence.Confirmed, r.Confidence));
    }

    [Fact]
    public void Layout_ForDeletedNode_ReportedConfirmed()
    {
        var project = Project(Patch("conv_a", deleted: [4]))
            .WithLayout("conv_a", new Dictionary<int, LayoutPoint>
            {
                [4] = new LayoutPoint(10, 10), [1] = new LayoutPoint(20, 20),
            });

        var row = Assert.Single(ProjectStaleDataScanner.Scan(project));
        Assert.Equal((4, StaleDataKind.Layout), (row.NodeId, row.Kind));
    }

    [Fact]
    public void CleanProject_Empty()
    {
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [1] = "note" },
            translations: new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "hi", "")],
            }));
        Assert.Empty(ProjectStaleDataScanner.Scan(project));
    }

    [Fact]
    public void NoDelegate_ProducesOnlyConfirmedRows()
    {
        var project = Project(Patch("conv_a", deleted: [7],
            comments: new Dictionary<int, string> { [7] = "x", [8] = "y" }));
        // 8 is not deleted and there is no effective-set delegate, so 8 is not flagged.
        var row = Assert.Single(ProjectStaleDataScanner.Scan(project));
        Assert.Equal(7, row.NodeId);
    }

    // A stub effective-set delegate: returns the given set for the named
    // conversation, null (unresolvable → skip) for any other name.
    private static Func<string, IReadOnlySet<int>?> Effective(
        string conv, params int[] liveIds)
        => name => name == conv ? new HashSet<int>(liveIds) : null;

    [Fact]
    public void Likely_WhenIdAbsentFromEffectiveSet_AndNotDeleted()
    {
        // Node 8 was an added node the writer later deleted: not in DeletedNodeIds,
        // not in the effective set — a "likely" orphan.
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [8] = "orphan", [3] = "live" }));

        var rows = ProjectStaleDataScanner.Scan(project, Effective("conv_a", 3));
        var row = Assert.Single(rows);
        Assert.Equal((8, StaleConfidence.Likely), (row.NodeId, row.Confidence));
    }

    [Fact]
    public void VanillaNodeComment_NotFlagged_WhenPresentInEffectiveSet()
    {
        // Node 3 is an unmodified vanilla node carrying a translator note: it is not
        // in any structural set but IS in the effective set, so it must NOT be flagged.
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [3] = "note on a vanilla line" }));

        Assert.Empty(ProjectStaleDataScanner.Scan(project, Effective("conv_a", 3)));
    }

    [Fact]
    public void UnresolvableConversation_Skipped_NotFlagged()
    {
        // Delegate returns null for conv_a → we cannot judge staleness → nothing flagged.
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [8] = "orphan" }));

        Assert.Empty(ProjectStaleDataScanner.Scan(project, name => null));
    }

    [Fact]
    public void DeletedNode_ReportedConfirmed_NotDoubleReportedAsLikely()
    {
        // 7 is deleted (confirmed) and also absent from the effective set; must appear once.
        var project = Project(Patch("conv_a", deleted: [7],
            comments: new Dictionary<int, string> { [7] = "gone" }));

        var row = Assert.Single(ProjectStaleDataScanner.Scan(project, Effective("conv_a", 3)));
        Assert.Equal(StaleConfidence.Confirmed, row.Confidence);
    }
}
