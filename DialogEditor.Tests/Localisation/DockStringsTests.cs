using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;

namespace DialogEditor.Tests.Localisation;

/// Issue 8: the docking shell's own menu strings (Float, Dock, Auto Hide, Close, the tab-strip
/// menus) ship inside Dock.Avalonia as named resources. The app redefines every user-facing one
/// in its own Strings.axaml, so a UI translation overlay translates them along with the rest of
/// the editor instead of leaving them in English.
public class DockStringsTests
{
    private const string DockStrings = "avares://Dock.Avalonia.Themes.Fluent/Controls/ControlStrings.axaml";
    private const string AppStrings  = "avares://DialogEditor.Avalonia/Resources/Strings.axaml";

    // The RootDockDebug* strings belong to Dock's developer debug overlay (Ctrl+Shift picker),
    // which users never see, so they are deliberately not redefined.
    private static bool IsUserFacing(object key) => key is string s && !s.StartsWith("RootDockDebug", StringComparison.Ordinal);

    private static ResourceDictionary Load(string uri) => (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri));

    [AvaloniaFact]
    public void EveryUserFacingDockString_IsDefinedInTheAppsStrings()
    {
        var dock = Load(DockStrings);
        var app  = Load(AppStrings);

        var missing = dock.Keys.Where(IsUserFacing).Where(k => !app.ContainsKey(k)).Select(k => (string)k).OrderBy(k => k).ToList();

        Assert.True(missing.Count == 0, "Dock strings not in Strings.axaml: " + string.Join(", ", missing));
    }

    [AvaloniaFact]
    public void DockMenus_ResolveToTheAppsStrings()
    {
        // App resources are searched before the Dock theme's, so the app's copy is what the
        // menus show, and an overlay that replaces Strings.axaml replaces these too.
        var app = Load(AppStrings);
        Assert.True(Application.Current!.TryGetResource("ToolChromeControlAutoHideString", null, out var value));
        Assert.Equal(app["ToolChromeControlAutoHideString"], value);
    }
}
