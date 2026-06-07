using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch.Changelog;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class ChangelogWindowTests
{
    public ChangelogWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Constructs_WithReleases()
    {
        var releases = new List<ChangelogRelease>
        {
            new("1.0.0", "2026-04-01",
                new[] { new ChangelogSection("Added", new[] { "Thing" }) }),
        };
        var window = new ChangelogWindow(new ChangelogViewModel(releases));
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Constructs_WhenEmpty()
    {
        var window = new ChangelogWindow(new ChangelogViewModel(Array.Empty<ChangelogRelease>()));
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }
}
