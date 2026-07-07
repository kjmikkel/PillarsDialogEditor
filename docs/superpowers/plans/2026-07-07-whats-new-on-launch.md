# "What's New On Launch" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On launch after an upgrade, auto-open the changelog filtered to the releases new since the user last ran the app.

**Architecture:** A new `AppSettings.LastSeenVersion` records the version last run. A pure `WhatsNewDecider` computes the new-release slice from the newest-first parsed changelog. `MainWindowViewModel.ShowWhatsNewIfUpdated()` (behind injectable seams, no `AppSettings` in tests) reuses the existing `ChangelogReader`/`ShowChangelog` to open the existing `ChangelogWindow` in a new "what's new" mode. A one-line startup trigger in `MainWindow.axaml.cs` fires it. Dormant until `CHANGELOG.md` is populated at release.

**Tech Stack:** C# / .NET, Avalonia UI, xUnit, CommunityToolkit.Mvvm.

## Global Constraints

- **TDD, red first.** Failing test before implementation. Tests in `DialogEditor.Tests` mirroring source structure.
- **Localisation.** No hardcoded user-visible strings; UI strings in `DialogEditor.Avalonia/Resources/Strings.axaml` (prefix `sys:`), read via `{DynamicResource}` (XAML) or `Loc.Get`/`Loc.Format` (C#).
- **Tooltips.** No new interactive controls in this feature (reuses `ChangelogWindow`), so none to add.
- **Error handling.** No bare `catch`; reuse the existing changelog read that already logs via `AppLog.Warn`. The decider is pure and non-throwing.
- **Tests run serially** (`DialogEditor.Tests` parallelisation disabled). Keep `ShowWhatsNewIfUpdated` tests off `AppSettings` statics via injected seams — do not touch global state in tests.
- **Version semantics:** `AppVersion.Current` may be `"unknown"` (no assembly attr) — guard it (never show, never persist).
- **Build/test:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`; single-test filter `--filter "FullyQualifiedName~<name>"`.

---

### Task 1: `AppSettings.LastSeenVersion`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Test: `DialogEditor.Tests/Services/AppSettingsLastSeenVersionTests.cs` (create)

**Interfaces:**
- Produces: `AppSettings.LastSeenVersion` (`string`, default `""`), static get/set persisting to settings.json.

- [ ] **Step 1: Write the failing test**

Open `DialogEditor.Tests/Services/AppSettingsTests.cs` first to copy the exact test-isolation pattern these settings tests use (they redirect the settings file / reset statics via a disposable fixture). Mirror that fixture here.

Create `DialogEditor.Tests/Services/AppSettingsLastSeenVersionTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class AppSettingsLastSeenVersionTests : IDisposable
{
    private readonly string _dir;

    public AppSettingsLastSeenVersionTests()
    {
        // Redirect settings to a temp dir (mirror AppSettingsTests' setup).
        _dir = Path.Combine(Path.GetTempPath(), "AppSettingsLSV_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
        AppSettings.OverrideDirectoryForTests(_dir);
    }

    public void Dispose()
    {
        AppSettings.ResetOverrideForTests();
        try { Directory.Delete(_dir, true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void LastSeenVersion_DefaultsToEmpty()
        => Assert.Equal("", AppSettings.LastSeenVersion);

    [Fact]
    public void LastSeenVersion_RoundTrips()
    {
        AppSettings.LastSeenVersion = "1.2.3";
        Assert.Equal("1.2.3", AppSettings.LastSeenVersion);
    }
}
```

**Important:** the override/reset helper names above (`OverrideDirectoryForTests`/`ResetOverrideForTests`) are placeholders — use whatever the existing `AppSettingsTests.cs` actually uses for isolation. Copy its real setup/teardown verbatim; do not invent an API.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~AppSettingsLastSeenVersionTests"`
Expected: FAIL — `AppSettings` has no `LastSeenVersion` (compile error).

- [ ] **Step 3: Add the field and static accessor**

In `DialogEditor.ViewModels/Services/AppSettings.cs`, add to the inner settings class (next to `UiLanguage`, ~line 69):

```csharp
        // The app version last run, for the launch "what's new" greeting. Default ""
        // means "no baseline yet" — covers both a fresh install and the first upgrade
        // that adds this key; both set the baseline silently (see design 2026-07-07).
        public string LastSeenVersion               { get; set; } = "";
```

And add the static accessor (next to the `UiLanguage` accessor, ~line 217):

```csharp
    public static string LastSeenVersion
    {
        get => Load().LastSeenVersion;
        set { var s = Load(); s.LastSeenVersion = value; Save(s); }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~AppSettingsLastSeenVersionTests"`
Expected: PASS (both facts).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/AppSettings.cs DialogEditor.Tests/Services/AppSettingsLastSeenVersionTests.cs
git commit -m "feat(whats-new): persist LastSeenVersion setting"
```

---

### Task 2: `WhatsNewDecider` (pure)

**Files:**
- Create: `DialogEditor.Patch/Changelog/WhatsNewDecider.cs`
- Test: `DialogEditor.Tests/Patch/Changelog/WhatsNewDeciderTests.cs` (create)

**Interfaces:**
- Consumes: `ChangelogRelease` (`DialogEditor.Patch.Changelog`).
- Produces:
  - `record WhatsNewResult(IReadOnlyList<ChangelogRelease> ReleasesToShow)`
  - `static WhatsNewResult WhatsNewDecider.Decide(string lastSeen, string current, IReadOnlyList<ChangelogRelease> all)`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Patch/Changelog/WhatsNewDeciderTests.cs`:

```csharp
using DialogEditor.Patch.Changelog;

namespace DialogEditor.Tests.Patch.Changelog;

public class WhatsNewDeciderTests
{
    private static ChangelogRelease Rel(string version) =>
        new(version, "2026-01-01", []);

    // Newest-first, as the parser produces.
    private static readonly IReadOnlyList<ChangelogRelease> Log =
        [Rel("1.3.0"), Rel("1.2.0"), Rel("1.1.0"), Rel("1.0.0")];

    [Fact]
    public void EmptyLastSeen_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("", "1.3.0", Log).ReleasesToShow);

    [Fact]
    public void SameVersion_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("1.3.0", "1.3.0", Log).ReleasesToShow);

    [Fact]
    public void Upgrade_ShowsOnlyNewerReleases_Exclusive()
    {
        var shown = WhatsNewDecider.Decide("1.1.0", "1.3.0", Log).ReleasesToShow;
        Assert.Equal(["1.3.0", "1.2.0"], shown.Select(r => r.Version));
    }

    [Fact]
    public void LastSeenIsNewest_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("1.3.0", "1.3.0", Log).ReleasesToShow);

    [Fact]
    public void LastSeenNotInLog_ShowsAll()
    {
        var shown = WhatsNewDecider.Decide("0.9.0", "1.3.0", Log).ReleasesToShow;
        Assert.Equal(4, shown.Count);
    }

    [Fact]
    public void EmptyChangelog_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("1.1.0", "1.3.0", []).ReleasesToShow);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~WhatsNewDeciderTests"`
Expected: FAIL — `WhatsNewDecider` does not exist.

- [ ] **Step 3: Implement the decider**

Create `DialogEditor.Patch/Changelog/WhatsNewDecider.cs`:

```csharp
namespace DialogEditor.Patch.Changelog;

/// The releases to greet the user with on launch (empty when nothing new).
public sealed record WhatsNewResult(IReadOnlyList<ChangelogRelease> ReleasesToShow);

/// Decides which changelog releases are "new since last run". The changelog is
/// newest-first, so this walks from the top until it reaches the last-seen version —
/// no semantic-version parsing/comparison needed.
/// Design: docs/superpowers/specs/2026-07-07-whats-new-on-launch-design.md
public static class WhatsNewDecider
{
    public static WhatsNewResult Decide(
        string lastSeen, string current, IReadOnlyList<ChangelogRelease> all)
    {
        if (string.IsNullOrEmpty(lastSeen) || lastSeen == current)
            return new WhatsNewResult([]);

        var newer = new List<ChangelogRelease>();
        foreach (var release in all)
        {
            if (release.Version == lastSeen) break;   // reached last-seen (exclusive)
            newer.Add(release);
        }
        return new WhatsNewResult(newer);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~WhatsNewDeciderTests"`
Expected: PASS (all six).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Changelog/WhatsNewDecider.cs DialogEditor.Tests/Patch/Changelog/WhatsNewDeciderTests.cs
git commit -m "feat(whats-new): pure decider for new-since-last-run releases"
```

---

### Task 3: `ChangelogViewModel` "what's new" mode + strings

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/ChangelogViewModelTests.cs` (add cases)

**Interfaces:**
- Consumes: `ChangelogRelease`.
- Produces: `ChangelogViewModel(IReadOnlyList<ChangelogRelease> releases, bool isWhatsNew = false, string version = "")`; `bool IsWhatsNew`; `string HeaderText`; `string WindowTitle`; `bool ShowHeader`.

- [ ] **Step 1: Add localisation strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, next to the existing `Changelog_*` keys:

```xml
    <sys:String x:Key="WhatsNew_Title">What's New</sys:String>
    <sys:String x:Key="WhatsNew_Header">What's new in {0}</sys:String>
```

- [ ] **Step 2: Write the failing tests**

Open `DialogEditor.Tests/ViewModels/ChangelogViewModelTests.cs` and note it uses `Loc.Configure(new StubStringProvider())` (the stub echoes keys). Add:

```csharp
    [Fact]
    public void Default_IsNotWhatsNew()
    {
        var vm = new ChangelogViewModel([]);
        Assert.False(vm.IsWhatsNew);
    }

    [Fact]
    public void WhatsNewMode_SetsFlagAndTitle()
    {
        var vm = new ChangelogViewModel([], isWhatsNew: true, version: "1.2.0");
        Assert.True(vm.IsWhatsNew);
        // Stub echoes the resource key, proving the what's-new branch is used.
        Assert.Equal("WhatsNew_Title", vm.WindowTitle);
        Assert.True(vm.ShowHeader);
    }

    [Fact]
    public void NonWhatsNew_UsesChangelogTitle_AndHidesHeader()
    {
        var vm = new ChangelogViewModel([]);
        Assert.Equal("Changelog_Title", vm.WindowTitle);
        Assert.False(vm.ShowHeader);
    }
```

(If a test class-level `Loc.Configure(new StubStringProvider())` isn't already present, add it in the constructor, matching the sibling VM test classes.)

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ChangelogViewModelTests"`
Expected: FAIL — the new constructor overload / members don't exist.

- [ ] **Step 4: Implement the mode**

Replace the constructor and add members in `DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs`:

```csharp
    public IReadOnlyList<ChangelogRelease> Releases { get; }
    public bool   IsWhatsNew { get; }
    private readonly string _version;

    public ChangelogViewModel(
        IReadOnlyList<ChangelogRelease> releases,
        bool isWhatsNew = false,
        string version = "")
    {
        Releases   = releases;
        IsWhatsNew = isWhatsNew;
        _version   = version;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(string.Empty);
        };
    }

    public bool   IsEmpty     => Releases.Count == 0;
    public bool   HasReleases => Releases.Count > 0;
    public string EmptyMessage => Loc.Get("Changelog_Empty");

    // What's-new mode shows a version header and a distinct window title.
    public bool   ShowHeader  => IsWhatsNew;
    public string HeaderText  => Loc.Format("WhatsNew_Header", _version);
    public string WindowTitle => IsWhatsNew
        ? Loc.Get("WhatsNew_Title")
        : Loc.Get("Changelog_Title");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ChangelogViewModelTests"`
Expected: PASS (existing + new cases).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/ChangelogViewModelTests.cs
git commit -m "feat(whats-new): what's-new mode on ChangelogViewModel"
```

---

### Task 4: `ChangelogWindow.axaml` — header + title binding

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ChangelogWindow.axaml`
- Test: run existing `DialogEditor.Tests/Views/ChangelogWindowTests.cs` (no new test; markup change).

**Interfaces:**
- Consumes: `ChangelogViewModel.WindowTitle`, `HeaderText`, `ShowHeader` (Task 3).

- [ ] **Step 1: Bind the title to the view-model**

In `DialogEditor.Avalonia/Views/ChangelogWindow.axaml`, change the window title from the static resource to the VM property (so it swaps in what's-new mode):

```xml
        Title="{Binding WindowTitle}"
```

(Was `Title="{DynamicResource Changelog_Title}"`. The non-what's-new path returns `Changelog_Title` via the VM, preserving current behaviour. Note the design-time `d:DataContext` — if the window has none and the title shows blank in the designer, that's cosmetic only.)

- [ ] **Step 2: Add the what's-new header**

Immediately after the `<DockPanel …>` open tag, before the Close button, add a top-docked header shown only in what's-new mode:

```xml
        <TextBlock DockPanel.Dock="Top"
                   IsVisible="{Binding ShowHeader}"
                   Text="{Binding HeaderText}"
                   FontWeight="Bold" FontSize="{DynamicResource FontSize.Subtitle}"
                   Foreground="{DynamicResource Brush.Text.Emphasis}"
                   Margin="0,0,0,10" TextWrapping="Wrap"/>
```

- [ ] **Step 3: Build and run the existing window tests**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj -clp:ErrorsOnly`
Then: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ChangelogWindowTests"`
Expected: build succeeds; existing window tests PASS (they construct the window / VM — confirm the title binding didn't break them; if a test asserted the old resource title, update it to the VM-driven value).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/ChangelogWindow.axaml
git commit -m "feat(whats-new): changelog window header + title for what's-new mode"
```

---

### Task 5: `MainWindowViewModel.ShowWhatsNewIfUpdated`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelWhatsNewTests.cs` (create)

**Interfaces:**
- Consumes: `ChangelogReader` (existing seam), `ShowChangelog` (existing delegate), `ChangelogParser`, `WhatsNewDecider`, `AppSettings.LastSeenVersion` (Task 1), `AppVersion.Current`.
- Produces:
  - `Func<string>?  LastSeenVersionGetter { get; set; }` (default `() => AppSettings.LastSeenVersion`)
  - `Action<string>? LastSeenVersionSetter { get; set; }` (default `v => AppSettings.LastSeenVersion = v`)
  - `Func<string>?  CurrentVersionProvider { get; set; }` (default `() => AppVersion.Current`)
  - `void ShowWhatsNewIfUpdated()`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/MainWindowViewModelWhatsNewTests.cs`. Reuse whatever construction the existing `MainWindowViewModelTests` uses to build a `MainWindowViewModel` (open that file and copy the constructor call — do not invent one). The test sets the injectable seams and a stub `ChangelogReader`, and captures `ShowChangelog`.

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelWhatsNewTests
{
    public MainWindowViewModelWhatsNewTests() => Loc.Configure(new StubStringProvider());

    private const string Changelog = """
        # Changelog

        ## [1.1.0] - 2026-02-01
        ### Added
        - New thing

        ## [1.0.0] - 2026-01-01
        ### Added
        - First thing
        """;

    private static MainWindowViewModel NewVm() => /* copy the real construction from
        MainWindowViewModelTests (e.g. its helper or `new MainWindowViewModel(...)`) */;

    private static (MainWindowViewModel vm, List<ChangelogViewModel> shown, List<string> persisted)
        Wire(string lastSeen, string current, string? changelog)
    {
        var vm        = NewVm();
        var shown     = new List<ChangelogViewModel>();
        var persisted = new List<string>();
        vm.ChangelogReader        = () => changelog;
        vm.ShowChangelog          = cvm => shown.Add(cvm);
        vm.LastSeenVersionGetter  = () => lastSeen;
        vm.LastSeenVersionSetter  = v => persisted.Add(v);
        vm.CurrentVersionProvider = () => current;
        return (vm, shown, persisted);
    }

    [Fact]
    public void Upgrade_ShowsWhatsNew_AndPersistsCurrent()
    {
        var (vm, shown, persisted) = Wire(lastSeen: "1.0.0", current: "1.1.0", Changelog);
        vm.ShowWhatsNewIfUpdated();
        var cvm = Assert.Single(shown);
        Assert.True(cvm.IsWhatsNew);
        Assert.Equal(["1.1.0"], cvm.Releases.Select(r => r.Version));
        Assert.Equal(["1.1.0"], persisted);
    }

    [Fact]
    public void SameVersion_ShowsNothing_ButPersists()
    {
        var (vm, shown, persisted) = Wire("1.1.0", "1.1.0", Changelog);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Equal(["1.1.0"], persisted);
    }

    [Fact]
    public void EmptyLastSeen_ShowsNothing_ButPersistsBaseline()
    {
        var (vm, shown, persisted) = Wire("", "1.1.0", Changelog);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Equal(["1.1.0"], persisted);
    }

    [Fact]
    public void UnknownVersion_ShowsNothing_AndDoesNotPersist()
    {
        var (vm, shown, persisted) = Wire("1.0.0", "unknown", Changelog);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Empty(persisted);
    }

    [Fact]
    public void EmptyChangelogOnUpgrade_ShowsNothing_ButPersists()
    {
        var (vm, shown, persisted) = Wire("1.0.0", "1.1.0", changelog: null);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Equal(["1.1.0"], persisted);
    }
}
```

Fill in `NewVm()` with the real constructor from `MainWindowViewModelTests`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelWhatsNewTests"`
Expected: FAIL — seams and `ShowWhatsNewIfUpdated` don't exist.

- [ ] **Step 3: Implement**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, ensure `using DialogEditor.Patch;` and `using DialogEditor.Patch.Changelog;` are present (add if missing). Near the existing `ChangelogReader`/`ShowChangelog` members, add the seams:

```csharp
    /// Test seams for the launch "what's new" greeting (default to AppSettings/AppVersion).
    public Func<string>?   LastSeenVersionGetter  { get; set; }
    public Action<string>? LastSeenVersionSetter  { get; set; }
    public Func<string>?   CurrentVersionProvider { get; set; }
```

Add the method (near `Changelog()`):

```csharp
    /// Called once at startup: if the app version advanced since last run, open the
    /// changelog filtered to the new releases. Always records the current version so
    /// it never re-shows. See design 2026-07-07-whats-new-on-launch.
    public void ShowWhatsNewIfUpdated()
    {
        var current = (CurrentVersionProvider ?? (() => AppVersion.Current))();
        if (string.IsNullOrEmpty(current) || current == "unknown")
            return; // no usable version — never show, never poison the baseline

        var lastSeen = (LastSeenVersionGetter ?? (() => AppSettings.LastSeenVersion))();

        var read     = ChangelogReader ?? DefaultChangelogReader;
        var text     = read();
        if (text is null) AppLog.Warn("What's new: CHANGELOG.md unavailable.");
        var releases = text is null
            ? Array.Empty<ChangelogRelease>()
            : ChangelogParser.Parse(text);

        var result = WhatsNewDecider.Decide(lastSeen, current, releases);
        if (result.ReleasesToShow.Count > 0)
            ShowChangelog?.Invoke(new ChangelogViewModel(
                result.ReleasesToShow, isWhatsNew: true, version: current));

        (LastSeenVersionSetter ?? (v => AppSettings.LastSeenVersion = v))(current);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelWhatsNewTests"`
Expected: PASS (all five).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelWhatsNewTests.cs
git commit -m "feat(whats-new): ShowWhatsNewIfUpdated orchestration on MainWindowViewModel"
```

---

### Task 6: Startup trigger + Gaps.md + verification

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`
- Modify: `Gaps.md`

**Interfaces:**
- Consumes: `MainWindowViewModel.ShowWhatsNewIfUpdated` (Task 5); the existing `vm.ShowChangelog` wiring.

- [ ] **Step 1: Fire the greeting after wiring**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, immediately after the `vm.ShowChangelog = …;` assignment block (the one that opens `ChangelogWindow`), add:

```csharp
        // Launch greeting: show "what's new" once if the app version advanced.
        vm.ShowWhatsNewIfUpdated();
```

This runs for both startup paths (fresh → onboarding → MainWindow, and returning → MainWindow), since the wiring executes whenever MainWindow's DataContext is set up. Fresh installs hit the empty-baseline branch and silently record the version.

- [ ] **Step 2: Full build + test suite**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj -clp:ErrorsOnly`
Then: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: build succeeds; entire suite PASSES (no regressions).

- [ ] **Step 3: Verify in the running app**

Use the `running-the-app` skill. Because `CHANGELOG.md` is frozen/empty, the greeting will not appear — verify the *mechanism is inert and safe*:
- Launch with a scratch project; confirm the app starts normally (no stray window, no crash) — proves the startup trigger runs cleanly against an empty changelog.
- Read `%LOCALAPPDATA%\PillarsDialogEditor\settings.json` and confirm a `LastSeenVersion` key is now present (baseline recorded). Back up / restore settings per the skill's rules.

(Full greeting rendering can't be exercised until the changelog is populated; the VM/decider tests cover that path.)

- [ ] **Step 4: Update `Gaps.md`**

In `Gaps.md` under "Changelog / Release Notes", replace the deferred sentence:

> A version-aware "what's new since your last run" layer remains a future enhancement — see the design spec.

with:

```markdown
A version-aware **"what's new since your last run"** layer is now implemented
(2026-07-07): on launch, if `AppVersion.Current` advanced past the persisted
`AppSettings.LastSeenVersion`, the changelog auto-opens filtered to the new
releases (pure `WhatsNewDecider` walks the newest-first log; `ChangelogWindow`
reused in a "what's new" mode). Dormant until `CHANGELOG.md` is populated at
release. Fresh installs and the first feature-adding upgrade record the baseline
silently. Spec: docs/superpowers/specs/2026-07-07-whats-new-on-launch-design.md.
```

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs Gaps.md
git commit -m "feat(whats-new): fire launch greeting; mark gap implemented"
```

---

## Self-Review

**Spec coverage:**
- §1 `AppSettings.LastSeenVersion` → Task 1. ✔
- §2 `WhatsNewDecider` (empty/equal/slice/not-found/empty-log) → Task 2. ✔
- §3 `ChangelogViewModel` what's-new mode (IsWhatsNew/HeaderText/title) → Task 3. ✔
- Window rendering of header + title → Task 4. ✔
- §4 `ShowWhatsNewIfUpdated` with injectable seams + unknown-version guard + always-persist → Task 5. ✔
- §5 startup trigger → Task 6. ✔
- Behavior matrix rows: fresh/first-upgrade (empty→baseline, Task 5 test), normal upgrade (Task 5), no-change (Task 5), unknown (Task 5), empty changelog (Task 5). ✔
- Cross-cutting: localisation (Task 3 strings), error handling (reuses logged read), serial-test/global-state (injected seams, Task 5), no new interactive controls. ✔
- Gaps.md + app verification → Task 6. ✔

**Placeholder scan:** The two "copy the real construction/isolation from the existing test file" notes (Tasks 1, 5) are explicit reuse instructions with a named source file, not open work — every other step has concrete code/commands.

**Type consistency:** `WhatsNewResult.ReleasesToShow` / `WhatsNewDecider.Decide(lastSeen, current, all)` defined in Task 2, consumed identically in Task 5. `ChangelogViewModel(releases, isWhatsNew, version)` + `IsWhatsNew`/`HeaderText`/`WindowTitle`/`ShowHeader` defined in Task 3, bound in Task 4, constructed in Task 5. Seam names (`LastSeenVersionGetter`/`Setter`, `CurrentVersionProvider`) consistent between Task 5 interface, implementation, and its test. `AppSettings.LastSeenVersion` type (`string`) consistent across Tasks 1 and 5.

**Implementation-time verifications flagged (not fabricated):** the real `AppSettingsTests` isolation fixture (Task 1) and the real `MainWindowViewModel`/`MainWindowViewModelTests` construction (Task 5) must be copied from those files during implementation — each step says so and names the file.
