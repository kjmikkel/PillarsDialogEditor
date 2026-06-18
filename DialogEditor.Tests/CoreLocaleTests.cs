using DialogEditor.Core.Resources;

namespace DialogEditor.Tests;

public class CoreLocaleTests
{
    [Fact]
    public void SetCulture_English_DoesNotThrow()
    {
        // Culture null means ResourceManager uses the invariant/English .resx
        var ex = Record.Exception(() => CoreLocale.SetCulture("en"));
        Assert.Null(ex);
    }

    [Fact]
    public void SetCulture_Null_DoesNotThrow()
    {
        var ex = Record.Exception(() => CoreLocale.SetCulture(null));
        Assert.Null(ex);
    }

    [Fact]
    public void SetCulture_InvalidCode_DoesNotThrow()
    {
        // Invalid culture codes should be caught internally and not crash the app.
        var ex = Record.Exception(() => CoreLocale.SetCulture("zz-INVALID-99"));
        Assert.Null(ex);
    }
}
