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
}
