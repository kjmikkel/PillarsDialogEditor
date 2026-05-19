using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class DialogProjectLayoutTests
{
    [Fact]
    public void WithLayout_StoresPositionsForConversation()
    {
        var project = DialogProject.Empty("mod");
        var positions = new Dictionary<int, LayoutPoint>
        {
            [1] = new(100, 200),
            [2] = new(300, 400),
        };

        var updated = project.WithLayout("edér", positions);

        var layout = updated.GetLayout("edér");
        Assert.NotNull(layout);
        Assert.Equal(new LayoutPoint(100, 200), layout[1]);
        Assert.Equal(new LayoutPoint(300, 400), layout[2]);
    }

    [Fact]
    public void WithLayout_PreservesExistingConversationLayouts()
    {
        var project = DialogProject.Empty("mod")
            .WithLayout("conv1", new Dictionary<int, LayoutPoint> { [1] = new(10, 20) })
            .WithLayout("conv2", new Dictionary<int, LayoutPoint> { [2] = new(30, 40) });

        Assert.NotNull(project.GetLayout("conv1"));
        Assert.NotNull(project.GetLayout("conv2"));
    }

    [Fact]
    public void GetLayout_UnknownConversation_ReturnsNull()
    {
        Assert.Null(DialogProject.Empty("mod").GetLayout("nonexistent"));
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesLayout()
    {
        var positions = new Dictionary<int, LayoutPoint>
        {
            [7]  = new(150.5, 300.0),
            [42] = new(600.0, 100.5),
        };
        var project = DialogProject.Empty("mod").WithLayout("eder", positions);

        var json       = DialogProjectSerializer.Serialize(project);
        var deserialized = DialogProjectSerializer.Deserialize(json);

        var layout = deserialized.GetLayout("eder");
        Assert.NotNull(layout);
        Assert.Equal(new LayoutPoint(150.5, 300.0), layout[7]);
        Assert.Equal(new LayoutPoint(600.0, 100.5), layout[42]);
    }

    // ── MergeLayout ───────────────────────────────────────────────────────

    [Fact]
    public void MergeLayout_UnionOfBothProjects_IncomingWinsOnOverlap()
    {
        var projectA = DialogProject.Empty("A")
            .WithLayout("eder", new Dictionary<int, LayoutPoint>
            {
                [1] = new(100, 200),   // only in A
                [2] = new(300, 400),   // in both — A's value
            });

        var projectB = DialogProject.Empty("B")
            .WithLayout("eder", new Dictionary<int, LayoutPoint>
            {
                [2] = new(999, 999),   // in both — B's value should win
                [3] = new(500, 600),   // only in B
            });

        var merged = projectA.MergeLayout("eder",
            projectB.GetLayout("eder")!);

        var layout = merged.GetLayout("eder")!;
        Assert.Equal(3, layout.Count);
        Assert.Equal(new LayoutPoint(100, 200), layout[1]);   // from A, untouched
        Assert.Equal(new LayoutPoint(999, 999), layout[2]);   // B wins overlap
        Assert.Equal(new LayoutPoint(500, 600), layout[3]);   // from B
    }

    [Fact]
    public void MergeLayout_PreservesEntriesAbsentFromIncoming()
    {
        var project = DialogProject.Empty("mod")
            .WithLayout("conv", new Dictionary<int, LayoutPoint> { [7] = new(10, 20) });

        var merged = project.MergeLayout("conv",
            new Dictionary<int, LayoutPoint> { [8] = new(30, 40) });

        var layout = merged.GetLayout("conv")!;
        Assert.Equal(2, layout.Count);   // both nodes present
        Assert.Equal(new LayoutPoint(10, 20), layout[7]);
        Assert.Equal(new LayoutPoint(30, 40), layout[8]);
    }

    [Fact]
    public void WithLayout_Replace_CleansUpDeletedNodes()
    {
        // WithLayout is used on Ctrl+S with the *complete* current canvas —
        // replacing ensures deleted nodes don't accumulate stale entries.
        var project = DialogProject.Empty("mod")
            .WithLayout("conv", new Dictionary<int, LayoutPoint>
            {
                [1] = new(10, 20),
                [2] = new(30, 40),  // node 2 will be "deleted"
            });

        // Save with only node 1 remaining
        var saved = project.WithLayout("conv",
            new Dictionary<int, LayoutPoint> { [1] = new(10, 20) });

        var layout = saved.GetLayout("conv")!;
        Assert.Single(layout);         // stale entry for node 2 gone
        Assert.False(layout.ContainsKey(2));
    }

    [Fact]
    public void Deserialize_OldProjectWithoutLayouts_ReturnsNullLayout()
    {
        // Simulate a .dialogproject written before Layouts was added
        const string json = """
            {
              "Name": "OldMod",
              "SchemaVersion": 1,
              "Patches": {}
            }
            """;
        var project = DialogProjectSerializer.Deserialize(json);
        Assert.Null(project.GetLayout("anything"));
    }
}
