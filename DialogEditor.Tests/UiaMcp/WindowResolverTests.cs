using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// Window addressing for #16. AutomationId is the primary key and a title is the
/// documented fallback: titles are localised, so a run that targets one breaks the
/// moment the UI language changes — the same locale-fragility that made menu items
/// get stable AutomationIds in a20973e.
/// </summary>
public class WindowResolverTests
{
    private static readonly WindowInfo Main =
        new("w0", "Pillars Dialog Editor [Scratch]", "MainWindow", IsMain: true);
    private static readonly WindowInfo About =
        new("w1", "About", "AboutWindow");

    private static WindowResolver NewResolver(params WindowInfo[] windows) =>
        new(windows.Length == 0 ? new[] { Main, About } : windows);

    [Fact]
    public void ResolvesASecondaryWindowByItsAutomationId()
    {
        var result = NewResolver().Resolve("AboutWindow");

        Assert.Null(result.ErrorKind);
        Assert.Equal("w1", result.Window!.Id);
    }

    [Fact]
    public void FallsBackToTheTitleWhenNoAutomationIdMatches()
    {
        var result = NewResolver().Resolve("About");

        Assert.Null(result.ErrorKind);
        Assert.Equal("w1", result.Window!.Id);
    }

    [Fact]
    public void PrefersAnAutomationIdMatchOverATitleMatchOnAnotherWindow()
    {
        // A window perversely TITLED "AboutWindow" must not steal the selector from the
        // window whose stable AutomationId is "AboutWindow". Titles are user-visible and
        // therefore attacker-of-convenience territory: a project name can end up in one.
        var decoy = new WindowInfo("w2", "AboutWindow", "SettingsWindow");

        var result = NewResolver(Main, About, decoy).Resolve("AboutWindow");

        Assert.Null(result.ErrorKind);
        Assert.Equal("w1", result.Window!.Id);
    }

    [Fact]
    public void DefaultsToTheMainWindowWhenNoSelectorIsGiven()
    {
        var result = NewResolver().Resolve(null);

        Assert.Null(result.ErrorKind);
        Assert.True(result.Window!.IsMain);
    }

    [Fact]
    public void ErrorsAsAmbiguousWhenTwoWindowsShareATitle()
    {
        // The app can open two Diff windows at once. Resolver's house rule applies:
        // picking one by tree order is how a run clicks the wrong thing and still
        // reports success.
        var a = new WindowInfo("w3", "Diff", "DiffWindow");
        var b = new WindowInfo("w4", "Diff", "DiffWindow");

        var result = NewResolver(Main, a, b).Resolve("Diff");

        Assert.Equal("Ambiguous", result.ErrorKind);
        Assert.Null(result.Window);
    }

    [Fact]
    public void ErrorsAsAmbiguousWhenTwoWindowsShareAnAutomationId()
    {
        // The primary key is not unique across INSTANCES: two open Diff windows both
        // carry automationId 'DiffWindow'. Falling through to the title branch here would
        // be worse than erroring - both titles collide too, so it would resolve nothing
        // while implying the automationId was the problem.
        var a = new WindowInfo("w3", "Diff - greeting.conversation", "DiffWindow");
        var b = new WindowInfo("w4", "Diff - farewell.conversation", "DiffWindow");

        var result = NewResolver(Main, a, b).Resolve("DiffWindow");

        Assert.Equal("Ambiguous", result.ErrorKind);
        Assert.Null(result.Window);
        Assert.Contains("automationId", result.ErrorMessage);
        Assert.Contains("greeting.conversation", result.ErrorMessage);
        Assert.Contains("farewell.conversation", result.ErrorMessage);
    }

    [Fact]
    public void NotFoundListsTheOpenWindowsSoTheCallerCanDiscoverThem()
    {
        var result = NewResolver().Resolve("Setings");

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("AboutWindow", result.ErrorMessage);
        Assert.Contains("About", result.ErrorMessage);
    }

    // --- ClassName, the middle tier -------------------------------------------------
    //
    // Avalonia already sets ClassName to the Window subclass name on all ~40 windows
    // (measured 2026-09-06: 'MainWindow', 'AboutWindow', 'SettingsWindow'). That is
    // locale-stable TODAY, which is the whole reason AutomationId was preferred over the
    // title -- so it slots between them rather than replacing either. It stays SECOND
    // because it is incidental: renaming the C# class silently moves the key, with no
    // signal at the rename site, whereas an AutomationId is a declared contract.

    private static readonly WindowInfo Diff =
        new("w9", "Diff - greeting", AutomationId: "", ClassName: "DiffWindow");

    [Fact]
    public void FallsBackToClassNameWhenNoAutomationIdMatches()
    {
        var result = NewResolver(Main, Diff).Resolve("DiffWindow");

        Assert.Null(result.ErrorKind);
        Assert.Equal("w9", result.Window!.Id);
    }

    [Fact]
    public void PrefersAnAutomationIdMatchOverAClassNameMatch()
    {
        var byId = new WindowInfo("w10", "Report", AutomationId: "DiffWindow");

        var result = NewResolver(Main, Diff, byId).Resolve("DiffWindow");

        Assert.Null(result.ErrorKind);
        Assert.Equal("w10", result.Window!.Id);
    }

    [Fact]
    public void PrefersAClassNameMatchOverALocalisedTitleMatch()
    {
        // The point of the tier: a window whose TITLE happens to read 'DiffWindow' must
        // not outrank the window whose class actually is DiffWindow, because the title
        // moves with the UI language and the class does not.
        var byTitle = new WindowInfo("w11", "DiffWindow", AutomationId: "", ClassName: "HistoryWindow");

        var result = NewResolver(Main, Diff, byTitle).Resolve("DiffWindow");

        Assert.Null(result.ErrorKind);
        Assert.Equal("w9", result.Window!.Id);
    }

    [Fact]
    public void ErrorsAsAmbiguousWhenTwoWindowsShareAClassName()
    {
        var a = new WindowInfo("w12", "Diff - greeting", AutomationId: "", ClassName: "DiffWindow");
        var b = new WindowInfo("w13", "Diff - farewell", AutomationId: "", ClassName: "DiffWindow");

        var result = NewResolver(Main, a, b).Resolve("DiffWindow");

        Assert.Equal("Ambiguous", result.ErrorKind);
        Assert.Contains("className", result.ErrorMessage);
    }

    [Fact]
    public void NotFoundListsTheClassNameSinceItIsOftenTheOnlyStableKey()
    {
        var result = NewResolver(Main, Diff).Resolve("nope");

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("DiffWindow", result.ErrorMessage);
    }

    [Fact]
    public void AnEmptySelectorMatchesNothingRatherThanEveryIdlessWindow()
    {
        // Every Window in the app currently has an empty AutomationId. Without the
        // empty-key guard, Resolve("") would "match" all of them and report ambiguity,
        // which blames the wrong thing -- the caller passed no selector at all.
        var result = NewResolver(Main, Diff).Resolve("");

        Assert.Equal("NotFound", result.ErrorKind);
    }

    // --- GuardModal (#16) ------------------------------------------------------------
    //
    // A modal grabs input app-wide, so an invoke aimed at the main window dispatches,
    // reports success, and does nothing. The inventory says this guard is cheap: the
    // windows a run wants to inspect ALONGSIDE the main window (Find/Replace, Diff,
    // History, Flow Analytics, Patch Manager) are all modeless and never trip it.

    private static readonly WindowInfo ForceDelete =
        new("w5", "Force delete", "ForceDeleteDialog", IsModal: true);

    [Fact]
    public void RefusesAnotherWindowWhileAModalIsOpen()
    {
        var resolver = NewResolver(Main, ForceDelete);

        var result = resolver.GuardModal(Main);

        Assert.Equal("ModalOpen", result.ErrorKind);
        Assert.Null(result.Window);
    }

    [Fact]
    public void NamesTheOpenModalSoTheRefusalDiagnosesItself()
    {
        // The point of the guard is not the blocking - it is that a run which left a
        // dialog open gets told which one, instead of a silent false green.
        var result = NewResolver(Main, ForceDelete).GuardModal(Main);

        Assert.Contains("Force delete", result.ErrorMessage);
        Assert.Contains("ForceDeleteDialog", result.ErrorMessage);
    }

    [Fact]
    public void AllowsResolvingTheModalItself()
    {
        // Refusing this would lock the caller out of the only window it can act on.
        var result = NewResolver(Main, ForceDelete).GuardModal(ForceDelete);

        Assert.Null(result.ErrorKind);
        Assert.Equal("w5", result.Window!.Id);
    }

    [Fact]
    public void ModelessCompanionWindowsDoNotTripTheGuard()
    {
        // Find/Replace is .Show(), not ShowDialog - inspecting the main window while it
        // is open is a real and supported workflow.
        var findReplace = new WindowInfo("w6", "Find and Replace", "FindReplaceWindow");

        var result = NewResolver(Main, findReplace).GuardModal(Main);

        Assert.Null(result.ErrorKind);
        Assert.True(result.Window!.IsMain);
    }

    [Fact]
    public void AllowsAModalWhileAnotherModalIsAlsoOpen()
    {
        // Modals nest here: the Settings flow opens LanguageCodeDialog on top of itself.
        // Keying the carve-out to "the target IS the modal" would lock the caller out of
        // the inner dialog - the exact lockout the carve-out exists to prevent. UIA gives
        // no owner chain to tell inner from outer, so any open modal stays addressable.
        var settings = new WindowInfo("w7", "Settings", "SettingsWindow", IsModal: true);
        var languageCode = new WindowInfo("w8", "Language code", "LanguageCodeDialog", IsModal: true);

        var result = NewResolver(Main, settings, languageCode).GuardModal(languageCode);

        Assert.Null(result.ErrorKind);
        Assert.Equal("w8", result.Window!.Id);
    }
}
