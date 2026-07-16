using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Docking;

public class WrapperDockableTests
{
    public WrapperDockableTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void BrowserTool_HasStableId_AndHostsInnerVm()
    {
        var inner = new GameBrowserViewModel(new StubDispatcher());
        var tool  = new BrowserTool(inner);
        Assert.Equal("Browser", tool.Id);
        Assert.Same(inner, tool.Inner);
        Assert.False(string.IsNullOrEmpty(tool.Title));
    }

    [Fact]
    public void CanvasDocument_CannotClose_AndHasCanvasId()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        var doc    = new CanvasDocument(canvas);
        Assert.Equal("Canvas", doc.Id);
        Assert.False(doc.CanClose);
        Assert.Same(canvas, doc.Inner);
    }
}
