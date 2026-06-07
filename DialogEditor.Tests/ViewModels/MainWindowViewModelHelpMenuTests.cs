using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelHelpMenuTests
{
    public MainWindowViewModelHelpMenuTests() => Loc.Configure(new StubStringProvider());

    private static MainWindowViewModel Make() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    [Fact]
    public void ChangelogCommand_ReadsViaSeam_ParsesAndShows()
    {
        var vm = Make();
        vm.ChangelogReader = () => "## [1.0.0] — 2026-04-01\n### Added\n- Hi\n";
        ChangelogViewModel? shown = null;
        vm.ShowChangelog = cl => shown = cl;

        vm.ChangelogCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.True(shown!.HasReleases);
        Assert.Equal("1.0.0", Assert.Single(shown.Releases).Version);
    }

    [Fact]
    public void ChangelogCommand_MissingFile_ShowsEmpty()
    {
        var vm = Make();
        vm.ChangelogReader = () => null;
        ChangelogViewModel? shown = null;
        vm.ShowChangelog = cl => shown = cl;

        vm.ChangelogCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.True(shown!.IsEmpty);
    }

    [Fact]
    public void AboutCommand_BuildsViewModelWithVersion()
    {
        var vm = Make();
        AboutViewModel? shown = null;
        vm.ShowAbout = a => shown = a;

        vm.AboutCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.False(string.IsNullOrEmpty(shown!.Version));
        Assert.Equal(MainWindowViewModel.RepositoryUrl, shown.RepositoryUrl);
    }
}
