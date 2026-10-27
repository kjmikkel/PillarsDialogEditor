using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class AppSettingsEditorModeTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settingsPath;

    public AppSettingsEditorModeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "editor-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // The file is deliberately not created: a missing file is the fresh-install case.
        _settingsPath = Path.Combine(_dir, "settings.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }

    [Fact] public void FreshInstall_HasNoMode() => Assert.Null(AppSettings.Mode);

    [Fact]
    public void Mode_RoundTrips_AsAString()
    {
        AppSettings.Mode = EditorMode.Reading;
        Assert.Equal(EditorMode.Reading, AppSettings.Mode);
        Assert.Contains("\"Reading\"", File.ReadAllText(_settingsPath));
    }

    [Fact] public void FreshInstall_HasNotSeenTheReaderTour() => Assert.False(AppSettings.ReaderTourSeen);

    [Fact]
    public void ExistingSettingsFile_WithoutTheKey_TreatsTheReaderTourAsSeen()
    {
        File.WriteAllText(_settingsPath, "{}");
        Assert.True(AppSettings.ReaderTourSeen);
    }

    [Fact]
    public void ExistingSettingsFile_WithoutMode_LoadsNull_SoTheChooserShowsForEveryone()
    {
        File.WriteAllText(_settingsPath, "{}");
        Assert.Null(AppSettings.Mode);
    }

    [Fact]
    public void Mode_Editing_RoundTrips()
    {
        AppSettings.Mode = EditorMode.Editing;
        Assert.Equal(EditorMode.Editing, AppSettings.Mode);
        Assert.Contains("\"Editing\"", File.ReadAllText(_settingsPath));
    }

    [Fact]
    public void CorruptSettingsFile_HasNotSeenTheReaderTour()
    {
        File.WriteAllText(_settingsPath, "{ this is not json");
        Assert.False(AppSettings.ReaderTourSeen);
    }
}
