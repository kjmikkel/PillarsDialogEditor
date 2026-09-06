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
            dupScan: _ => OneExact());

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
            dupScan: _ => ignored is null ? report : new DuplicateLineReport([], []),
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

    // ── Configurable near-duplicate threshold (issue #14) ────────────────────

    [Fact] // Omitting the ctor argument keeps the historical 0.85 bar.
    public void NearThreshold_DefaultsToScannerDefault()
    {
        var vm = new TextTagValidationViewModel(scan: () => []);

        Assert.Equal(DuplicateLineScanner.DefaultNearThreshold, vm.NearThreshold);
    }

    /// The constructor calls Refresh(), so the threshold field has to be assigned before
    /// that — otherwise the very first duplicate scan runs with 0 and reports nonsense.
    [Fact]
    public void NearThreshold_InitialValue_ReachesTheConstructorScan()
    {
        var seen = new List<double>();
        _ = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: o => { seen.Add(o.NearThreshold); return new DuplicateLineReport([], []); },
            duplicateOptions: new DuplicateScanOptions(0.75));

        Assert.Equal(0.75, Assert.Single(seen));
    }

    [Fact] // The preset list the ComboBox binds to must offer the default.
    public void NearThresholdOptions_IncludeTheDefault()
    {
        var vm = new TextTagValidationViewModel(scan: () => []);

        Assert.Contains(DuplicateLineScanner.DefaultNearThreshold, vm.NearThresholdOptions);
        Assert.All(vm.NearThresholdOptions, t => Assert.InRange(t, 0.0, 1.0));
    }

    [Fact] // Modelled on CheckGameFiles_Toggle_PassesFlagToStaleScan.
    public void NearThreshold_Change_PassesNewValueToDupScan()
    {
        var seen = new List<double>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: o => { seen.Add(o.NearThreshold); return new DuplicateLineReport([], []); });
        seen.Clear();

        vm.NearThreshold = 0.70;

        Assert.Equal(0.70, Assert.Single(seen));
    }

    [Fact] // Persistence is the caller's job; the VM just reports the change.
    public void NearThreshold_Change_InvokesPersistCallback()
    {
        var persisted = 0.0;
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            persistDuplicateOptions: o => persisted = o.NearThreshold);

        vm.NearThreshold = 0.95;

        Assert.Equal(0.95, persisted);
    }

    [Fact] // No callback wired (the unit-test default) must not throw.
    public void NearThreshold_Change_WithoutPersistCallback_DoesNotThrow()
    {
        var vm = new TextTagValidationViewModel(scan: () => []);

        vm.NearThreshold = 0.80;

        Assert.Equal(0.80, vm.NearThreshold);
    }

    // ── Widened field scope (issue #14) ──────────────────────────────────────

    [Fact] // Default scope is the historical one: primary language, Default text.
    public void ScopeToggles_DefaultToOff()
    {
        var vm = new TextTagValidationViewModel(scan: () => []);

        Assert.False(vm.IncludeFemaleText);
        Assert.False(vm.IncludeOtherLanguages);
    }

    /// The constructor calls Refresh(), so every scope field must be assigned before it —
    /// otherwise the first scan runs with the wrong scope and the window opens showing a
    /// report the toggles do not describe.
    [Fact]
    public void ScopeToggles_InitialValues_ReachTheConstructorScan()
    {
        var seen = new List<DuplicateScanOptions>();
        _ = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: o => { seen.Add(o); return new DuplicateLineReport([], []); },
            duplicateOptions: new DuplicateScanOptions(0.75, true, true));

        var o = Assert.Single(seen);
        Assert.Equal(0.75, o.NearThreshold);
        Assert.True(o.IncludeFemaleText);
        Assert.True(o.IncludeOtherLanguages);
    }

    [Fact] // Modelled on CheckGameFiles_Toggle_PassesFlagToStaleScan.
    public void IncludeFemaleText_Toggle_PassesFlagToDupScan()
    {
        var seen = new List<DuplicateScanOptions>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: o => { seen.Add(o); return new DuplicateLineReport([], []); });
        seen.Clear();

        vm.IncludeFemaleText = true;

        Assert.True(Assert.Single(seen).IncludeFemaleText);
    }

    [Fact]
    public void IncludeOtherLanguages_Toggle_PassesFlagToDupScan()
    {
        var seen = new List<DuplicateScanOptions>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: o => { seen.Add(o); return new DuplicateLineReport([], []); });
        seen.Clear();

        vm.IncludeOtherLanguages = true;

        Assert.True(Assert.Single(seen).IncludeOtherLanguages);
    }

    [Fact] // All three options travel together, so one persist callback carries the lot.
    public void ScopeToggles_Change_InvokesPersistCallbackWithEveryOption()
    {
        DuplicateScanOptions? persisted = null;
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            persistDuplicateOptions: o => persisted = o);

        vm.NearThreshold       = 0.75;
        vm.IncludeFemaleText   = true;
        vm.IncludeOtherLanguages = true;

        Assert.NotNull(persisted);
        Assert.Equal(0.75, persisted!.NearThreshold);
        Assert.True(persisted.IncludeFemaleText);
        Assert.True(persisted.IncludeOtherLanguages);
    }

    [Fact] // No callback wired (the unit-test default) must not throw.
    public void ScopeToggles_Change_WithoutPersistCallback_DoesNotThrow()
    {
        var vm = new TextTagValidationViewModel(scan: () => []);

        vm.IncludeFemaleText     = true;
        vm.IncludeOtherLanguages = true;

        Assert.True(vm.IncludeFemaleText);
        Assert.True(vm.IncludeOtherLanguages);
    }

    /// A row must say WHERE a hit came from once it can come from somewhere other than
    /// primary Default text — otherwise "these two lines match" is unactionable. The test
    /// string provider echoes keys rather than formatting them, so assert on which key the
    /// annotation branch selected; the rendered wording is a localisation concern.
    [Theory]
    [InlineData("",   false, false)]  // primary language, default text -> no annotation
    [InlineData("",   true,  true)]   // female variant
    [InlineData("de", false, true)]   // other language
    [InlineData("de", true,  true)]   // both
    public void DuplicateRow_AnnotatesOnlyNonDefaultSources(
        string language, bool isFemale, bool expectAnnotation)
    {
        var report = new DuplicateLineReport(
            [new ExactDuplicateGroup("k", "some duplicated line here",
                [new LineRef("c1", 1, "some duplicated line here", language, isFemale),
                 new LineRef("c1", 2, "some duplicated line here", language, isFemale)])],
            []);

        var vm = new TextTagValidationViewModel(scan: () => [], dupScan: _ => report);

        var row = Assert.Single(vm.DuplicateRows);
        Assert.Equal(expectAnnotation, row.Locations.Contains("Duplicate_Source"));
    }
}
