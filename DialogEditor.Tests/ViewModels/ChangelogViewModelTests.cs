using System;
using System.Collections.Generic;
using DialogEditor.Patch.Changelog;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class ChangelogViewModelTests
{
    public ChangelogViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void NoReleases_IsEmpty_WithLocalizedMessage()
    {
        var vm = new ChangelogViewModel(Array.Empty<ChangelogRelease>());

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasReleases);
        Assert.Equal("Changelog_Empty", vm.EmptyMessage); // StubStringProvider echoes the key
    }

    [Fact]
    public void WithReleases_HasReleases()
    {
        var releases = new List<ChangelogRelease>
        {
            new("1.0.0", "2026-04-01",
                new[] { new ChangelogSection("Added", new[] { "Thing" }) }),
        };

        var vm = new ChangelogViewModel(releases);

        Assert.False(vm.IsEmpty);
        Assert.True(vm.HasReleases);
        Assert.Same(releases, vm.Releases);
    }

    [Fact]
    public void Default_IsNotWhatsNew()
    {
        var vm = new ChangelogViewModel(Array.Empty<ChangelogRelease>());
        Assert.False(vm.IsWhatsNew);
        Assert.Equal("Changelog_Title", vm.WindowTitle); // stub echoes the key
        Assert.False(vm.ShowHeader);
    }

    [Fact]
    public void WhatsNewMode_SetsFlagTitleAndHeader()
    {
        var vm = new ChangelogViewModel(
            Array.Empty<ChangelogRelease>(), isWhatsNew: true, version: "1.2.0");
        Assert.True(vm.IsWhatsNew);
        Assert.Equal("WhatsNew_Title", vm.WindowTitle);
        Assert.True(vm.ShowHeader);
    }
}
