using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 177: every window type in both apps is sized by WindowSizing. A window that
/// bypassed it would stay at its XAML pixel size while its text follows the font scale. Opens
/// each window type with a parameterless constructor at 2.0×.
/// </summary>
public class EveryWindowIsSizedTests : IDisposable
{
    public EveryWindowIsSizedTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    // Closing a ModeChooserWindow without a choice saves Reading mode (issue 234). These tests open
    // every window type, so without a private settings file that leaked Reading into the run-wide
    // fallback file and later tests started in Reading mode.
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"ewis_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static readonly Assembly[] Apps =
    [
        typeof(DialogEditor.Avalonia.App).Assembly,
        typeof(DialogEditor.Avalonia.Shared.ThemeOnboardingWindow).Assembly,
        typeof(DialogEditor.PatchManager.App).Assembly,
    ];

    public static IEnumerable<object[]> WindowTypes() =>
        Apps.SelectMany(a => a.GetTypes())
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName)
            .Select(t => new object[] { t.AssemblyQualifiedName! });

    [AvaloniaTheory]
    [MemberData(nameof(WindowTypes))]
    public void Window_IsSizedByWindowSizing(string typeName)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        var w = (Window)Activator.CreateInstance(Type.GetType(typeName)!)!;
        var designMinWidth = w.MinWidth;
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Assert.Equal(2.0, WindowSizing.GetAppliedScale(w));
            var expected = WindowSizing.GetMode(w) == WindowSizingMode.Scale ? designMinWidth * 2 : designMinWidth;
            Assert.Equal(expected, w.MinWidth);
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
