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
}
