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

    [Fact]
    public void FieldConflict_ShowsTheFieldNameVerbatim()
    {
        Loc.Configure(new StubStringProvider());

        Assert.Equal("DefaultText", new PatchConflictRowViewModel(FieldRow()).FieldLabel);
    }

    [Fact]
    public void DeleteVsModify_LabelComesFromResources()
    {
        Loc.Configure(new StubStringProvider());

        Assert.Equal("PatchManager_DeletedField", new PatchConflictRowViewModel(DeletionRow()).FieldLabel);
    }

    [Fact]
    public void Row_ExposesTheUnderlyingConflict()
    {
        Loc.Configure(new StubStringProvider());
        var conflict = DeletionRow();

        Assert.Same(conflict, new PatchConflictRowViewModel(conflict).Conflict);
    }
}

public class PatchConflictRowResourceEndToEndTests
{
    public PatchConflictRowResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void DeletedField_RendersFromSharedStrings()
    {
        var row = new PatchConflictRowViewModel(new PatchConflict("conv1", 7, null, 1, 0));

        Assert.Equal("(deleted)", row.FieldLabel);
    }

    [AvaloniaFact]
    public void Description_ComposesTheWholeRowFromSharedStrings()
    {
        // The view used to compose this line from a hard-coded MultiBinding
        // StringFormat, which the .axaml guard does not scan. It is one resource now.
        var row = new PatchConflictRowViewModel(new PatchConflict("conv1", 7, "DefaultText", 0, 1));

        Assert.Equal("conversation 'conv1' · node 7 · DefaultText", row.Description);
    }
}
