using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class TextTagValidationViewModelTests
{
    public TextTagValidationViewModelTests() => Loc.Configure(new StubStringProvider());

    private static TextTagIssueRow Row(string conv, int node, string lang) =>
        new(conv, node, lang, "msg");

    [Fact]
    public void Rows_PopulatedFromScan_WithLabels()
    {
        var vm = new TextTagValidationViewModel(() => [Row("conv_a", 5, ""), Row("conv_a", 5, "fr")]);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("TextTagValidation_Default", vm.Rows[0].LanguageLabel); // stub echoes key
        Assert.Equal("fr", vm.Rows[1].LanguageLabel);
        Assert.True(vm.HasIssues);
    }

    [Fact]
    public void EmptyScan_NoIssues_WithEmptySummary()
    {
        var vm = new TextTagValidationViewModel(() => []);
        Assert.False(vm.HasIssues);
        Assert.Empty(vm.Rows);
        Assert.Equal("TextTagValidation_NoIssues", vm.SummaryText);
    }

    [Fact]
    public void Refresh_ReinvokesScan()
    {
        var results = new List<TextTagIssueRow>();
        var vm = new TextTagValidationViewModel(() => results);
        Assert.Empty(vm.Rows);
        results.Add(Row("conv_a", 1, ""));
        vm.RefreshCommand.Execute(null);
        Assert.Single(vm.Rows);
        Assert.True(vm.HasIssues);
    }

    // ── Spelling rows (spell checker feature) ───────────────────────────────

    private static TextTagIssueRow SpellRow(string word) =>
        new("conv_a", 5, "", "msg", TextIssueType.Spelling, word);

    [Fact]
    public void TypeLabels_AreLocalized()
    {
        var vm = new TextTagValidationViewModel(() => [Row("conv_a", 1, ""), SpellRow("captian")]);
        Assert.Equal("TextIssueType_Tag",      vm.Rows[0].TypeLabel); // stub echoes keys
        Assert.Equal("TextIssueType_Spelling", vm.Rows[1].TypeLabel);
    }

    [Fact]
    public void AddToDictionary_OnlyOnSpellingRows_InvokesAndRescans()
    {
        var added = new List<string>();
        var results = new List<TextTagIssueRow> { Row("conv_a", 1, ""), SpellRow("captian") };
        var vm = new TextTagValidationViewModel(() => results, addWord: added.Add);

        Assert.False(vm.Rows[0].CanAddToDictionary);
        Assert.True(vm.Rows[1].CanAddToDictionary);

        results.RemoveAt(1); // simulate the word becoming correct after add
        vm.Rows[1].AddToDictionaryCommand.Execute(null);

        Assert.Equal(["captian"], added);
        Assert.Single(vm.Rows); // rescanned
    }

    [Fact]
    public void NoAddWordCallback_DisablesAddButton()
    {
        var vm = new TextTagValidationViewModel(() => [SpellRow("captian")]);
        Assert.False(vm.Rows[0].CanAddToDictionary);
    }

    // ── Duplicate + ignored panes ───────────────────────────────────────────

    private static LineRef Ref(string conv, int id, string text) => new(conv, id, text);

    private static DuplicateLineReport OneExact() =>
        new([new ExactDuplicateGroup("the wind howls through the rigging tonight",
                "The wind howls through the rigging tonight",
                [Ref("c1", 1, "The wind howls through the rigging tonight"),
                 Ref("c2", 2, "the wind howls through the rigging tonight")])],
            []);

    [Fact]
    public void DupScan_PopulatesDuplicateRows()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: OneExact);

        Assert.True(vm.HasDuplicates);
        var row = Assert.Single(vm.DuplicateRows);
        Assert.Contains("wind howls", row.Text);
    }

    [Fact]
    public void IgnoreCommand_CallsDelegate_AndRefreshes()
    {
        IgnoredDuplicate? ignored = null;
        var report = OneExact();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: () => ignored is null ? report : new DuplicateLineReport([], []),
            ignore: e => ignored = e);

        vm.DuplicateRows[0].IgnoreCommand.Execute(null);

        Assert.NotNull(ignored);
        Assert.Equal(DuplicateKind.Exact, ignored!.Kind);
        Assert.False(vm.HasDuplicates);   // re-scanned; delegate now filters it out
    }

    [Fact]
    public void IgnoredList_PopulatesPane_AndRestoreCallsDelegate()
    {
        IgnoredDuplicate? restored = null;
        var entry = new IgnoredDuplicate(DuplicateKind.Exact, ["k"], "the ignored line here");
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            ignoredList: () => [entry],
            unignore: e => restored = e);

        Assert.True(vm.HasIgnoredDuplicates);
        var row = Assert.Single(vm.IgnoredDuplicateRows);
        Assert.Equal("the ignored line here", row.DisplayText);

        row.RestoreCommand.Execute(null);
        Assert.Equal(entry, restored);
    }
}
