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
