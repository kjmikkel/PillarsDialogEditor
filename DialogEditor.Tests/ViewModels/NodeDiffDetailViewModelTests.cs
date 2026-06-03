using System.Collections.Generic;
using System.Linq;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDiffDetailViewModelTests
{
    public NodeDiffDetailViewModelTests() => Loc.Configure(new StubStringProvider());

    private static IReadOnlyDictionary<string, (string Default, string Female)> Map(
        params (string Lang, string Default, string Female)[] items) =>
        items.ToDictionary(i => i.Lang, i => (i.Default, i.Female));

    private static readonly IReadOnlyDictionary<string, (string Default, string Female)> Empty = Map();

    [Fact]
    public void Primary_IsAlwaysPresent_EvenWhenUnchanged()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", ""), ("fr", "x", "")),
            Map(("en", "a", ""), ("fr", "y", "")));

        Assert.Equal(2, vm.Sections.Count);
        var en = vm.Sections.Single(s => s.LanguageCode == "en");
        Assert.True(en.IsPrimary);
        Assert.Contains(vm.Sections, s => s.LanguageCode == "fr");
    }

    [Fact]
    public void NonPrimary_Unchanged_IsExcluded()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", ""), ("de", "d", "")),
            Map(("en", "b", ""), ("de", "d", "")));

        Assert.Single(vm.Sections);
        Assert.Equal("en", vm.Sections[0].LanguageCode);
    }

    [Fact]
    public void Sections_OrderedPrimaryFirstThenAlphabetical()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "fr",
            Map(("en", "1", ""), ("de", "2", ""), ("fr", "3", "")),
            Map(("en", "x", ""), ("de", "y", ""), ("fr", "z", "")));

        Assert.Equal(new[] { "fr", "de", "en" }, vm.Sections.Select(s => s.LanguageCode).ToArray());
    }

    [Fact]
    public void StructuralOnly_WhenNoLanguageDiffers()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", "")), Map(("en", "a", "")));

        Assert.True(vm.IsStructuralOnly);
        Assert.False(vm.ShowSections);
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void Added_PlaceholderBefore_PerSection()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Added, "en",
            Empty, Map(("en", "hi", ""), ("fr", "salut", "")));

        var en = vm.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal("Diff_Detail_NodeAdded", en.DefaultBefore);
        Assert.Equal("hi", en.DefaultAfter);
        var fr = vm.Sections.Single(s => s.LanguageCode == "fr");
        Assert.Equal("Diff_Detail_NodeAdded", fr.DefaultBefore);
        Assert.Equal("salut", fr.DefaultAfter);
    }

    [Fact]
    public void Removed_PlaceholderAfter_PerSection()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Removed, "en",
            Map(("en", "bye", ""), ("fr", "adieu", "")), Empty);

        var en = vm.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal("bye", en.DefaultBefore);
        Assert.Equal("Diff_Detail_NodeRemoved", en.DefaultAfter);
    }

    [Fact]
    public void FemaleRow_Visibility_IsPerSection()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", "fa"), ("de", "d", "")),
            Map(("en", "b", "fb"), ("de", "e", "")));

        Assert.True(vm.Sections.Single(s => s.LanguageCode == "en").HasFemaleRow);
        Assert.False(vm.Sections.Single(s => s.LanguageCode == "de").HasFemaleRow);
    }

    [Fact]
    public void Section_LanguageName_IsResolved()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", "")), Map(("en", "b", "")));

        // Stub returns the resource key verbatim.
        Assert.Equal("Language_Name_en", vm.Sections[0].LanguageName);
    }

    [Fact]
    public void HeaderText_UsesLocFormatKey()
    {
        var vm = new NodeDiffDetailViewModel(7, DiffStatus.Changed, "en",
            Map(("en", "a", "")), Map(("en", "b", "")));

        Assert.Equal("Diff_Detail_Header", vm.HeaderText);
    }
}
