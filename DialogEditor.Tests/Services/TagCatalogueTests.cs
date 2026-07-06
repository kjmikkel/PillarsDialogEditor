using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// The dialog-text tag vocabulary (tags.json) is engine-verified: token lists
/// come from both games' decompiled Conversation.cs (+ PoE2 ShipDuelManager.cs),
/// not from scanning shipped text. Count == 0 on a Token means engine-supported
/// but unused in shipped dialog. Lowercase variants ([player race]) are notes on
/// the base token, never separate entries.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public class TagCatalogueTests
{
    private static TagCatalogue Cat => TagCatalogue.Instance;

    [Fact]
    public void LoadEmbedded_ContainsKnownEntries()
    {
        Assert.NotEmpty(Cat.All);

        var playerName = Cat.All.Single(e => e.Name == "[Player Name]");
        Assert.Equal("Token", playerName.Kind);
        Assert.Contains("poe1", playerName.Games);
        Assert.Contains("poe2", playerName.Games);

        var shipDuelPlayer = Cat.All.Single(e => e.Name == "[ShipDuel_Player]");
        Assert.Equal(new[] { "poe2" }, shipDuelPlayer.Games);
        Assert.Equal(0, shipDuelPlayer.Count);

        var ispeech = Cat.All.Single(e => e.Name == "<ispeech>…</ispeech>");
        Assert.Equal("Markup", ispeech.Kind);
        Assert.Equal(new[] { "poe2" }, ispeech.Games);
    }

    [Fact]
    public void ForGame_FiltersByGame()
    {
        var poe1 = Cat.ForGame("poe1");
        var poe2 = Cat.ForGame("poe2");

        Assert.DoesNotContain(poe1, e => e.Name.StartsWith("[ShipDuel_"));
        Assert.DoesNotContain(poe1, e => e.Kind == "Markup");
        Assert.Contains(poe2, e => e.Name.StartsWith("[ShipDuel_"));
        Assert.Contains(poe2, e => e.Kind == "Markup");
        Assert.Contains(poe1, e => e.Name == "[Player Name]");
    }

    [Fact]
    public void Entries_AreStructurallySound()
    {
        foreach (var e in Cat.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.Contains(e.Kind, new[] { "Token", "Markup", "Convention" });
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
            Assert.NotEmpty(e.Games);
            Assert.All(e.Games, g => Assert.Contains(g, new[] { "poe1", "poe2" }));
            Assert.True(e.Count >= 0);
        }
    }

    [Fact]
    public void LowercaseVariants_AreNotesNotEntries()
    {
        // PoE2's [player race]-style lowercase pairs are documented in Notes on
        // the base token; they must never appear as separate entries.
        Assert.DoesNotContain(Cat.All, e => e.Name.StartsWith("[player "));

        var race = Cat.All.Single(e => e.Name == "[Player Race]");
        Assert.NotNull(race.Notes);
        Assert.Contains("[player race]", race.Notes!);
    }
}
