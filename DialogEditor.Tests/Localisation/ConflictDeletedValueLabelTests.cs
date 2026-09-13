using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// The deleting side of a delete-vs-edit conflict used to carry the literal
/// "(deleted)" in MineValue/TheirsValue. That string was both what the dialog showed
/// and the sentinel MergeBuilder compared against, so it could not be localised in
/// place. The analyzer now reports which side deleted (MergeConflict.DeletedSide) and
/// the row renders the label — the same split as PatchCounts for whole-conversation
/// conflicts.
/// </summary>
public class ConflictDeletedValueLabelTests
{
    private static MergeConflict DeleteVsEdit(MergeSide deletedSide, string mine, string theirs) =>
        new(MergeConflictKind.DeleteVsEdit, "conv", 4, null, mine, theirs) { DeletedSide = deletedSide };

    [Fact]
    public void MineDeletes_MineValueComesFromResources_TheirsPassesThrough()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(DeleteVsEdit(MergeSide.Mine, "", "DefaultText"));

        Assert.Equal("GitConflict_DeletedValue", row.MineValue);
        Assert.Equal("DefaultText", row.TheirsValue);
    }

    [Fact]
    public void TheirsDeletes_TheirsValueComesFromResources_MinePassesThrough()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(DeleteVsEdit(MergeSide.Theirs, "DefaultText", ""));

        Assert.Equal("DefaultText", row.MineValue);
        Assert.Equal("GitConflict_DeletedValue", row.TheirsValue);
    }

    [Fact]
    public void EditingSideAdded_ItsValueComesFromResources()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(
            new MergeConflict(MergeConflictKind.DeleteVsEdit, "conv", 4, null, "", "")
            { DeletedSide = MergeSide.Mine, EditSummary = MergeEditSummary.Added });

        Assert.Equal("GitConflict_DeletedValue", row.MineValue);
        Assert.Equal("GitConflict_AddedValue",   row.TheirsValue);
    }

    [Fact]
    public void EditingSideModified_ItsValueComesFromResources()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(
            new MergeConflict(MergeConflictKind.DeleteVsEdit, "conv", 4, null, "", "")
            { DeletedSide = MergeSide.Theirs, EditSummary = MergeEditSummary.Modified });

        Assert.Equal("GitConflict_ModifiedValue", row.MineValue);
        Assert.Equal("GitConflict_DeletedValue",  row.TheirsValue);
    }

    [Fact]
    public void EditingSideNamedItsFields_ValuePassesThrough()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(DeleteVsEdit(MergeSide.Mine, "", "DefaultText"));

        Assert.Equal("DefaultText", row.TheirsValue);
    }

    [Fact]
    public void MineDeletes_MineIsTheAcceptDeletionChoice()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(DeleteVsEdit(MergeSide.Mine, "", "DefaultText"));

        Assert.Equal("GitConflict_AcceptDeletion", row.MineLabel);
        Assert.Equal("GitConflict_KeepEdit", row.TheirsLabel);
    }

    [Fact]
    public void TheirsDeletes_TheirsIsTheAcceptDeletionChoice()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(DeleteVsEdit(MergeSide.Theirs, "DefaultText", ""));

        Assert.Equal("GitConflict_KeepEdit", row.MineLabel);
        Assert.Equal("GitConflict_AcceptDeletion", row.TheirsLabel);
    }
}

public class ConflictDeletedValueResourceEndToEndTests
{
    public ConflictDeletedValueResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void DeletedValue_RendersFromStringsAxaml()
    {
        var row = new ConflictRowViewModel(
            new MergeConflict(MergeConflictKind.DeleteVsEdit, "conv", 4, null, "", "DefaultText")
            { DeletedSide = MergeSide.Mine });

        Assert.Equal("(deleted)", row.MineValue);
    }

    [AvaloniaFact]
    public void AddedAndModifiedSummaries_RenderFromStringsAxaml()
    {
        var added = new ConflictRowViewModel(
            new MergeConflict(MergeConflictKind.DeleteVsEdit, "conv", 4, null, "", "")
            { DeletedSide = MergeSide.Mine, EditSummary = MergeEditSummary.Added });
        var modified = new ConflictRowViewModel(
            new MergeConflict(MergeConflictKind.DeleteVsEdit, "conv", 4, null, "", "")
            { DeletedSide = MergeSide.Mine, EditSummary = MergeEditSummary.Modified });

        Assert.Equal("(added)",    added.TheirsValue);
        Assert.Equal("(modified)", modified.TheirsValue);
    }
}
