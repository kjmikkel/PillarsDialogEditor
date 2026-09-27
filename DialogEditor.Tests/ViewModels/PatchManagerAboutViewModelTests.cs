using DialogEditor.Patch.Schema;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// The standalone Patch Manager's About window (GitHub issue 79): version, the file
/// formats this patcher reads, and where to get a newer one.
public class PatchManagerAboutViewModelTests
{
    public PatchManagerAboutViewModelTests() => Loc.Configure(new EchoStringProvider(new()
    {
        ["PatchManagerAbout_FormatLine"] = "{0} {1} or earlier",
        ["Schema_Format_Project"]           = "project format",
        ["Schema_Format_ConversationPatch"] = "conversation patch format",
        ["Schema_Format_PatchList"]         = "load-order format",
    }));

    private static PatchManagerAboutViewModel Make(Func<string, bool>? opener = null)
        => new("2.0.1") { UrlOpener = opener ?? (_ => true) };

    [Fact]
    public void Version_IsSurfaced()
        => Assert.Equal("2.0.1", Make().Version);

    [Fact]
    public void SupportedFormats_HasOneLocalisedLinePerFormat()
        => Assert.Equal(
            SchemaFormats.Supported.Select(f => $"{Loc.Get(FormatKey(f.Kind))} {f.Version} or earlier"),
            Make().SupportedFormats);

    private static string FormatKey(SchemaFileKind kind) => kind switch
    {
        SchemaFileKind.Project           => "Schema_Format_Project",
        SchemaFileKind.ConversationPatch => "Schema_Format_ConversationPatch",
        _                                => "Schema_Format_PatchList",
    };

    [Fact]
    public void OpenReleases_OpensThePatcherReleasesPage()
    {
        string? opened = null;
        var vm = Make(url => { opened = url; return true; });

        vm.OpenReleasesCommand.Execute(null);

        Assert.Equal(SchemaFormats.PatcherReleasesUrl, opened);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public void OpenRepository_OpensTheRepository()
    {
        string? opened = null;
        var vm = Make(url => { opened = url; return true; });

        vm.OpenRepositoryCommand.Execute(null);

        Assert.Equal("https://github.com/kjmikkel/PillarsDialogEditor", opened);
    }

    [Fact]
    public void OpenFailure_SetsLocalisedStatus()
    {
        var vm = Make(_ => false);

        vm.OpenReleasesCommand.Execute(null);

        Assert.Equal("PatchManagerAbout_OpenFailed", vm.Status);
    }
}
