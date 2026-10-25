using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// The conflict summary list used to compose its line from a MultiBinding
/// StringFormat over PatchConflict.FieldName — which hard-coded both the English
/// scaffolding and, for a delete-vs-modify row, the "(deleted)" marker. It binds one
/// PatchConflictRowViewModel.Description now; these render it for real so a broken
/// binding path fails loudly rather than silently blanking the row.
/// </summary>
public class PatchManagerViewTests
{
    private static PatchManagerViewModel VmWith(params DialogProject[] projects)
    {
        Loc.Configure(new AvaloniaStringProvider());
        var vm = new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker());
        for (var i = 0; i < projects.Length; i++)
            vm.Entries.Add(new PatchEntryViewModel($"mod{i}.dialogproject", projects[i]));
        return vm;
    }

    private static DialogProject Modifies(int nodeId, string field) =>
        DialogProject.Empty("mod").WithPatch(
            new ConversationPatch("conv1", ConversationPatch.CurrentSchemaVersion, [], [],
                [new NodeModification(nodeId,
                    new Dictionary<string, FieldChange> { [field] = new FieldChange("old", "new") },
                    [], [])]));

    private static DialogProject Deletes(int nodeId) =>
        DialogProject.Empty("mod").WithPatch(
            new ConversationPatch("conv1", ConversationPatch.CurrentSchemaVersion, [], [nodeId], []));

    private static string FirstRowText(PatchManagerViewModel vm)
    {
        var view   = new PatchManagerView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var list = view.FindControl<ItemsControl>("ConflictList")!;
        Assert.Equal(vm.Conflicts.Count, list.ItemCount);

        return list.GetVisualDescendants().OfType<TextBlock>().First().Text!;
    }

    [AvaloniaFact]
    public void FieldConflictRow_RendersTheFieldName()
        => Assert.Equal("conversation 'conv1' \u00b7 node 5 \u00b7 DefaultText \u00b7 between 'mod' and 'mod'",
                        FirstRowText(VmWith(Modifies(5, "DefaultText"), Modifies(5, "DefaultText"))));

    [AvaloniaFact]
    public void DeleteVsModifyRow_RendersTheLocalisedDeletedMarker()
        => Assert.Equal("conversation 'conv1' \u00b7 node 7 \u00b7 (deleted) \u00b7 between 'mod' and 'mod'",
                        FirstRowText(VmWith(Modifies(7, "DefaultText"), Deletes(7))));

    // ── "Get the latest patcher" link (GitHub issue 79) ──────────────────

    private static Button GetLatestPatcherButton(PatchManagerViewModel vm)
    {
        var view = new PatchManagerView { DataContext = vm };
        new Window { Content = view }.Show();
        return view.FindControl<Button>("GetLatestPatcherButton")!;
    }

    [AvaloniaFact]
    public void GetLatestPatcher_IsHiddenByDefault()
        => Assert.False(GetLatestPatcherButton(VmWith()).IsVisible);

    [AvaloniaFact]
    public void GetLatestPatcher_ShowsWithTheFlag_AndOpensTheReleasesPage()
    {
        var vm = VmWith();
        string? opened = null;
        vm.UrlOpener = url => { opened = url; return true; };
        var button = GetLatestPatcherButton(vm);

        vm.ShowGetLatestPatcher = true;
        button.Command!.Execute(null);

        Assert.True(button.IsVisible);
        Assert.Equal(DialogEditor.Patch.Schema.SchemaFormats.PatcherReleasesUrl, opened);
        Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(button) as string));
    }

    // ── "After a game update…" link (GitHub issue 147) ───────────────────
    // A small text link by Apply / Remove all mods, in both the editor's Patch Manager and the
    // standalone patcher (both host this view). It is always there, never a banner or prompt.

    private static Button GameUpdateHelpLink(PatchManagerViewModel vm)
    {
        var view = new PatchManagerView { DataContext = vm };
        new Window { Content = view }.Show();
        return view.FindControl<Button>("GameUpdateHelpLink")!;
    }

    [AvaloniaFact]
    public void GameUpdateHelpLink_IsVisible_WithLocalisedTextTooltipAndAutomationName()
    {
        var link = GameUpdateHelpLink(VmWith());

        Assert.True(link.IsVisible);
        Assert.Equal(Loc.Get("PatchManager_GameUpdateHelp_Link"), link.Content);
        Assert.Equal(Loc.Get("ToolTip_GameUpdateHelp"), ToolTip.GetTip(link));
        Assert.Equal(Loc.Get("ToolTip_GameUpdateHelp"), AutomationProperties.GetHelpText(link));
    }

    [AvaloniaFact]
    public void GameUpdateHelpLink_ShowsTheExplanation()
    {
        var vm = VmWith();
        (string Title, string Message)? shown = null;
        vm.ShowGameUpdateHelp = (title, message) => { shown = (title, message); return Task.CompletedTask; };

        GameUpdateHelpLink(vm).Command!.Execute(null);

        Assert.Equal((Loc.Get("PatchManager_GameUpdateHelp_Title"), Loc.Get("PatchManager_GameUpdateHelp_Message")), shown);
        Assert.DoesNotContain("[", shown!.Value.Message);   // a real string, not a missing-key marker
    }

    // GitHub issue 259: a broken entry's reason is visible text in its row, not only a tooltip.
    [AvaloniaFact]
    public void EntryThatFailedToLoad_ShowsTheReasonInItsRow()
    {
        var vm = VmWith();
        vm.Entries.Add(new PatchEntryViewModel("broken.dialogpack", "End of Central Directory record could not be found."));
        var view   = new PatchManagerView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var texts = view.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("End of Central Directory record could not be found."));
    }
}
