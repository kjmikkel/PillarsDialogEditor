using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class FlowAnalyticsWindowTests
{
    public FlowAnalyticsWindowTests() => Loc.Configure(new StubStringProvider());

    private static ConversationEditSnapshot Snapshot() => new([
        new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "one two three", "",
                             "Conversation", "None", "", "", "", false, false,
                             [new LinkEditSnapshot(0, 1, 1f, "", false)], [], []),
        new NodeEditSnapshot(1, true, SpeakerCategory.Player, "", "", "four five", "",
                             "Conversation", "None", "", "", "", false, false, [], [], [])
    ]);

    [AvaloniaFact]
    public void Constructs_WithPathStats()
    {
        var vm = new FlowAnalyticsViewModel(Snapshot, _ => { });
        var window = new FlowAnalyticsWindow(vm);
        window.Show();
        vm.RefreshCommand.Execute(null);

        Assert.True(window.IsVisible);
        Assert.True(vm.HasPathStats);
        window.Close();
    }

    /// The reading-speed picker is the only way to reach a non-default speed from the UI,
    /// so pin that it is actually bound both ways and that a selection re-runs the analysis.
    [AvaloniaFact]
    public void Window_BindsReadingSpeedPicker_AndSelectionReRunsAnalysis()
    {
        var reads = 0;
        var vm = new FlowAnalyticsViewModel(() => { reads++; return Snapshot(); }, _ => { });
        var window = new FlowAnalyticsWindow(vm);
        window.Show();
        vm.RefreshCommand.Execute(null);

        var combo = window.FindControl<ComboBox>("ReadingSpeedComboBox");
        Assert.NotNull(combo);
        Assert.Equal(PathStatsFormat.DefaultWordsPerMinute, combo!.SelectedItem);

        var before = reads;
        combo.SelectedItem = 120;

        Assert.Equal(120, vm.WordsPerMinute);
        Assert.True(reads > before, "selecting a reading speed should re-run the analysis");
        window.Close();
    }

    /// root -> A (choice) -> npc -> A1 (choice), plus a dead end. Exercises the recursive
    /// TreeDataTemplate and the endings list, which a VM test cannot reach: a self-nesting
    /// template that fails to resolve throws only when the tree is actually realised.
    private static ConversationEditSnapshot NestedSnapshot() => new([
        new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "start", "",
                             "Conversation", "None", "", "", "", false, false,
                             [new LinkEditSnapshot(0, 1, 1f, "", false)], [], []),
        new NodeEditSnapshot(1, true, SpeakerCategory.Player, "", "", "A", "",
                             "Conversation", "None", "", "", "", false, false,
                             [new LinkEditSnapshot(1, 2, 1f, "", false)], [], []),
        new NodeEditSnapshot(2, false, SpeakerCategory.Npc, "", "", "npc line", "",
                             "Conversation", "None", "", "", "", false, false,
                             [new LinkEditSnapshot(2, 3, 1f, "", false)], [], []),
        new NodeEditSnapshot(3, true, SpeakerCategory.Player, "", "", "A1", "",
                             "Conversation", "None", "", "", "", false, false, [], [], [])
    ]);

    [AvaloniaFact]
    public void Constructs_WithNestedForkTreeAndEndings()
    {
        var vm = new FlowAnalyticsViewModel(NestedSnapshot, _ => { });
        var window = new FlowAnalyticsWindow(vm);
        window.Show();
        vm.RefreshCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var tree = window.FindControl<TreeView>("ForkTreeView");
        var endings = window.FindControl<ItemsControl>("EndingsList");
        Assert.NotNull(tree);
        Assert.NotNull(endings);
        Assert.True(vm.HasEndings);

        // The container theme binds IsExpanded two-way, so the top-level fork opens itself.
        var container = tree!.ContainerFromIndex(0) as TreeViewItem;
        Assert.NotNull(container);
        Assert.True(container!.IsExpanded);

        window.Close();
    }
}
