using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

public class LanguageApplierTests
{
    [Fact]
    public void Available_ContainsEnglish()
    {
        var ids = new LanguageApplier().Available.Select(o => o.Id);
        Assert.Equal(["en"], ids);
    }

    [AvaloniaFact]
    public void Apply_English_BumpsRevision()
    {
        var before = LocaleService.Current.Revision;
        new LanguageApplier().Apply("en");
        Assert.Equal(before + 1, LocaleService.Current.Revision);
    }

    [AvaloniaFact]
    public void Apply_English_DoesNotAddToDictionaries()
    {
        var app    = Application.Current!;
        var before = app.Resources.MergedDictionaries.Count;
        new LanguageApplier().Apply("en");
        Assert.Equal(before, app.Resources.MergedDictionaries.Count);
    }

    [AvaloniaFact]
    public void Apply_UnknownCode_FallsBackToEnglish_AndBumpsRevision()
    {
        var app    = Application.Current!;
        var before = app.Resources.MergedDictionaries.Count;
        var rev    = LocaleService.Current.Revision;

        new LanguageApplier().Apply("zz-UNKNOWN");

        // No overlay injected (unknown code treated as "en")
        Assert.Equal(before, app.Resources.MergedDictionaries.Count);
        // Still bumped
        Assert.Equal(rev + 1, LocaleService.Current.Revision);
    }

    [AvaloniaFact]
    public void Apply_English_DisplayNameKeyResolvesViaStringProvider()
    {
        var applier  = new LanguageApplier();
        var english  = applier.Available.Single(o => o.Id == "en");
        var provider = new AvaloniaStringProvider();
        // The key must resolve (we registered the strings resource dictionary in the test app)
        Assert.NotEqual($"[{english.DisplayNameKey}]", provider.Get(english.DisplayNameKey));
    }
}
