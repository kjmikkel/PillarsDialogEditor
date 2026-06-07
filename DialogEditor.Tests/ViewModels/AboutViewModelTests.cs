using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class AboutViewModelTests
{
    public AboutViewModelTests() => Loc.Configure(new StubStringProvider());

    private static AboutViewModel Make(System.Func<string, bool> opener)
        => new("9.9.9", "https://repo", "https://docs") { UrlOpener = opener };

    [Fact]
    public void Version_IsSurfaced()
    {
        Assert.Equal("9.9.9", Make(_ => true).Version);
    }

    [Fact]
    public void OpenRepository_InvokesOpenerWithRepoUrl()
    {
        string? opened = null;
        var vm = Make(url => { opened = url; return true; });

        vm.OpenRepositoryCommand.Execute(null);

        Assert.Equal("https://repo", opened);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public void OpenDocs_OnFailure_SetsLocalizedStatus()
    {
        var vm = Make(_ => false);

        vm.OpenDocsCommand.Execute(null);

        Assert.Equal("About_OpenFailed", vm.Status); // StubStringProvider echoes the key
    }
}
