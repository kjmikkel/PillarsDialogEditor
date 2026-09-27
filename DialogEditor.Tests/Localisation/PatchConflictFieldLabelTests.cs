using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// A delete-vs-modify row in the Patch Manager used to carry the literal "(deleted)"
/// in PatchConflict.FieldName. That string was both what the conflict list showed and
/// part of ConflictDetector's DistinctBy identity key, so it could not be localised in
/// place — and DialogEditor.Patch references only Core, so it cannot reach Loc at all.
/// FieldName is now null for a deletion (IsDeletion) and the row view model renders
/// the label. Same split as MergeConflict.DeletedSide for git merges.
/// </summary>
public class PatchConflictRowLabelTests
{
    private static PatchConflict FieldRow()    => new("conv1", 7, "DefaultText", 0, 1);
    private static PatchConflict DeletionRow() => new("conv1", 7, null, 1, 0);
    private static PatchConflictRowViewModel Row(PatchConflict c) => new(c, "ModA", "ModB");

    [Fact]
    public void FieldConflict_ShowsTheFieldNameVerbatim()
    {
        Loc.Configure(new StubStringProvider());

        Assert.Equal("DefaultText", Row(FieldRow()).FieldLabel);
    }

    [Fact]
    public void DeleteVsModify_LabelComesFromResources()
    {
        Loc.Configure(new StubStringProvider());

        Assert.Equal("PatchManager_DeletedField", Row(DeletionRow()).FieldLabel);
    }

    [Fact]
    public void Row_ExposesTheUnderlyingConflict()
    {
        Loc.Configure(new StubStringProvider());
        var conflict = DeletionRow();

        Assert.Same(conflict, Row(conflict).Conflict);
    }

    // Issue #6: the kinds without a field name each get their own resource label.
    [Fact]
    public void AddedNodeConflict_LabelComesFromResources()
    {
        Loc.Configure(new StubStringProvider());
        var c = new PatchConflict("conv1", 500, null, 0, 1) { Kind = PatchConflictKind.AddedNode };

        Assert.Equal("PatchManager_AddedNodeField", Row(c).FieldLabel);
    }

    [Fact]
    public void LinkConflict_LabelComesFromResources()
    {
        Loc.Configure(new StubStringProvider());
        var c = new PatchConflict("conv1", 5, null, 0, 1) { Kind = PatchConflictKind.Link, LinkToNodeId = 9 };

        Assert.Equal("PatchManager_LinkField", Row(c).FieldLabel);
    }
}

public class PatchConflictRowResourceEndToEndTests
{
    public PatchConflictRowResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void DeletedField_RendersFromSharedStrings()
    {
        var row = new PatchConflictRowViewModel(new PatchConflict("conv1", 7, null, 1, 0), "ModA", "ModB");

        Assert.Equal("(deleted)", row.FieldLabel);
    }

    [AvaloniaFact]
    public void Description_ComposesTheWholeRowFromSharedStrings()
    {
        // The view used to compose this line from a hard-coded MultiBinding
        // StringFormat, which the .axaml guard does not scan. It is one resource now.
        var row = new PatchConflictRowViewModel(new PatchConflict("conv1", 7, "DefaultText", 0, 1), "ModA", "ModB");

        Assert.Equal("conversation 'conv1' · node 7 · DefaultText · between 'ModA' and 'ModB'", row.Description);
    }

    [AvaloniaFact]
    public void AddedNodeAndLinkLabels_RenderFromSharedStrings()
    {
        var added = new PatchConflict("conv1", 500, null, 0, 1) { Kind = PatchConflictKind.AddedNode };
        var link  = new PatchConflict("conv1", 5, null, 0, 1) { Kind = PatchConflictKind.Link, LinkToNodeId = 9 };

        Assert.Equal("(added by both)", new PatchConflictRowViewModel(added, "ModA", "ModB").FieldLabel);
        Assert.Equal("link to node 9",  new PatchConflictRowViewModel(link,  "ModA", "ModB").FieldLabel);
    }
}
