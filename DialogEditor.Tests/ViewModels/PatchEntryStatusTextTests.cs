using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// GitHub issue 259: a load-order row's problem (failed to load, or conflicts with another mod)
/// was only a hover tooltip on a ⚠ glyph. StatusText is the same explanation as visible text.
/// </summary>
public class PatchEntryStatusTextTests
{
    public PatchEntryStatusTextTests() => Loc.Configure(new EchoStringProvider(new()
    {
        ["PatchManager_EntryLoadErrorDetail"] = "Could not load: {0}",
        ["PatchManager_EntryConflictDetail"]  = "Conflicts.",
    }));

    [Fact]
    public void LoadedEntry_WithoutConflict_HasNoStatus()
    {
        var entry = new PatchEntryViewModel("a.dialogpack", DialogProject.Empty("A"));
        Assert.Equal(string.Empty, entry.StatusText);
        Assert.False(entry.HasStatus);
    }

    [Fact]
    public void EntryThatFailedToLoad_SaysWhy()
    {
        var entry = new PatchEntryViewModel("a.dialogpack", "not a zip");
        Assert.Equal("Could not load: not a zip", entry.StatusText);
        Assert.True(entry.HasStatus);
    }

    [Fact]
    public void ConflictingEntry_SaysSo_AndRaisesTheChange()
    {
        var entry = new PatchEntryViewModel("a.dialogpack", DialogProject.Empty("A"));
        var raised = new List<string?>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.HasConflict = true;

        Assert.Equal("Conflicts.", entry.StatusText);
        Assert.Contains(nameof(PatchEntryViewModel.StatusText), raised);
        Assert.Contains(nameof(PatchEntryViewModel.HasStatus), raised);
    }
}
