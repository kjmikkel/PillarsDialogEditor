using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// <summary>
/// B-003: the "Default localization format" ComboBox in Settings used static
/// &lt;ComboBoxItem&gt; children while binding SelectedItem to a string VM property —
/// a type mismatch that meant the saved value never showed as selected, and any
/// selection the user made could never be written back to AppSettings.
/// </summary>
public class SettingsWindowTests : IDisposable
{
    public SettingsWindowTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    [AvaloniaFact]
    public void LocalizationFormatComboBox_SelectedItem_ReflectsSavedValue()
    {
        AppSettings.DefaultLocalizationFormat = "Json";

        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel("/game", new StubFolderPicker())
        };
        window.Show();

        var combo = window.FindControl<ComboBox>("LocalizationFormatComboBox")!;
        Assert.Equal("Json", combo.SelectedItem);
    }

    [AvaloniaFact]
    public void LocalizationFormatComboBox_Selecting_PersistsToAppSettings()
    {
        AppSettings.DefaultLocalizationFormat = "Csv";

        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel("/game", new StubFolderPicker())
        };
        window.Show();

        var combo = window.FindControl<ComboBox>("LocalizationFormatComboBox")!;
        combo.SelectedItem = "Xliff";

        Assert.Equal("Xliff", AppSettings.DefaultLocalizationFormat);
    }
}

/// The autosave pickers (issue #11): both bind int options to an int VM property, the
/// same type-match trap B-003 was about.
public class SettingsWindowAutosaveTests : IDisposable
{
    public SettingsWindowAutosaveTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    private static SettingsWindow ShowWindow()
    {
        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel("/game", new StubFolderPicker())
        };
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void AutosaveIntervalComboBox_SelectedItem_ReflectsSavedValue()
    {
        AppSettings.AutosaveIntervalSeconds = 300;
        var combo = ShowWindow().FindControl<ComboBox>("AutosaveIntervalComboBox")!;
        Assert.Equal(300, combo.SelectedItem);
    }

    [AvaloniaFact]
    public void AutosaveIntervalComboBox_OffIsSelectable()
    {
        AppSettings.AutosaveIntervalSeconds = 0;
        var combo = ShowWindow().FindControl<ComboBox>("AutosaveIntervalComboBox")!;
        Assert.Equal(0, combo.SelectedItem);
    }

    [AvaloniaFact]
    public void AutosaveGenerationsComboBox_SelectedItem_ReflectsSavedValue()
    {
        AppSettings.AutosaveGenerations = 5;
        var combo = ShowWindow().FindControl<ComboBox>("AutosaveGenerationsComboBox")!;
        Assert.Equal(5, combo.SelectedItem);
    }

    [AvaloniaFact]
    public void AutosaveIntervalComboBox_SelectingAnOption_PersistsIt()
    {
        var combo = ShowWindow().FindControl<ComboBox>("AutosaveIntervalComboBox")!;
        combo.SelectedItem = 120;
        Assert.Equal(120, AppSettings.AutosaveIntervalSeconds);
    }
}
