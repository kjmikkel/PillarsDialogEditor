using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Game-aware, searchable view over TagCatalogue. StubStringProvider echoes
/// keys, so localised headers/badges assert against the resource key itself.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public class TagReferenceViewModelTests
{
    public TagReferenceViewModelTests() => Loc.Configure(new StubStringProvider());

    private static TagReferenceViewModel MakeVm(string gameId = "") => new(gameId);

    private static IEnumerable<TagRowViewModel> AllTokenRows(TagReferenceViewModel vm)
        => vm.TokenGroups.SelectMany(g => g.Rows);

    [Fact]
    public void InitialGame_FollowsActiveGame_DefaultsToPoE2()
    {
        Assert.Equal(TagGameFilter.PoE1, MakeVm("poe1").SelectedGame.Value);
        Assert.Equal(TagGameFilter.PoE2, MakeVm("poe2").SelectedGame.Value);
        Assert.Equal(TagGameFilter.PoE2, MakeVm().SelectedGame.Value);
    }

    [Fact]
    public void PoE1_HidesShipDuelTokensAndMarkup()
    {
        var vm = MakeVm("poe1");

        Assert.DoesNotContain(vm.TokenGroups, g => g.Header == "TagCategory_ShipDuel");
        Assert.False(vm.HasMarkup);
        Assert.True(vm.HasTokens);
        Assert.True(vm.HasConventions);   // stage directions + VO annotations
    }

    [Fact]
    public void Both_ShowsUnionWithGameBadges()
    {
        var vm = MakeVm();
        vm.SelectedGame = vm.GameOptions.Single(o => o.Value == TagGameFilter.Both);

        Assert.Contains(vm.TokenGroups, g => g.Header == "TagCategory_ShipDuel");
        var playerName = AllTokenRows(vm).Single(r => r.Name == "[Player Name]");
        Assert.True(playerName.ShowBadge);
        Assert.Equal("TagRef_BadgePoE1 · TagRef_BadgePoE2", playerName.GamesBadge);
    }

    [Fact]
    public void Search_FiltersNameAndDescription_CaseInsensitive()
    {
        var vm = MakeVm("poe2");

        vm.SearchText = "ISPEECH";
        Assert.Empty(vm.TokenGroups);
        Assert.Equal("<ispeech>…</ispeech>", Assert.Single(vm.MarkupRows).Name);

        vm.SearchText = "watcher";   // matches token descriptions only
        Assert.Contains(AllTokenRows(vm), r => r.Name == "[Player Name]");
        Assert.False(vm.HasMarkup);

        vm.SearchText = "";
        Assert.True(vm.HasMarkup);
        Assert.True(vm.HasTokens);
    }

    [Fact]
    public void TokenGroups_AppearInFixedCategoryOrder()
    {
        var vm = MakeVm("poe2");
        Assert.Equal(
            new[] { "TagCategory_Player", "TagCategory_CharacterReference", "TagCategory_ShipDuel", "TagCategory_Other" },
            vm.TokenGroups.Select(g => g.Header).ToArray());
    }

    [Fact]
    public void NoResults_WhenSearchMatchesNothing()
    {
        var vm = MakeVm("poe2");
        vm.SearchText = "zzz_no_such_tag";
        Assert.True(vm.HasNoResults);
    }

    [Fact]
    public void EngineOnlyBadge_OnZeroCountTokensOnly()
    {
        var vm = MakeVm("poe2");
        Assert.True(AllTokenRows(vm).Single(r => r.Name == "[ShipDuel_Player]").IsEngineOnly);
        Assert.False(AllTokenRows(vm).Single(r => r.Name == "[Player Name]").IsEngineOnly);
        // Conventions have count 0 but are not "engine-only" tokens.
        Assert.DoesNotContain(vm.ConventionRows, r => r.IsEngineOnly);
    }
}
