using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

// GitHub issue 257: an unresolved speaker is shown as a readable label, never a bare GUID.
public class SpeakerNameServiceTests
{
    private const string Unknown = "c0a1c0a1-0000-4000-8000-000000000000";
    private const string Player  = "b1a8e901-0000-0000-0000-000000000000";
    private const string Narrator = "00000000-0000-0000-0000-000000000000";

    private sealed class TemplateProvider : IStringProvider
    {
        public string Get(string key) => key == "Speaker_UnknownWithId" ? "Unknown speaker ({0})" : key;
        public bool TryGet(string key, out string value) { value = Get(key); return true; }
    }

    public SpeakerNameServiceTests()
    {
        Loc.Configure(new TemplateProvider());
        SpeakerNameService.Register(new Dictionary<string, string>());
    }

    [Fact]
    public void Resolve_UnknownGuid_GivesAReadableLabelWithAShortId()
        => Assert.Equal("Unknown speaker (c0a1c0a1…)", SpeakerNameService.Resolve(Unknown));

    [Fact]
    public void Resolve_RegisteredGuid_GivesTheName()
    {
        SpeakerNameService.Register(new Dictionary<string, string> { [Unknown] = "Serafen" });
        Assert.Equal("Serafen", SpeakerNameService.Resolve(Unknown));
    }

    [Fact]
    public void Resolve_BlankGuid_GivesNull()
        => Assert.Null(SpeakerNameService.Resolve(""));

    [Fact]
    public void IsKnown_FalseForAnUnregisteredGuid_TrueForABuiltIn()
    {
        Assert.False(SpeakerNameService.IsKnown(Unknown));
        Assert.True(SpeakerNameService.IsKnown(Player));
    }

    [Theory]
    [InlineData(Player,   SpeakerCategory.Player)]
    [InlineData(Narrator, SpeakerCategory.Narrator)]
    public void BuiltInCategory_NamesTheBuiltInSpeakers(string guid, SpeakerCategory expected)
        => Assert.Equal(expected, SpeakerNameService.BuiltInCategory(guid));

    [Fact]
    public void BuiltInCategory_NullForAnyOtherGuid()
        => Assert.Null(SpeakerNameService.BuiltInCategory(Unknown));
}
