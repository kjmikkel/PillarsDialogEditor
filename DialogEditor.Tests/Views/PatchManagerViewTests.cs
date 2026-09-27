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
}
