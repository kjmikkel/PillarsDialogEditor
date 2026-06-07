# Changelog & About Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two Help-menu dialogs — an in-app **Changelog** reader and an **About** dialog — sharing one version source.

**Architecture:** A pure `ChangelogParser` (in `DialogEditor.Patch`) turns a bundled `CHANGELOG.md` into grouped releases; a shared `AppVersion` helper reads the assembly informational version (used by both the GUI About dialog and the CLI `--version`). Two relay commands on `MainWindowViewModel` build view-models and hand them to the View via `Action<...>` seams (the established pattern), which opens dark-themed `ChangelogWindow` / `AboutWindow`. All file IO and URL launching sit behind injectable seams for testability.

**Tech Stack:** .NET 8, Avalonia 11 (headless xUnit tests), CommunityToolkit.Mvvm source generators, existing `Loc`/`AppLog` services.

**Spec:** `docs/superpowers/specs/2026-06-07-changelog-and-about-design.md`

**Conventions (verified in repo):**
- Tests: xUnit `[Fact]`; Avalonia views use `[AvaloniaFact]` with `Loc.Configure(new StubStringProvider())` in the test ctor (`StubStringProvider.Get(key) => key`).
- `AppLog` lives in `DialogEditor.ViewModels.Services`; `Loc` in `DialogEditor.ViewModels.Resources`.
- Commands use `[RelayCommand]` (generates `XxxCommand`). VM→View windows are opened by `Func`/`Action` callbacks set in `MainWindow.axaml.cs` (e.g. `vm.ShowImportWarnings = ...`).
- Window chrome mirrors `DiffHelpWindow.axaml`: `Background="#1a1a2a"`, `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`, `ShowInTaskbar="False"`.
- New user-facing strings go in `DialogEditor.Avalonia/Resources/Strings.axaml` (no inline text); every caught exception logged except `OperationCanceledException`.

---

## File Structure

**Create**
- `DialogEditor.Patch/AppVersion.cs` — shared version reader.
- `DialogEditor.Patch/Changelog/ChangelogModels.cs` — `ChangelogRelease`, `ChangelogSection` records.
- `DialogEditor.Patch/Changelog/ChangelogParser.cs` — pure parser.
- `DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs` — display wrapper.
- `DialogEditor.ViewModels/ViewModels/AboutViewModel.cs` — version + link commands.
- `DialogEditor.ViewModels/Services/ExternalLauncher.cs` — default URL/file opener.
- `DialogEditor.Avalonia/Views/ChangelogWindow.axaml` (+ `.axaml.cs`).
- `DialogEditor.Avalonia/Views/AboutWindow.axaml` (+ `.axaml.cs`).
- `CHANGELOG.md` (repo root) — frozen placeholder.
- Tests: `DialogEditor.Tests/Patch/AppVersionTests.cs`, `DialogEditor.Tests/Patch/Changelog/ChangelogParserTests.cs`, `DialogEditor.Tests/ViewModels/ChangelogViewModelTests.cs`, `DialogEditor.Tests/ViewModels/AboutViewModelTests.cs`, `DialogEditor.Tests/ViewModels/MainWindowViewModelHelpMenuTests.cs`, `DialogEditor.Tests/Views/ChangelogWindowTests.cs`, `DialogEditor.Tests/Views/AboutWindowTests.cs`.

**Modify**
- `DialogEditor.PatchCli/Program.cs:5-8` — use `AppVersion`.
- `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — add `ChangelogCommand`/`AboutCommand` + seams.
- `DialogEditor.Avalonia/Views/MainWindow.axaml:136-137` — two new Help menu items.
- `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` — wire `ShowChangelog`/`ShowAbout`.
- `DialogEditor.Avalonia/Resources/Strings.axaml:909` — new strings.
- `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj:34-36` — bundle `CHANGELOG.md`.

**Build/test commands**
- Test one class: `dotnet test --filter "FullyQualifiedName~<ClassName>"`
- Full suite: `dotnet test`

---

## Task 1: `AppVersion` shared helper

**Files:**
- Create: `DialogEditor.Patch/AppVersion.cs`
- Test: `DialogEditor.Tests/Patch/AppVersionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using DialogEditor.Patch;
using Xunit;

namespace DialogEditor.Tests.Patch;

public class AppVersionTests
{
    [Fact]
    public void FromAssembly_ReturnsInformationalVersionOfThatAssembly()
    {
        var asm = typeof(AppVersionTests).Assembly;
        var expected = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        Assert.Equal(expected, AppVersion.FromAssembly(asm));
    }

    [Fact]
    public void FromAssembly_Null_ReturnsUnknown()
    {
        Assert.Equal("unknown", AppVersion.FromAssembly(null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AppVersionTests"`
Expected: FAIL — `AppVersion` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Reflection;

namespace DialogEditor.Patch;

/// Single source of truth for the application version string, shared by the GUI About
/// dialog and the CLI `--version` so they never drift. Reads the assembly's
/// AssemblyInformationalVersion (fed from the VERSION file at build time).
public static class AppVersion
{
    public static string Current => FromAssembly(Assembly.GetEntryAssembly());

    public static string FromAssembly(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AppVersionTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/AppVersion.cs DialogEditor.Tests/Patch/AppVersionTests.cs
git commit -m "feat: add AppVersion shared version helper"
```

---

## Task 2: Point the CLI at `AppVersion`

**Files:**
- Modify: `DialogEditor.PatchCli/Program.cs:5-8`

- [ ] **Step 1: Replace the inline version read**

Find (lines 5-8):

```csharp
var Version =
    typeof(Program).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
```

Replace with:

```csharp
var Version = DialogEditor.Patch.AppVersion.FromAssembly(
    System.Reflection.Assembly.GetExecutingAssembly());
```

(`using DialogEditor.Patch;` is already present at the top of the file.)

- [ ] **Step 2: Verify it still builds and reports a version**

Run: `dotnet run --project DialogEditor.PatchCli -- --version`
Expected: prints `dialog-patcher <version>` (same value as before; no `unknown` unless unset).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.PatchCli/Program.cs
git commit -m "refactor: CLI reads version via AppVersion helper"
```

---

## Task 3: Changelog model records

**Files:**
- Create: `DialogEditor.Patch/Changelog/ChangelogModels.cs`

This task has no standalone test (records are exercised by Task 4's parser tests). Build only.

- [ ] **Step 1: Create the records**

```csharp
using System.Collections.Generic;

namespace DialogEditor.Patch.Changelog;

/// One released version with its grouped notes, newest-first in a changelog.
public sealed record ChangelogRelease(
    string Version,
    string Date,
    IReadOnlyList<ChangelogSection> Sections);

/// A group of notes under a release. Heading is null for a flat, unlabelled list
/// (bullets that appear before any "### " subheading).
public sealed record ChangelogSection(string? Heading, IReadOnlyList<string> Entries)
{
    public bool HasHeading => !string.IsNullOrWhiteSpace(Heading);
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build DialogEditor.Patch`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Patch/Changelog/ChangelogModels.cs
git commit -m "feat: add changelog release/section model records"
```

---

## Task 4: `ChangelogParser`

**Files:**
- Create: `DialogEditor.Patch/Changelog/ChangelogParser.cs`
- Test: `DialogEditor.Tests/Patch/Changelog/ChangelogParserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Patch.Changelog;
using Xunit;

namespace DialogEditor.Tests.Patch.Changelog;

public class ChangelogParserTests
{
    [Fact]
    public void Parse_GroupsSubsections_NewestFirst()
    {
        const string md = """
            # Changelog

            ## [1.2.0] — 2026-05-31
            ### Added
            - Diff viewer
            - Selective apply
            ### Fixed
            - Crash on open

            ## [1.1.0] - 2026-05-01
            ### Added
            - Branches
            """;

        var releases = ChangelogParser.Parse(md);

        Assert.Collection(releases,
            r =>
            {
                Assert.Equal("1.2.0", r.Version);
                Assert.Equal("2026-05-31", r.Date);
                Assert.Collection(r.Sections,
                    s => { Assert.Equal("Added", s.Heading); Assert.Equal(new[] { "Diff viewer", "Selective apply" }, s.Entries); },
                    s => { Assert.Equal("Fixed", s.Heading); Assert.Equal(new[] { "Crash on open" }, s.Entries); });
            },
            r =>
            {
                Assert.Equal("1.1.0", r.Version);
                Assert.Equal("2026-05-01", r.Date);
                var s = Assert.Single(r.Sections);
                Assert.Equal("Added", s.Heading);
                Assert.Equal(new[] { "Branches" }, s.Entries);
            });
    }

    [Fact]
    public void Parse_BulletsBeforeAnySubheading_FormFlatNullHeadingSection()
    {
        const string md = """
            ## [1.0.0] — 2026-04-01
            - First note
            * Second note
            """;

        var release = Assert.Single(ChangelogParser.Parse(md));
        var section = Assert.Single(release.Sections);
        Assert.Null(section.Heading);
        Assert.False(section.HasHeading);
        Assert.Equal(new[] { "First note", "Second note" }, section.Entries);
    }

    [Fact]
    public void Parse_EmptyOrProseOnly_ReturnsEmpty()
    {
        Assert.Empty(ChangelogParser.Parse(""));
        Assert.Empty(ChangelogParser.Parse("# Changelog\n\nNo releases yet.\n## [Unreleased]\n"));
    }
}
```

(Note: `## [Unreleased]` has no date, so it is not recognized as a release — exactly the frozen-pre-release state.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChangelogParserTests"`
Expected: FAIL — `ChangelogParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DialogEditor.Patch.Changelog;

/// Parses the project's "Keep a Changelog"-style CHANGELOG.md into grouped releases.
/// Intentionally understands only our shape (## release / ### section / - bullet); any
/// other markdown is ignored. Never throws on malformed input.
public static class ChangelogParser
{
    // "## [1.2.0] — 2026-05-31" or "## 1.2.0 - 2026-05-31"
    private static readonly Regex ReleaseRx =
        new(@"^\s*##\s+\[?(?<ver>[^\]\s]+)\]?\s*[—-]\s*(?<date>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex SectionRx =
        new(@"^\s*###\s+(?<head>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletRx =
        new(@"^\s*[-*]\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<ChangelogRelease> Parse(string markdown)
    {
        var releases = new List<ChangelogRelease>();

        List<ChangelogSection>? sections = null;
        string? version = null, date = null;
        string? heading = null;
        List<string>? entries = null;

        void FlushSection()
        {
            if (sections is null) return;
            if (heading is not null || entries is { Count: > 0 })
                sections.Add(new ChangelogSection(heading, entries ?? new List<string>()));
            heading = null;
            entries = null;
        }

        void FlushRelease()
        {
            if (version is not null)
            {
                FlushSection();
                releases.Add(new ChangelogRelease(version, date ?? "", sections!));
            }
            sections = null; version = null; date = null;
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            var rel = ReleaseRx.Match(line);
            if (rel.Success)
            {
                FlushRelease();
                version = rel.Groups["ver"].Value;
                date = rel.Groups["date"].Value;
                sections = new List<ChangelogSection>();
                continue;
            }

            if (sections is null) continue; // ignore anything before the first release

            var sec = SectionRx.Match(line);
            if (sec.Success)
            {
                FlushSection();
                heading = sec.Groups["head"].Value;
                entries = new List<string>();
                continue;
            }

            var bul = BulletRx.Match(line);
            if (bul.Success)
            {
                (entries ??= new List<string>()).Add(bul.Groups["text"].Value);
            }
            // any other line is ignored
        }

        FlushRelease();
        return releases;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ChangelogParserTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Changelog/ChangelogParser.cs DialogEditor.Tests/Patch/Changelog/ChangelogParserTests.cs
git commit -m "feat: add ChangelogParser with grouped subsections"
```

---

## Task 5: `ExternalLauncher` (default URL/file opener)

**Files:**
- Create: `DialogEditor.ViewModels/Services/ExternalLauncher.cs`

No standalone test (it shells out to the OS; behaviour is exercised through the injectable
seam in Tasks 6–7). Build only.

- [ ] **Step 1: Create the launcher**

```csharp
using System;
using System.Diagnostics;

namespace DialogEditor.ViewModels.Services;

/// Opens a URL or local file via the OS default handler. Default implementation behind the
/// view-models' opener seams so tests can substitute a fake. Returns false (and logs) on
/// failure rather than throwing.
public static class ExternalLauncher
{
    public static bool Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ExternalLauncher: failed to open '{target}': {ex.Message}");
            return false;
        }
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build DialogEditor.ViewModels`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.ViewModels/Services/ExternalLauncher.cs
git commit -m "feat: add ExternalLauncher default opener"
```

---

## Task 6: `ChangelogViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/ChangelogViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ChangelogViewModelTests"`
Expected: FAIL — `ChangelogViewModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Collections.Generic;
using DialogEditor.Patch.Changelog;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// Display wrapper for the parsed changelog. Holds the releases and the empty-state copy
/// for the reader window.
public sealed class ChangelogViewModel
{
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    public ChangelogViewModel(IReadOnlyList<ChangelogRelease> releases) => Releases = releases;

    public bool IsEmpty => Releases.Count == 0;
    public bool HasReleases => Releases.Count > 0;
    public string EmptyMessage => Loc.Get("Changelog_Empty");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ChangelogViewModelTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs DialogEditor.Tests/ViewModels/ChangelogViewModelTests.cs
git commit -m "feat: add ChangelogViewModel display wrapper"
```

---

## Task 7: `AboutViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/AboutViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/AboutViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AboutViewModelTests"`
Expected: FAIL — `AboutViewModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// Backing model for the About dialog: version + license/credits copy + repository/docs
/// link commands. Link opening goes through an injectable seam (default: ExternalLauncher).
public sealed partial class AboutViewModel : ObservableObject
{
    public string Version { get; }
    public string RepositoryUrl { get; }
    public string DocsUrl { get; }

    public string AppName => Loc.Get("About_AppName");
    public string Description => Loc.Get("About_Description");
    public string License => Loc.Get("About_License");
    public string Credits => Loc.Get("About_Credits");

    [ObservableProperty]
    private string _status = "";

    public Func<string, bool> UrlOpener { get; set; } = ExternalLauncher.Open;

    public AboutViewModel(string version, string repositoryUrl, string docsUrl)
    {
        Version = version;
        RepositoryUrl = repositoryUrl;
        DocsUrl = docsUrl;
    }

    [RelayCommand] private void OpenRepository() => Open(RepositoryUrl);
    [RelayCommand] private void OpenDocs() => Open(DocsUrl);

    private void Open(string url)
    {
        if (UrlOpener(url)) return;
        AppLog.Warn($"About: failed to open '{url}'.");
        Status = Loc.Get("About_OpenFailed");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AboutViewModelTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/AboutViewModel.cs DialogEditor.Tests/ViewModels/AboutViewModelTests.cs
git commit -m "feat: add AboutViewModel with link commands"
```

---

## Task 8: Help-menu commands + seams on `MainWindowViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (add after the `OpenWalkthrough` region, around line 1082)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelHelpMenuTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelHelpMenuTests
{
    public MainWindowViewModelHelpMenuTests() => Loc.Configure(new StubStringProvider());

    private static MainWindowViewModel Make() =>
        new(new ImmediateDispatcher(), new NullFolderPicker(), new NullFilePicker());

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
```

> **Note on test doubles:** `ImmediateDispatcher`, `NullFolderPicker`, `NullFilePicker` are the existing test doubles used by other `MainWindowViewModel` tests. Before writing, open `DialogEditor.Tests/ViewModels/MainWindowViewModelApplyTests.cs` and reuse whatever doubles it constructs the VM with (match its exact constructor call and helper names); substitute them in `Make()` if the names differ.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MainWindowViewModelHelpMenuTests"`
Expected: FAIL — `ChangelogCommand`/`AboutCommand`/seams do not exist.

- [ ] **Step 3: Add the implementation**

Insert this region immediately after the `OpenWalkthrough` members (after line ~1101, before the "Backup offer" region). Add `using DialogEditor.Patch.Changelog;` to the file's usings:

```csharp
    // ── Changelog (Help menu) ─────────────────────────────────────────────
    private const string ChangelogFileName = "CHANGELOG.md";

    /// Test seam: returns the raw changelog text, or null when unavailable.
    public Func<string?>? ChangelogReader { get; set; }

    /// Set by the UI layer to open the changelog reader window.
    public Action<ChangelogViewModel>? ShowChangelog { get; set; }

    [RelayCommand]
    private void Changelog()
    {
        var read = ChangelogReader ?? DefaultChangelogReader;
        var text = read();
        if (text is null) AppLog.Warn("Changelog: CHANGELOG.md unavailable.");
        var releases = text is null
            ? Array.Empty<ChangelogRelease>()
            : ChangelogParser.Parse(text);
        ShowChangelog?.Invoke(new ChangelogViewModel(releases));
    }

    private static string? DefaultChangelogReader()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ChangelogFileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Changelog read failed: {ex.Message}");
            return null;
        }
    }

    // ── About (Help menu) ─────────────────────────────────────────────────
    public const string RepositoryUrl = "https://github.com/kjmikkel/PillarsDialogEditor";
    public const string DocsUrl = "https://github.com/kjmikkel/PillarsDialogEditor#readme";

    /// Set by the UI layer to open the About window.
    public Action<AboutViewModel>? ShowAbout { get; set; }

    [RelayCommand]
    private void About()
        => ShowAbout?.Invoke(new AboutViewModel(AppVersion.Current, RepositoryUrl, DocsUrl));
```

(If `System` / `System.IO` are not already imported, they are — `using System.IO;` is at the top and `ImplicitUsings` is enabled. `AppVersion` resolves via the existing `using DialogEditor.Patch;`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MainWindowViewModelHelpMenuTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelHelpMenuTests.cs
git commit -m "feat: add Changelog/About commands and seams to MainWindowViewModel"
```

---

## Task 9: Localized strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (insert after line 909, the `Menu_OpenWalkthroughTip` entry)

No test (strings are consumed by Tasks 8/10/11 tests via `StubStringProvider`). Build only.

- [ ] **Step 1: Add the strings**

Insert after the `Menu_OpenWalkthroughTip` line:

```xml
    <sys:String x:Key="Menu_Changelog">Changelog…</sys:String>
    <sys:String x:Key="Menu_ChangelogTip">See what has changed in each released version of the editor, newest first.</sys:String>
    <sys:String x:Key="Menu_About">About…</sys:String>
    <sys:String x:Key="Menu_AboutTip">Show the application version, licence, credits, and links to the project's repository and online documentation.</sys:String>

    <sys:String x:Key="Changelog_Title">Changelog</sys:String>
    <sys:String x:Key="Changelog_Empty">No release notes yet — the first public release has not happened.</sys:String>
    <sys:String x:Key="Changelog_Close">Close</sys:String>

    <sys:String x:Key="About_Title">About</sys:String>
    <sys:String x:Key="About_AppName">Pillars Dialog Editor</sys:String>
    <sys:String x:Key="About_Description">A visual editor and patch toolkit for Pillars of Eternity I &amp; II conversations.</sys:String>
    <sys:String x:Key="About_VersionLabel">Version</sys:String>
    <sys:String x:Key="About_License">Licence: see LICENSE in the project repository.</sys:String>
    <sys:String x:Key="About_Credits">Built with Avalonia and Nodify.</sys:String>
    <sys:String x:Key="About_OpenRepository">Repository</sys:String>
    <sys:String x:Key="About_OpenRepositoryTip">Open the project's source-code repository in your browser.</sys:String>
    <sys:String x:Key="About_OpenDocs">Online documentation</sys:String>
    <sys:String x:Key="About_OpenDocsTip">Open the project's online documentation (README) in your browser.</sys:String>
    <sys:String x:Key="About_OpenFailed">Couldn't open the link. Please check your internet connection or browser.</sys:String>
    <sys:String x:Key="About_Close">Close</sys:String>
```

> **Open item (confirm at this step):** replace the `About_License` / `About_Credits` / `About_Description` copy with the exact wording from the repo's `LICENSE` / `README` if it differs.

- [ ] **Step 2: Verify it builds**

Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: add Changelog/About localized strings"
```

---

## Task 10: `ChangelogWindow`

**Files:**
- Create: `DialogEditor.Avalonia/Views/ChangelogWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/ChangelogWindow.axaml.cs`
- Test: `DialogEditor.Tests/Views/ChangelogWindowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ChangelogWindowTests"`
Expected: FAIL — `ChangelogWindow` does not exist.

- [ ] **Step 3: Create the XAML**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.ChangelogWindow"
        Title="{StaticResource Changelog_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="460" Height="560" MinHeight="320" CanResize="True"
        ShowInTaskbar="False" Background="#1a1a2a">
    <DockPanel Margin="14,12,14,12">
        <Button DockPanel.Dock="Bottom" HorizontalAlignment="Right" Margin="0,10,0,0"
                Content="{StaticResource Changelog_Close}"
                ToolTip.Tip="{StaticResource Changelog_Close}"
                Click="Close_Click"/>

        <TextBlock DockPanel.Dock="Top"
                   IsVisible="{Binding IsEmpty}"
                   Text="{Binding EmptyMessage}"
                   Foreground="#bbb" FontSize="13" TextWrapping="Wrap"/>

        <ScrollViewer IsVisible="{Binding HasReleases}">
            <ItemsControl ItemsSource="{Binding Releases}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="0,0,0,14">
                            <TextBlock FontWeight="Bold" FontSize="14" Foreground="#ddd">
                                <TextBlock.Text>
                                    <MultiBinding StringFormat="{}{0} — {1}">
                                        <Binding Path="Version"/>
                                        <Binding Path="Date"/>
                                    </MultiBinding>
                                </TextBlock.Text>
                            </TextBlock>
                            <ItemsControl ItemsSource="{Binding Sections}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Margin="0,4,0,0">
                                            <TextBlock Text="{Binding Heading}"
                                                       IsVisible="{Binding HasHeading}"
                                                       Foreground="#9ad" FontSize="11" FontWeight="Bold"/>
                                            <ItemsControl ItemsSource="{Binding Entries}">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate>
                                                        <TextBlock Text="{Binding StringFormat='• {0}'}"
                                                                   Foreground="#bbb" FontSize="12"
                                                                   TextWrapping="Wrap" Margin="6,0,0,0"/>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

- [ ] **Step 4: Create the code-behind**

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow() => InitializeComponent();

    public ChangelogWindow(ChangelogViewModel viewModel) : this()
        => DataContext = viewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
```

> **If the project uses source-generated XAML** (other views call `InitializeComponent()` without a hand-written loader), match that pattern instead: remove the explicit `InitializeComponent` method body and let the generator provide it, exactly like a sibling window such as `DiffWindow.axaml.cs`. Check `DiffWindow.axaml.cs` first and copy its `InitializeComponent` style.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ChangelogWindowTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/ChangelogWindow.axaml DialogEditor.Avalonia/Views/ChangelogWindow.axaml.cs DialogEditor.Tests/Views/ChangelogWindowTests.cs
git commit -m "feat: add ChangelogWindow reader"
```

---

## Task 11: `AboutWindow`

**Files:**
- Create: `DialogEditor.Avalonia/Views/AboutWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/AboutWindow.axaml.cs`
- Test: `DialogEditor.Tests/Views/AboutWindowTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class AboutWindowTests
{
    public AboutWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Constructs_AndShows()
    {
        var vm = new AboutViewModel("1.2.3", "https://repo", "https://docs");
        var window = new AboutWindow(vm);
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AboutWindowTests"`
Expected: FAIL — `AboutWindow` does not exist.

- [ ] **Step 3: Create the XAML**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.AboutWindow"
        Title="{StaticResource About_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="420" Height="320" CanResize="False"
        ShowInTaskbar="False" Background="#1a1a2a">
    <StackPanel Margin="18,16,18,16" Spacing="8">
        <TextBlock Text="{Binding AppName}" Foreground="#eee" FontSize="18" FontWeight="Bold"/>
        <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="{StaticResource About_VersionLabel}" Foreground="#888" FontSize="12"/>
            <TextBlock Text="{Binding Version}" Foreground="#ccc" FontSize="12"/>
        </StackPanel>
        <TextBlock Text="{Binding Description}" Foreground="#bbb" FontSize="12" TextWrapping="Wrap"/>
        <TextBlock Text="{Binding License}" Foreground="#999" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"/>
        <TextBlock Text="{Binding Credits}" Foreground="#999" FontSize="11" TextWrapping="Wrap"/>

        <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
            <Button Content="{StaticResource About_OpenRepository}"
                    ToolTip.Tip="{StaticResource About_OpenRepositoryTip}"
                    Command="{Binding OpenRepositoryCommand}"/>
            <Button Content="{StaticResource About_OpenDocs}"
                    ToolTip.Tip="{StaticResource About_OpenDocsTip}"
                    Command="{Binding OpenDocsCommand}"/>
        </StackPanel>

        <TextBlock Text="{Binding Status}" Foreground="#e0a" FontSize="11" TextWrapping="Wrap"/>

        <Button HorizontalAlignment="Right" Margin="0,8,0,0"
                Content="{StaticResource About_Close}"
                ToolTip.Tip="{StaticResource About_Close}"
                Click="Close_Click"/>
    </StackPanel>
</Window>
```

- [ ] **Step 4: Create the code-behind**

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    public AboutWindow(AboutViewModel viewModel) : this()
        => DataContext = viewModel;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
```

> Same XAML-loader caveat as Task 10, Step 4 — match `DiffWindow.axaml.cs`.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AboutWindowTests"`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/AboutWindow.axaml DialogEditor.Avalonia/Views/AboutWindow.axaml.cs DialogEditor.Tests/Views/AboutWindowTests.cs
git commit -m "feat: add AboutWindow"
```

---

## Task 12: Wire the Help menu + window callbacks

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml:136` (after the `OpenWalkthrough` menu item, before `</MenuItem>` at line 137)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (in the VM-callback setup block, alongside `vm.ShowImportWarnings = ...` around lines 54-77)

No new automated test — covered by the window + VM tests. Verified manually in Task 13.

- [ ] **Step 1: Add the menu items**

In `MainWindow.axaml`, insert after the `Open Walkthrough…` `MenuItem` (currently ending line 136) and before the closing `</MenuItem>` of the Help menu:

```xml
                        <Separator/>
                        <MenuItem Header="{StaticResource Menu_Changelog}"
                                  ToolTip.Tip="{StaticResource Menu_ChangelogTip}"
                                  Command="{Binding ChangelogCommand}"/>
                        <MenuItem Header="{StaticResource Menu_About}"
                                  ToolTip.Tip="{StaticResource Menu_AboutTip}"
                                  Command="{Binding AboutCommand}"/>
```

- [ ] **Step 2: Wire the window-open callbacks**

In `MainWindow.axaml.cs`, in the block that assigns `vm.Show...` callbacks (near line 55), add:

```csharp
        vm.ShowChangelog = changelogVm =>
        {
            var window = new ChangelogWindow(changelogVm);
            window.Show();
            window.Activate();
        };
        vm.ShowAbout = aboutVm =>
        {
            var window = new AboutWindow(aboutVm);
            window.Show();
            window.Activate();
        };
```

(`DialogEditor.Avalonia.Views` is the same namespace as `MainWindow`, so no extra using is needed.)

- [ ] **Step 3: Build**

Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat: wire Changelog/About into the Help menu"
```

---

## Task 13: Seed the frozen `CHANGELOG.md` and bundle it

**Files:**
- Create: `CHANGELOG.md` (repo root)
- Modify: `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj:36` (after the walkthrough `Content` element, inside the same `ItemGroup` at lines 32-37)

- [ ] **Step 1: Create the placeholder changelog**

Per the CLAUDE.md "Changelog" rule, this stays effectively empty until the initial release.

```markdown
# Changelog

All notable changes to this project will be documented in this file.

This project follows [Keep a Changelog](https://keepachangelog.com/) and
[Semantic Versioning](https://semver.org/).

## [Unreleased]

_The first public release has not happened yet. Release notes will appear here once it does._
```

(The `## [Unreleased]` heading has no date, so `ChangelogParser` ignores it and the in-app
reader shows the empty-state message — the intended pre-release behaviour.)

- [ ] **Step 2: Bundle it next to the executable**

In `DialogEditor.Avalonia.csproj`, after the walkthrough `Content` element (line 36), inside the same `ItemGroup`, add:

```xml
    <!-- Ship the changelog next to the executable so Help > Changelog finds it. -->
    <Content Include="..\CHANGELOG.md" Link="CHANGELOG.md">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
```

- [ ] **Step 3: Verify it copies to output**

Run: `dotnet build DialogEditor.Avalonia`
Then confirm the file is in the output:
Run: `dotnet build DialogEditor.Avalonia && powershell -Command "Test-Path (Join-Path (Get-ChildItem -Recurse -Filter DialogEditor.Avalonia.dll -Path DialogEditor.Avalonia/bin | Select-Object -First 1).DirectoryName 'CHANGELOG.md')"`
Expected: `True`.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md DialogEditor.Avalonia/DialogEditor.Avalonia.csproj
git commit -m "feat: seed frozen CHANGELOG.md and bundle with the app"
```

---

## Task 14: Full suite + Gaps.md closeout

**Files:**
- Modify: `Gaps.md` (the *About / Version Info* and *Changelog / Release Notes* entries)

- [ ] **Step 1: Run the whole test suite**

Run: `dotnet test`
Expected: PASS (all tests, including the 7 new test classes).

- [ ] **Step 2: Mark the gaps implemented**

In `Gaps.md`, update the **About / Version Info** and **Changelog / Release Notes** entries to note they are now implemented (Help ▸ About… / Help ▸ Changelog…), mirroring how other shipped entries are phrased (e.g. the Version Control "implemented" notes). Keep the changelog entry's reminder that `CHANGELOG.md` stays frozen until the initial release (per the CLAUDE.md rule).

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs: mark Changelog + About gaps implemented"
```

---

## Self-Review (completed by plan author)

**Spec coverage:** Source file (T13) · `ChangelogParser` w/ grouped subsections (T3–T4) · `AppVersion` shared w/ CLI (T1–T2) · `ChangelogWindow` (T10) · `AboutWindow` (T11) · Help menu items + commands (T8, T12) · localization (T9) · error handling — missing/empty/malformed changelog (T4, T8, T10), version fallback (T1), link failure (T7) · TDD across all logic tasks · frozen-changelog decision honoured (T13 + CLAUDE.md). Version-awareness correctly absent (out of scope).

**Placeholder scan:** No "TBD/TODO" in code steps. Two flagged *open items* (About copy text in T9; reuse of existing VM test doubles in T8) are explicit confirm-at-step notes with concrete defaults, not blanks.

**Type consistency:** `ChangelogRelease(Version, Date, Sections)` and `ChangelogSection(Heading, Entries)` used identically in T3/T4/T6/T8/T10. `ChangelogReader`/`ShowChangelog`/`ShowAbout` seam names and `ChangelogCommand`/`AboutCommand` generated-command names match across T8/T10/T11/T12. `UrlOpener` consistent in T5/T7. `RepositoryUrl`/`DocsUrl` consistent T7/T8.
