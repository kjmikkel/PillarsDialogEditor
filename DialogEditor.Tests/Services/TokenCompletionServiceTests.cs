using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class TokenCompletionServiceTests
{
    private readonly TokenCompletionService _svc = new();

    [Fact]
    public void TryGetContext_OpenBracket_ReturnsTokenContext()
    {
        var ctx = _svc.TryGetContext("hello [Pla", 10);
        Assert.NotNull(ctx);
        Assert.Equal('[', ctx!.Delimiter);
        Assert.Equal(6, ctx.FragmentStart);
        Assert.Equal("[Pla", ctx.Fragment);
    }

    [Fact]
    public void TryGetContext_OpenAngle_ReturnsMarkupContext()
    {
        var ctx = _svc.TryGetContext("say <is", 7);
        Assert.NotNull(ctx);
        Assert.Equal('<', ctx!.Delimiter);
        Assert.Equal("<is", ctx.Fragment);
    }

    [Fact]
    public void TryGetContext_AfterClosingBracket_ReturnsNull()
        => Assert.Null(_svc.TryGetContext("[Player Name]", 13));

    [Fact]
    public void TryGetContext_AfterClosedTag_ReturnsNull()
        => Assert.Null(_svc.TryGetContext("<i>text", 7));

    [Fact]
    public void TryGetContext_NoOpener_ReturnsNull()
        => Assert.Null(_svc.TryGetContext("plain text", 5));

    [Fact]
    public void TryGetContext_StopsAtNewline()
        => Assert.Null(_svc.TryGetContext("[unclosed\nnext", 14));

    [Fact]
    public void TryGetContext_SecondOpenBracket_UsesNearest()
    {
        var ctx = _svc.TryGetContext("[Player Name] [Sl", 17);
        Assert.NotNull(ctx);
        Assert.Equal(14, ctx!.FragmentStart);
        Assert.Equal("[Sl", ctx.Fragment);
    }

    [Fact]
    public void TryGetContext_SpaceInsideToken_DoesNotDismiss()
    {
        var ctx = _svc.TryGetContext("[Player Na", 10);
        Assert.NotNull(ctx);
        Assert.Equal("[Player Na", ctx!.Fragment);
    }

    [Fact]
    public void GetCandidates_BracketOffersTokens_NotMarkupOrConvention()
    {
        var ctx = _svc.TryGetContext("[", 1)!;
        var names = _svc.GetCandidates(ctx, "poe2").Select(e => e.Name).ToList();
        Assert.Contains("[Player Name]", names);
        Assert.DoesNotContain(names, n => n.StartsWith('<'));
        Assert.DoesNotContain("Stage directions", names); // Convention never offered
    }

    [Fact]
    public void GetCandidates_AngleOffersMarkup()
    {
        var ctx = _svc.TryGetContext("<", 1)!;
        var names = _svc.GetCandidates(ctx, "poe2").Select(e => e.Name).ToList();
        Assert.Contains("<i>…</i>", names);
        Assert.All(names, n => Assert.StartsWith("<", n));
    }

    [Fact]
    public void GetCandidates_Poe1_ExcludesShipDuelTokens()
    {
        var ctx = _svc.TryGetContext("[Ship", 5)!;
        var names = _svc.GetCandidates(ctx, "poe1").Select(e => e.Name).ToList();
        Assert.DoesNotContain(names, n => n.StartsWith("[ShipDuel_"));
    }

    [Fact]
    public void GetCandidates_UnknownGame_OffersUnion()
    {
        var ctx = _svc.TryGetContext("[Ship", 5)!;
        var names = _svc.GetCandidates(ctx, "").Select(e => e.Name).ToList();
        Assert.Contains(names, n => n.StartsWith("[ShipDuel_")); // poe2-only entry still offered
    }

    [Fact]
    public void GetCandidates_PrefixMatch_IsCaseInsensitive()
    {
        var ctx = _svc.TryGetContext("[pla", 4)!;
        var names = _svc.GetCandidates(ctx, "poe2").Select(e => e.Name).ToList();
        Assert.Contains("[Player Name]", names);
    }

    [Fact]
    public void GetCandidates_RankedByShippedCountDescending()
    {
        var ctx = _svc.TryGetContext("[S", 2)!;
        var cands = _svc.GetCandidates(ctx, "poe2");
        // [Specified n] (count 2091) outranks [SkillCheck n] (238) and [Slot n] (717)
        for (var i = 1; i < cands.Count; i++)
            Assert.True(cands[i - 1].Count >= cands[i].Count);
    }

    [Fact]
    public void GetCandidates_NoMatch_ReturnsEmpty()
    {
        var ctx = _svc.TryGetContext("[Zzz", 4)!;
        Assert.Empty(_svc.GetCandidates(ctx, "poe2"));
    }

    private static TagEntry Entry(string name)
        => TagCatalogue.Instance.All.Single(e => e.Name == name);

    [Fact]
    public void ApplyCompletion_PlainToken_CaretAtEnd()
    {
        var ctx = _svc.TryGetContext("[Pla", 4)!;
        var edit = _svc.ApplyCompletion(ctx, Entry("[Player Name]"));
        Assert.Equal(0, edit.ReplaceStart);
        Assert.Equal(4, edit.ReplaceLength);
        Assert.Equal("[Player Name]", edit.InsertedText);
        Assert.Equal("[Player Name]".Length, edit.SelectionStart);
        Assert.Equal(0, edit.SelectionLength);
    }

    [Fact]
    public void ApplyCompletion_ParameterisedToken_SelectsNumber()
    {
        var ctx = _svc.TryGetContext("[Spe", 4)!;
        var edit = _svc.ApplyCompletion(ctx, Entry("[Specified n]"));
        Assert.Equal("[Specified 0]", edit.InsertedText);
        Assert.Equal("[Specified ".Length, edit.SelectionStart); // caret on the '0'
        Assert.Equal(1, edit.SelectionLength);
    }

    [Fact]
    public void ApplyCompletion_PairedMarkup_CaretBetweenTags()
    {
        var ctx = _svc.TryGetContext("<i", 2)!;
        var edit = _svc.ApplyCompletion(ctx, Entry("<i>…</i>"));
        Assert.Equal("<i></i>", edit.InsertedText);
        Assert.Equal("<i>".Length, edit.SelectionStart);
        Assert.Equal(0, edit.SelectionLength);
    }

    [Fact]
    public void ApplyCompletion_AttributeMarkup_CaretInsideQuotes()
    {
        var ctx = _svc.TryGetContext("<col", 4)!;
        var edit = _svc.ApplyCompletion(ctx, Entry("<color=\"…\">…</color>"));
        Assert.Equal("<color=\"\"></color>", edit.InsertedText);
        Assert.Equal("<color=\"".Length, edit.SelectionStart);
        Assert.Equal(0, edit.SelectionLength);
    }
}
