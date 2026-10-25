using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 251: the toolbar's dialogue-text language and Settings' app language were both
/// called "Language", and the toolbar listed raw codes ("en").
/// </summary>
public class LanguageControlLabelTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"lang_settings_{Guid.NewGuid():N}.json");

    public LanguageControlLabelTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [Theory]
    [InlineData("fr", "Language_Name_fr")]   // StubStringProvider echoes the key
    [InlineData("xx", "xx")]                 // unknown code: shown as is
    public void LanguageNameConverter_ShowsTheName(string code, string expected) =>
        Assert.Equal(expected, new LanguageNameConverter().Convert(code, typeof(string), null, CultureInfo.InvariantCulture));

    [AvaloniaFact]
    public void ToolbarLanguageList_ShowsNames_NotCodes()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.AvailableLanguages = ["en", "fr"];
        vm.SelectedLanguage   = "en";
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>()
                          .Single(c => ReferenceEquals(c.ItemsSource, vm.AvailableLanguages));
        var shown = combo.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Language_Name_en", shown);
        Assert.DoesNotContain("en", shown);
    }

    [AvaloniaFact]
    public void TheTwoLanguageControls_HaveDifferentLabels()
    {
        Assert.True(Application.Current!.TryGetResource("Label_Language", null, out var text));
        Assert.True(Application.Current!.TryGetResource("Settings_Language", null, out var app));
        Assert.NotEqual(((string)text!).TrimEnd(':'), (string)app!);
    }
}
