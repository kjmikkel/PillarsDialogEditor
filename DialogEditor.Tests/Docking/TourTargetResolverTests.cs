using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Dock.Model.Core;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Docking;

/// <summary>
/// Issue #10: the guided tour's Browser / Details steps must resolve their targets without
/// a compile-time name, because Dock instantiates tool content lazily from
/// Application.DataTemplates and that content carries no x:Name in MainWindow's scope.
///
/// These tests pin the three links a docked tour step depends on, each of which broke
/// silently (a ring on nothing, no exception) when the docking shell landed:
///   step TargetName -> dock tool Id (so a closed tool can be revealed first)
///   dock tool Id    -> the view TYPE Dock actually renders for it (via DataTemplates)
///   view TYPE       -> the live control, found across the main window AND floating hosts
/// </summary>
public class TourTargetResolverTests
{
    public TourTargetResolverTests() => Loc.Configure(new StubStringProvider());

    private static EditorDockFactory MakeFactory() => new(
        new GameBrowserViewModel(new StubDispatcher()),
        new ConversationViewModel(new StubDispatcher()),
        new NodeDetailViewModel(),
        new ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));

    private static IEnumerable<IDockable> Descendants(IDockable d)
    {
        yield return d;
        if (d is IDock dock && dock.VisibleDockables is not null)
            foreach (var c in dock.VisibleDockables)
                foreach (var x in Descendants(c))
                    yield return x;
    }

    // ── Mapping ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("BrowserPanel", EditorDockFactory.BrowserId, typeof(GameBrowserView))]
    [InlineData("CanvasView",   EditorDockFactory.CanvasId,  typeof(ConversationView))]
    [InlineData("DetailPanel",  EditorDockFactory.DetailsId, typeof(NodeDetailView))]
    public void DockedTargetFor_MapsDockHostedStepsToToolIdAndViewType(
        string targetName, string expectedDockId, Type expectedViewType)
    {
        var target = TourTargetResolver.DockedTargetFor(targetName);

        Assert.NotNull(target);
        Assert.Equal(expectedDockId,   target!.DockToolId);
        Assert.Equal(expectedViewType, target.ViewType);
    }

    [Fact]
    public void DockedTargetFor_WindowChromeTarget_IsNotDocked()
        => Assert.Null(TourTargetResolver.DockedTargetFor("HelpToggle"));

    // ── Contract with the rest of the app ─────────────────────────────────

    /// Every step in the shipped tour must resolve to *something*: either a docked view
    /// (whose tool Id exists in the default layout, so it can be revealed) or a control
    /// named in MainWindow.axaml (the classic FindControl path). This is the test that
    /// would have failed when the docking shell turned BrowserPanel/DetailPanel into
    /// dangling names.
    [Fact]
    public void EveryDefaultTourStep_HasAResolvableTarget()
    {
        var layoutIds = Descendants(MakeFactory().CreateLayout()).Select(d => d.Id).ToHashSet();
        var mainWindowNames = MainWindowXNames();

        var unresolvable = GuidedTourViewModel.DefaultSteps
            .Select(s => s.TargetName)
            .Where(name => TourTargetResolver.DockedTargetFor(name) is { } docked
                ? !layoutIds.Contains(docked.DockToolId)
                : !mainWindowNames.Contains(name))
            .ToList();

        Assert.True(unresolvable.Count == 0,
            "Guided tour steps whose target can never be found (neither a docked tool in the " +
            $"default layout nor an x:Name in MainWindow.axaml): {string.Join(", ", unresolvable)}");
    }

    /// The type-based lookup is only correct if the view type we search for is the one
    /// Dock actually builds for that tool. Walk the real chain: tool Id -> wrapper -> Inner
    /// VM -> Application.DataTemplates -> built control, and compare types.
    [AvaloniaTheory]
    [InlineData("BrowserPanel")]
    [InlineData("CanvasView")]
    [InlineData("DetailPanel")]
    public void DockedTarget_ViewType_IsWhatTheAppTemplatesBuildForThatTool(string targetName)
    {
        var docked = TourTargetResolver.DockedTargetFor(targetName)!;
        var tool = Descendants(MakeFactory().CreateLayout()).First(d => d.Id == docked.DockToolId);

        // Every wrapper dockable exposes its panel VM as Inner (BrowserTool, CanvasDocument,
        // DetailsTool, ConditionSearchTool); App.axaml templates that VM by type.
        var inner = tool.GetType().GetProperty("Inner")!.GetValue(tool)!;
        var template = global::Avalonia.Application.Current!.DataTemplates.First(t => t.Match(inner));
        var built = template.Build(inner);

        Assert.IsType(docked.ViewType, built);
    }

    // ── Live lookup ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Resolve_DockedTarget_FindsViewInMainWindowByType()
    {
        var view   = new NodeDetailView();
        var window = new Window { Content = new Border { Child = view } };
        window.Show();

        Assert.Same(view, TourTargetResolver.Resolve("DetailPanel", window, []));
    }

    /// A tool dragged out of the main window lives in its own TopLevel, invisible to the
    /// main window's visual tree — the resolver must search the floating hosts too.
    [AvaloniaFact]
    public void Resolve_DockedTarget_FindsViewInFloatingHost_WhenNotInMainWindow()
    {
        var main     = new Window { Content = new TextBlock() };
        var view     = new NodeDetailView();
        var floating = new Window { Content = view };
        main.Show();
        floating.Show();

        Assert.Same(view, TourTargetResolver.Resolve("DetailPanel", main, [floating]));
    }

    /// Closed tool / content not yet realised: null means "not now", and must not throw.
    [AvaloniaFact]
    public void Resolve_DockedTarget_ReturnsNull_WhenViewIsNowhere()
    {
        var main = new Window { Content = new TextBlock() };
        main.Show();

        Assert.Null(TourTargetResolver.Resolve("BrowserPanel", main, []));
    }

    /// Non-docked targets keep the classic named lookup in the main window's name scope.
    [AvaloniaFact]
    public void Resolve_NamedTarget_UsesMainWindowNameScope()
    {
        var toggle = new ToggleButton { Name = "HelpToggle" };
        var main   = new Window { Content = toggle };
        var scope  = new NameScope();
        scope.Register("HelpToggle", toggle);
        NameScope.SetNameScope(main, scope);
        main.Show();

        Assert.Same(toggle, TourTargetResolver.Resolve("HelpToggle", main, []));
    }

    [AvaloniaFact]
    public void Resolve_UnknownName_ReturnsNull()
    {
        var main = new Window { Content = new TextBlock() };
        NameScope.SetNameScope(main, new NameScope());
        main.Show();

        Assert.Null(TourTargetResolver.Resolve("NoSuchControl", main, []));
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static HashSet<string> MainWindowXNames()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Views", "MainWindow.axaml"));
        return Regex.Matches(xaml, @"x:Name=""(?<n>[^""]+)""")
            .Select(m => m.Groups["n"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
