using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// In-memory IWindowSource shaped like the 2026-09-06 probe of the running app, so the
/// inventory rules are exercised against the tree this app really produces.
/// </summary>
public sealed class FakeWindowSource : IWindowSource
{
    private readonly Dictionary<string, List<RawWindow>> _owned = new();
    private readonly List<RawWindow> _top = new();

    public IReadOnlyList<RawWindow> TopLevel() => _top;
    public IReadOnlyList<RawWindow> OwnedBy(string id) =>
        _owned.TryGetValue(id, out var kids) ? kids : Array.Empty<RawWindow>();

    public RawWindow Add(string? ownerId, string title, string className, nint handle,
        string automationId = "")
    {
        var w = new RawWindow($"w{_owned.Count}", title, automationId, className, handle);
        _owned[w.Id] = new List<RawWindow>();
        if (ownerId is null) _top.Add(w); else _owned[ownerId].Add(w);
        return w;
    }

    /// <summary>
    /// What the probe measured: main window and About sit at root (both .Show()), and
    /// Settings — opened with ShowDialog(this) — nests UNDER the main window rather than
    /// appearing as its sibling.
    /// </summary>
    public static FakeWindowSource ProbeSnapshot()
    {
        var s = new FakeWindowSource();
        var main = s.Add(null, "Pillars Dialog Editor", "MainWindow", 986482);
        s.Add(null, "About", "AboutWindow", 462358);
        s.Add(main.Id, "Settings", "SettingsWindow", 771231);
        return s;
    }
}

/// <summary>
/// Turning the live UIA window tree into WindowInfo (#16).
///
/// The issue proposed enumerating RootElement's children by process id. The probe on
/// 2026-09-06 showed that is not enough: a ShowDialog window is a CHILD of its owner in
/// the UIA tree, so root-children-only finds About and misses Settings — that is, it
/// misses precisely the ~18 modal dialogs the guard exists for.
/// </summary>
public class WindowInventoryTests
{
    private const nint MainHandle = 986482;

    private static IReadOnlyList<WindowInfo> Inventory(FakeWindowSource? source = null) =>
        new WindowInventory(source ?? FakeWindowSource.ProbeSnapshot(), MainHandle).Windows();

    [Fact]
    public void IncludesAModalNestedUnderItsOwner()
    {
        var settings = Inventory().SingleOrDefault(w => w.Title == "Settings");

        Assert.NotNull(settings);
    }

    [Fact]
    public void MarksAnOwnedWindowAsModal()
    {
        // Avalonia reports neither WindowPattern.IsModal (always false) nor a disabled
        // owner (IsEnabled stays true), so ownership is the only signal the platform
        // actually gives us. Measured 2026-09-06.
        var settings = Inventory().Single(w => w.Title == "Settings");

        Assert.True(settings.IsModal);
    }

    [Fact]
    public void LeavesAModelessTopLevelWindowUnflagged()
    {
        var about = Inventory().Single(w => w.Title == "About");

        Assert.False(about.IsModal);
    }

    [Fact]
    public void IdentifiesTheMainWindowByItsHandleNotItsPosition()
    {
        // Enumeration order is not stable: the probe saw About listed BEFORE the main
        // window on one run and after it on another, so taking the first is a coin flip.
        var main = Inventory().Single(w => w.IsMain);

        Assert.Equal("Pillars Dialog Editor", main.Title);
    }

    [Fact]
    public void FlagsNestedModalsAllTheWayDown()
    {
        // Settings opens LanguageCodeDialog on top of itself, which is why GuardModal
        // allows any modal rather than only the outermost.
        var s = new FakeWindowSource();
        var main = s.Add(null, "Pillars Dialog Editor", "MainWindow", MainHandle);
        var settings = s.Add(main.Id, "Settings", "SettingsWindow", 771231);
        s.Add(settings.Id, "Language code", "LanguageCodeDialog", 881234);

        var inner = Inventory(s).Single(w => w.Title == "Language code");

        Assert.True(inner.IsModal);
    }

    [Fact]
    public void CarriesClassNameThroughAsTheStableKeyThatExistsToday()
    {
        // No Window in the app has an AutomationId yet, so ClassName is the only stable
        // key the resolver can actually match on right now. Dropping it here would quietly
        // demote every lookup to the localised title.
        var settings = Inventory().Single(w => w.Title == "Settings");

        Assert.Equal("SettingsWindow", settings.ClassName);
    }
}
