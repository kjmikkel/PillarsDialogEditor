using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class DiffViewModelApplyTests
{
    [Fact]
    public void ConversationGroup_TogglingAll_SelectsEveryNode()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: [3]);
        var group  = new ConversationChangeViewModel(change);

        group.IsAllSelected = true;

        Assert.All(group.Nodes, n => Assert.True(n.IsSelected));
    }

    [Fact]
    public void ConversationGroup_IsAllSelected_IsNull_WhenPartiallySelected()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var group  = new ConversationChangeViewModel(change);

        group.Nodes[0].IsSelected = true;

        Assert.Null(group.IsAllSelected);
    }

    [Fact]
    public void ConversationGroup_SelectedNodeIds_ReflectsChecked()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var group  = new ConversationChangeViewModel(change);

        group.Nodes[0].IsSelected = true;

        Assert.Equal([1], group.SelectedNodeIds);
    }
}
