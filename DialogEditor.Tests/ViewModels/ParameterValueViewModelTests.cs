// DialogEditor.Tests/ViewModels/ParameterValueViewModelTests.cs
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class ParameterValueViewModelTests : IDisposable
{
    public void Dispose()
    {
        SpeakerNameService.Register(new Dictionary<string, string>());
        GameDataNameService.Clear();
    }

    private static ParameterValueViewModel Make(
        string type, string lookupKind = "", string value = "") =>
        new() { Name = "p", Description = "", Type = type,
                LookupKind = lookupKind, Value = value };

    // ── HasLookup ──────────────────────────────────────────────────────────

    [Fact]
    public void HasLookup_EmptyLookupKind_ReturnsFalse()
        => Assert.False(Make("Guid").HasLookup);

    [Fact]
    public void HasLookup_NonEmptyLookupKind_ReturnsTrue()
        => Assert.True(Make("Guid", "Quest").HasLookup);

    // ── IsText ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsText_True_WhenNoLookupAndNotEnum()
        => Assert.True(Make("String").IsText);

    [Fact]
    public void IsText_False_WhenHasLookup()
        => Assert.False(Make("GlobalVariable", "GlobalVariable").IsText);

    [Fact]
    public void IsText_False_WhenEnum()
        => Assert.False(Make("Boolean").IsText);

    // ── Suggestions ────────────────────────────────────────────────────────

    [Fact]
    public void Suggestions_EmptyWhenNoLookup()
        => Assert.Empty(Make("Guid").Suggestions);

    [Fact]
    public void Suggestions_ReturnsDisplayNames_FromRegisteredKind()
    {
        GameDataNameService.Register("Quest",
            [new NamedEntry("A Quest — cccccccc-0000-0000-0000-000000000003",
                            "cccccccc-0000-0000-0000-000000000003")]);
        Assert.Equal(
            ["A Quest — cccccccc-0000-0000-0000-000000000003"],
            Make("Guid", "Quest").Suggestions);
    }

    [Fact]
    public void Suggestions_EmptyWhenKindNotRegistered()
        => Assert.Empty(Make("Guid", "Quest").Suggestions);

    // ── OnValueChanged normalisation ───────────────────────────────────────

    [Fact]
    public void Value_WritesStoredValue_WhenDisplayNameSelected()
    {
        const string guid = "cccccccc-0000-0000-0000-000000000003";
        GameDataNameService.Register("Quest",
            [new NamedEntry($"A Quest — {guid}", guid)]);
        var vm = Make("Guid", "Quest");
        vm.Value = $"A Quest — {guid}";
        Assert.Equal(guid, vm.Value);
    }

    [Fact]
    public void Value_PreservesRaw_WhenNoMatchingEntry()
    {
        GameDataNameService.Register("Quest",
            [new NamedEntry("A Quest — abc", "abc")]);
        var vm = Make("Guid", "Quest");
        vm.Value = "00000000-0000-0000-0000-999999999999";
        Assert.Equal("00000000-0000-0000-0000-999999999999", vm.Value);
    }

    [Fact]
    public void Value_Unchanged_WhenNoLookup()
    {
        var vm = Make("String");
        vm.Value = "Something — not-a-guid";
        Assert.Equal("Something — not-a-guid", vm.Value);
    }

    [Fact]
    public void Value_WritesStoredValue_ForStringKind()
    {
        // For GlobalVariable: DisplayName == StoredValue, so lookup is identity.
        GameDataNameService.Register("GlobalVariable",
            [new NamedEntry("npc_met_eder", "npc_met_eder")]);
        var vm = Make("GlobalVariable", "GlobalVariable");
        vm.Value = "npc_met_eder";
        Assert.Equal("npc_met_eder", vm.Value);
    }

    // ── Speaker lookup ─────────────────────────────────────────────────────

    [Fact]
    public void Suggestions_ContainsBuiltins_WhenSpeakerKindRegistered()
    {
        var speakers = SpeakerNameService.All
            .Select(s => new NamedEntry($"{s.Name} — {s.Guid}", s.Guid))
            .ToList();
        GameDataNameService.Register("Speaker", speakers);
        var vm = Make("ObjectGuid", "Speaker");
        Assert.Contains(vm.Suggestions, s => s.Contains("Player"));
        Assert.Contains(vm.Suggestions, s => s.Contains("Narrator"));
    }
}
