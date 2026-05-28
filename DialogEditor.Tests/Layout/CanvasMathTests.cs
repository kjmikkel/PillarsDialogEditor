using DialogEditor.Core.Layout;

namespace DialogEditor.Tests.Layout;

public class CanvasMathTests
{
    [Fact]
    public void ScreenToCanvas_IdentityZoom_OffsetsByOrigin()
    {
        var (x, y) = CanvasMath.ScreenToCanvas(100, 200, 1.0, 50, 75);
        Assert.Equal(150.0, x);
        Assert.Equal(275.0, y);
    }

    [Fact]
    public void ScreenToCanvas_ZoomTwo_HalvesScreenThenAddsOrigin()
    {
        var (x, y) = CanvasMath.ScreenToCanvas(100, 200, 2.0, 10, 20);
        Assert.Equal(60.0, x);   // 100/2 + 10
        Assert.Equal(120.0, y);  // 200/2 + 20
    }

    [Fact]
    public void ScreenToCanvas_ZeroOrigin_DividesScreenByZoom()
    {
        var (x, y) = CanvasMath.ScreenToCanvas(300, 150, 3.0, 0, 0);
        Assert.Equal(100.0, x);
        Assert.Equal(50.0,  y);
    }
}
