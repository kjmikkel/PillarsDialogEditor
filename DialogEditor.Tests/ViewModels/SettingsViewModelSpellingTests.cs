using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class SettingsViewModelSpellingTests : IDisposable
{
    private readonly string _dir;

    public SettingsViewModelSpellingTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        _dir = Path.Combine(Path.GetTempPath(), $"setspell_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null && File.Exists(path)) File.Delete(path);
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private SpellDictionaryStore StoreWithEn()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DialogEditor.slnx")))
            root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(_dir, "en_US.aff"));
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(_dir, "en_US.dic"));
        return new SpellDictionaryStore(_dir, Path.Combine(_dir, "user.txt"));
    }

    [Fact]
    public void DictionariesFolder_ComesFromStore()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker(), spellStore: StoreWithEn());
        Assert.Equal(_dir, vm.DictionariesFolder);
    }

    [Fact]
    public void DetectedDictionaryLanguages_ListsStoreLanguages()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker(), spellStore: StoreWithEn());
        Assert.Contains("en", vm.DetectedDictionaryLanguages);
    }

    [Fact]
    public void OpenDictionariesFolder_CreatesFolder_AndInvokesOpener()
    {
        var missing = Path.Combine(_dir, "sub", "dictionaries");
        var store = new SpellDictionaryStore(missing, Path.Combine(_dir, "user.txt"));
        string? opened = null;
        var vm = new SettingsViewModel("/game", new StubFolderPicker(), spellStore: store)
        {
            FolderOpener = p => opened = p,
        };
        vm.OpenDictionariesFolderCommand.Execute(null);
        Assert.True(Directory.Exists(missing));
        Assert.Equal(missing, opened);
    }

    [Fact]
    public void OpenDictionarySource_InvokesUrlOpener_WithPinnedRepo()
    {
        string? opened = null;
        var vm = new SettingsViewModel("/game", new StubFolderPicker(), spellStore: StoreWithEn())
        {
            UrlOpener = u => opened = u,
        };
        vm.OpenDictionarySourceCommand.Execute(null);
        Assert.Equal("https://github.com/LibreOffice/dictionaries", opened);
    }

    [Fact]
    public void NoStore_SafeEmptyState()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        Assert.Equal(string.Empty, vm.DictionariesFolder);
        Assert.Empty(vm.DetectedDictionaryLanguages);
    }
}
