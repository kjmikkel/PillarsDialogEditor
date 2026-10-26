using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// GitHub issue 320: the warnings about a node's *text* (possible misspellings with their
/// quick fixes, unknown tokens and unbalanced markup) sat inside the Display section, which
/// starts collapsed, so a user saw squiggles but no list of fixes, and a screen-reader user
/// (for whom that list is the spelling channel) got no hint at all. They now sit right under
/// the text boxes, visible whatever the sections' state.
public class NodeDetailViewTextWarningsTests : IDisposable
{
    private readonly string _spellDir;

    public NodeDetailViewTextWarningsTests()
    {
        Loc.Configure(new StubStringProvider());
        _spellDir = Path.Combine(Path.GetTempPath(), $"textwarn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_spellDir);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DialogEditor.slnx")))
            root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(_spellDir, "en_US.aff"));
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(_spellDir, "en_US.dic"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_spellDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private (NodeDetailViewModel vm, NodeDetailView view) Show(string text)
    {
        // Every section collapsed, as a fresh pane opens.
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2", ActiveLanguage = "en" };
        vm.SpellChecker = new SpellCheckService(
            new SpellDictionaryStore(_spellDir, Path.Combine(_spellDir, "user.txt")));
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
            Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);
        vm.Load(new NodeViewModel(node, new StringEntry(1, text, "")));
        var view = new NodeDetailView { DataContext = vm };
        new Window { Content = view, Width = 500, Height = 900 }.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, view);
    }

    private static ItemsControl ListShowing<T>(NodeDetailView view, Func<T, bool> match) =>
        view.GetLogicalDescendants().OfType<ItemsControl>()
            .Single(ic => ic.ItemsSource is IEnumerable<T> items && items.Any(match));

    private static void AssertShownOutsideAnySection(ItemsControl list)
    {
        // A collapsed Expander's content isn't in the visual tree at all (so it's "effectively
        // visible", with no visual ancestors): check it's attached, laid out and not in a section.
        Assert.NotNull(list.GetVisualRoot());
        Assert.True(list.IsEffectivelyVisible, "the list is hidden");
        Assert.True(list.Bounds.Height > 0, "the list has no height");
        Assert.Empty(list.GetLogicalAncestors().OfType<Expander>());
    }

    [AvaloniaFact]
    public void SpellingIssues_AreVisible_WithEverySectionCollapsed()
    {
        var (vm, view) = Show("The captian waits.");
        Assert.NotEmpty(vm.SpellingIssues);
        AssertShownOutsideAnySection(ListShowing<SpellingIssueViewModel>(view, _ => true));
    }

    [AvaloniaFact]
    public void TokenWarnings_AreVisible_WithEverySectionCollapsed()
    {
        var (vm, view) = Show("The captain <i>waits.");
        Assert.NotEmpty(vm.TokenWarnings);
        var first = vm.TokenWarnings[0];
        AssertShownOutsideAnySection(ListShowing<string>(view, s => s == first));
    }

    [AvaloniaFact]
    public void TextWarnings_SitBetweenTheTextBoxesAndTheLinks()
    {
        // Right under what they're about, above everything else in the pane.
        var (_, view) = Show("The captian <i>waits.");
        var order = view.GetLogicalDescendants().OfType<Control>().ToList();
        var female = order.OfType<TextBox>().Single(t => t.Watermark is not null && t.AcceptsReturn
                                                         && t.Name != "DefaultTextBox");
        var spelling = ListShowing<SpellingIssueViewModel>(view, _ => true);
        var links = order.OfType<ItemsControl>().Single(ic => ic.ItemsSource is IEnumerable<ConnectionViewModel>);
        Assert.InRange(order.IndexOf(spelling), order.IndexOf(female) + 1, order.IndexOf(links) - 1);
    }
}
