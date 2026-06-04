using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class GitConflictResolutionWindowTests
{
    public GitConflictResolutionWindowTests() => Loc.Configure(new StubStringProvider());

    private static GitConflictResolutionViewModel MakeVm()
    {
        static DialogProject P(string to)
        {
            var mod = new NodeModification(
                4, new Dictionary<string, FieldChange> { ["DefaultText"] = new FieldChange("orig", to) }, [], []);
            return DialogProject.Empty("p").WithPatch(
                new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [mod]));
        }

        var mine   = P("friend");
        var theirs = P("traveler");
        return new GitConflictResolutionViewModel(mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [AvaloniaFact]
    public void ConflictList_ShowsOneItem()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("ConflictList")!.ItemCount);
    }

    [AvaloniaFact]
    public void ApplyButton_DisabledUntilResolved()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.False(window.FindControl<Button>("ApplyButton")!.Command!.CanExecute(null));
    }

    [AvaloniaFact]
    public void SelectedFieldEditConflict_BuildsHighlightedInlines()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        var mineText = window.FindControl<TextBlock>("MineDiffText")!;
        Assert.NotNull(mineText.Inlines);
        Assert.True(mineText.Inlines!.Count > 0);
    }

    private static GitConflictResolutionViewModel MakeTranslationVm()
    {
        static DialogProject P(string text)
        {
            var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                {
                    ["en"] = [new NodeTranslation(4, text, "")],
                },
            };
            return DialogProject.Empty("p").WithPatch(patch);
        }

        var mine   = P("Hello friend");
        var theirs = P("Hello traveler");
        return new GitConflictResolutionViewModel(mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [AvaloniaFact]
    public void SelectedTranslationConflict_BuildsHighlightedInlines()
    {
        var vm     = MakeTranslationVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        var mineText = window.FindControl<TextBlock>("MineDiffText")!;
        Assert.True(mineText.Inlines!.Count > 1);   // common + mine-only spans
    }

    private static GitConflictResolutionViewModel MakeFemaleTranslationVm()
    {
        static DialogProject P(string female)
        {
            var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                {
                    ["en"] = [new NodeTranslation(4, "Hello", female)],   // Default equal both sides
                },
            };
            return DialogProject.Empty("p").WithPatch(patch);
        }

        var mine   = P("Hello friend");
        var theirs = P("Hello traveler");
        return new GitConflictResolutionViewModel(mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [AvaloniaFact]
    public void FemaleTranslationConflict_RendersVisibleFemaleInlines()
    {
        var vm     = MakeFemaleTranslationVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        var femaleText = window.FindControl<TextBlock>("MineFemaleDiffText")!;
        Assert.True(femaleText.IsVisible);
        Assert.True(femaleText.Inlines!.Count > 1);   // common + mine-only spans
    }

    [AvaloniaFact]
    public void FieldEditConflict_HidesFemaleBlock()
    {
        var vm     = MakeVm();   // field edit, no female text
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.False(window.FindControl<TextBlock>("MineFemaleDiffText")!.IsVisible);
    }

    [AvaloniaFact]
    public void TranslationResolveAndApply_TakesTheirsText()
    {
        var vm     = MakeTranslationVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        vm.Conflicts[0].Choice = MergeSide.Theirs;
        window.FindControl<Button>("ApplyButton")!.Command!.Execute(null);

        Assert.NotNull(vm.Result);
        var t = vm.Result!.Patches["greeting"].Translations["en"].Single(x => x.NodeId == 4);
        Assert.Equal("Hello traveler", t.DefaultText);
    }

    private static GitConflictResolutionViewModel MakeConversationLevelVm()
    {
        static NodeEditSnapshot Node(int id) =>
            new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);
        static DialogProject P(params NodeEditSnapshot[] nodes) =>
            DialogProject.Empty("p").WithPatch(
                new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, nodes, [], []));

        var mine   = P(Node(5));
        var theirs = P(Node(5), Node(9));
        return new GitConflictResolutionViewModel(mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [AvaloniaFact]
    public void ConversationLevelConflict_ResolvesToTheirsWholePatch()
    {
        var vm     = MakeConversationLevelVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.Equal(MergeConflictKind.ConversationLevel, vm.Conflicts[0].Conflict.Kind);

        vm.Conflicts[0].Choice = MergeSide.Theirs;
        window.FindControl<Button>("ApplyButton")!.Command!.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Contains(vm.Result!.Patches["greeting"].AddedNodes, n => n.NodeId == 9);
    }

    [AvaloniaFact]
    public void ResolveAndApply_ProducesMergedResult()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        vm.Conflicts[0].Choice = MergeSide.Theirs;

        var apply = window.FindControl<Button>("ApplyButton")!;
        Assert.True(apply.Command!.CanExecute(null));
        apply.Command!.Execute(null);

        Assert.NotNull(vm.Result);
        var mod = vm.Result!.Patches["greeting"].ModifiedNodes.Single(m => m.NodeId == 4);
        Assert.Equal("traveler", mod.FieldChanges["DefaultText"].To);
    }
}
