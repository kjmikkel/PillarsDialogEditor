using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

public class LegendWindowTests
{
    [AvaloniaFact]
    public void OnClosing_HidesWindowAndFiresCallback()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        try
        {
            var window = new LegendWindow();
            var callbackFired = false;
            window.OnHidden = () => callbackFired = true;
            window.Show();
            window.Close();
            Assert.True(callbackFired);
        }
        finally
        {
            var path = AppSettings.SettingsPathOverride;
            AppSettings.SettingsPathOverride = null;
            if (path != null) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ShowAndRestore_WithSavedPosition_RestoresPosition()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        try
        {
            AppSettings.SetLegendPosition(200, 300);
            var owner = new Window();
            owner.Show();
            var window = new LegendWindow();
            window.ShowAndRestore(owner);
            Assert.Equal(new PixelPoint(200, 300), window.Position);
        }
        finally
        {
            var path = AppSettings.SettingsPathOverride;
            AppSettings.SettingsPathOverride = null;
            if (path != null) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ShowAndRestore_NoSavedPosition_DoesNotThrow()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        try
        {
            var owner = new Window();
            owner.Show();
            var window = new LegendWindow();
            var ex = Record.Exception(() => window.ShowAndRestore(owner));
            Assert.Null(ex);
        }
        finally
        {
            var path = AppSettings.SettingsPathOverride;
            AppSettings.SettingsPathOverride = null;
            if (path != null) File.Delete(path);
        }
    }
}
