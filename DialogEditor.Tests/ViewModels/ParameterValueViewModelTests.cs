using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ParameterValueViewModel"/> GUID-type behaviour:
/// <c>IsGuidType</c>, <c>GuidSuggestions</c>, and display-string normalisation.
/// </summary>
public class ParameterValueViewModelTests : IDisposable
{
    // Reset speaker data after each test so static state doesn't bleed between tests.
    public void Dispose() => SpeakerNameService.Register(new Dictionary<string, string>());

    private static ParameterValueViewModel Make(string type, string value = "") =>
        new() { Name = "p", Description = "", Type = type, Value = value };

    // ── IsGuidType ────────────────────────────────────────────────────────────

    [Fact]
    public void IsGuidType_True_ForObjectGuidType()
        => Assert.True(Make("ObjectGuid").IsGuidType);

    [Fact]
    public void IsGuidType_True_ForGuidType()
        => Assert.True(Make("Guid").IsGuidType);

    [Fact]
    public void IsGuidType_False_ForGameDataType()
        => Assert.False(Make("GameData").IsGuidType);

    [Fact]
    public void IsGuidType_False_ForStringType()
        => Assert.False(Make("String").IsGuidType);

    // ── IsText excludes GUID types ────────────────────────────────────────────

    [Fact]
    public void IsText_False_ForObjectGuidType()
        => Assert.False(Make("ObjectGuid").IsText);

    [Fact]
    public void IsText_False_ForGuidType()
        => Assert.False(Make("Guid").IsText);

    // ── GuidSuggestions ───────────────────────────────────────────────────────

    [Fact]
    public void GuidSuggestions_ContainsBuiltInPlayer()
    {
        var vm = Make("ObjectGuid");
        Assert.Contains(vm.GuidSuggestions, s => s.Contains("Player"));
    }

    [Fact]
    public void GuidSuggestions_ContainsBuiltInNarrator()
    {
        var vm = Make("ObjectGuid");
        Assert.Contains(vm.GuidSuggestions, s => s.Contains("Narrator"));
    }

    [Fact]
    public void GuidSuggestions_FormatContainsBothNameAndGuid()
    {
        SpeakerNameService.Register(new Dictionary<string, string>
        {
            { "aaaaaaaa-0000-0000-0000-000000000001", "Edér" },
        });
        var vm = Make("ObjectGuid");
        var match = vm.GuidSuggestions.Single(s => s.Contains("Edér"));
        Assert.Contains("aaaaaaaa-0000-0000-0000-000000000001", match);
    }

    [Fact]
    public void GuidSuggestions_SearchableByGuidFragment()
    {
        SpeakerNameService.Register(new Dictionary<string, string>
        {
            { "aaaaaaaa-0000-0000-0000-000000000001", "Edér" },
        });
        var vm = Make("ObjectGuid");
        // The suggestion string must contain the GUID so filtering by GUID prefix works
        Assert.Contains(vm.GuidSuggestions, s => s.Contains("aaaaaaaa"));
    }

    // ── Value normalisation ───────────────────────────────────────────────────

    [Fact]
    public void Value_NormalizesDisplayStringToGuid_WhenGuidType()
    {
        var vm = Make("ObjectGuid");
        vm.Value = "Player — b1a8e901-0000-0000-0000-000000000000";
        Assert.Equal("b1a8e901-0000-0000-0000-000000000000", vm.Value);
    }

    [Fact]
    public void Value_NotNormalized_WhenRawGuid()
    {
        var vm = Make("ObjectGuid");
        var guid = "b1a8e901-0000-0000-0000-000000000000";
        vm.Value = guid;
        Assert.Equal(guid, vm.Value);
    }

    [Fact]
    public void Value_NotNormalized_ForNonGuidType()
    {
        var vm = Make("String");
        vm.Value = "Something — not-a-guid";
        Assert.Equal("Something — not-a-guid", vm.Value);
    }

    [Fact]
    public void Value_NotNormalized_WhenSeparatorButInvalidGuid()
    {
        var vm = Make("ObjectGuid");
        vm.Value = "Player — not-actually-a-guid";
        Assert.Equal("Player — not-actually-a-guid", vm.Value);
    }
}
