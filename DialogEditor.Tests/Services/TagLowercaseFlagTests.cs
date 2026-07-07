using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class TagLowercaseFlagTests
{
    // Every entry whose notes advertise a lowercase form must carry Lowercase=true,
    // so the prose note and the machine-readable flag cannot drift apart.
    [Fact]
    public void EntriesWhoseNotesMentionLowercase_CarryTheFlag()
    {
        foreach (var entry in TagCatalogue.Instance.All)
        {
            var notesMentionLowercase =
                entry.Notes is not null &&
                entry.Notes.Contains("lowercase form", System.StringComparison.OrdinalIgnoreCase);

            if (notesMentionLowercase)
                Assert.True(entry.Lowercase,
                    $"'{entry.Name}' notes mention a lowercase form but Lowercase is false.");
        }
    }

    [Fact]
    public void PlayerRace_HasLowercaseFlag()
    {
        var race = System.Linq.Enumerable.First(
            TagCatalogue.Instance.All, e => e.Name == "[Player Race]");
        Assert.True(race.Lowercase);
    }

    [Fact]
    public void PlayerName_DoesNotHaveLowercaseFlag()
    {
        var name = System.Linq.Enumerable.First(
            TagCatalogue.Instance.All, e => e.Name == "[Player Name]");
        Assert.False(name.Lowercase);
    }
}
