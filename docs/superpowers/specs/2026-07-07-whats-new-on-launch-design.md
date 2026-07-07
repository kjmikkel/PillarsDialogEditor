# "What's New Since Your Last Run" on Launch — Design

**Date:** 2026-07-07
**Status:** Approved
**Gap:** `Gaps.md` › "Changelog / Release Notes" — the deferred *"version-aware 'what's new since your last run' layer"*.
**Builds on:** `2026-06-07-changelog-and-about-design.md` (`ChangelogParser`, `ChangelogRelease`, `ChangelogViewModel`, `ChangelogWindow`, `AppVersion`).

## Problem

The in-app Changelog reader (**Help ▸ Changelog…**) lets a user *look up* release
notes, but nothing tells them what changed when they launch a newly-upgraded
build. The changelog-and-about design explicitly deferred a "what's new since
your last run" layer. This adds it: on launch, if the app version has advanced
since the user last ran it, auto-open the changelog filtered to just the new
releases.

Note: per the CLAUDE.md **Changelog** rule, `CHANGELOG.md` is frozen (effectively
empty) until the initial public release. This feature ships the *mechanism* —
version tracking, new-release filtering, the launch surface — which stays dormant
(shows nothing) until the changelog is populated at release, then lights up
automatically. The mechanism is fully testable now with a stub changelog.

## Decisions (settled during brainstorming)

- **Surface — reuse `ChangelogWindow`, filtered.** Auto-open the existing window
  showing only the releases new since the last-seen version, with a "what's new"
  header. DRY: one extra `ChangelogViewModel` mode, no new window. (Rejected: a
  dedicated dialog — duplicates release rendering; an in-app banner — needs new
  MainWindow chrome.)
- **First upgrade that adds this feature — set baseline silently.** An existing
  user whose settings predate the feature (no last-seen recorded) gets the current
  version recorded with nothing shown; the greeting activates from the *next*
  upgrade. (Rejected: dumping the full historical changelog on that first upgrade.)
- **No semantic-version math.** The changelog is stored newest-first, so "what's
  new since X" is a walk from the top until the release matching X — arbitrary
  version schemes (pre-release suffixes, etc.) need no parsing/comparison.

## Architecture

Pure decision logic in `DialogEditor.Patch.Changelog`; orchestration on
`MainWindowViewModel` behind injectable seams (no `AppSettings` global state in
tests); a thin startup trigger in the View. Follows the house isolation pattern.

### 1. Persistence — `AppSettings.LastSeenVersion`

A new `string LastSeenVersion` (default `""`), with the same static get/set + JSON
round-trip as the existing settings. A single uniform rule replaces the
bool-migration nuance (default-false-no-file vs default-true-missing-key): **empty
string means "no baseline yet"**, which covers *both* a fresh install and the
first upgrade that introduces the key — both of which are "set baseline silently".

### 2. Pure decider — `WhatsNewDecider` (`DialogEditor.Patch.Changelog`)

Sits next to `ChangelogParser`; no dependencies beyond `ChangelogRelease`.

```csharp
public sealed record WhatsNewResult(IReadOnlyList<ChangelogRelease> ReleasesToShow);

public static class WhatsNewDecider
{
    public static WhatsNewResult Decide(
        string lastSeen, string current, IReadOnlyList<ChangelogRelease> all);
}
```

Rules:
- `lastSeen` is empty **or** `lastSeen == current` → `ReleasesToShow = []`
  (baseline / nothing new).
- otherwise → walk `all` (newest-first) from the top, collecting releases until one
  whose `Version == lastSeen` (exclusive); that slice is what's new. If no release
  matches `lastSeen`, return all releases (an unrecognized last-seen is treated as
  very old; bounded by the changelog size).

`ShouldShow` is simply `ReleasesToShow.Count > 0` (computed by the caller).

**Build-metadata normalization.** `AppVersion.Current` is the assembly
`InformationalVersion`, which carries a `+<git-hash>` semver build-metadata suffix
(e.g. `1.0.0+9c2d3a55…`) that changelog headings never have. `Decide` strips
everything from the first `+` on `lastSeen`, `current`, and each release `Version`
before comparing, so versions match on their released identity and a per-commit
hash change on rebuilds doesn't spuriously re-trigger the greeting. (Discovered
during app verification; unit tests originally used clean version strings.)

### 3. `ChangelogViewModel` — "what's new" mode

Add an optional constructor path:

```csharp
public ChangelogViewModel(
    IReadOnlyList<ChangelogRelease> releases,
    bool isWhatsNew = false,
    string version = "");
```

New members:
- `bool IsWhatsNew` — true when opened as the launch greeting.
- `string HeaderText` — `Loc.Format("WhatsNew_Header", version)` when `IsWhatsNew`,
  else the existing changelog framing (`Loc.Get("Changelog_Header")` or equivalent
  already used by the window; if the window currently has no bound header, add one).

The existing `Changelog()` command keeps calling `new ChangelogViewModel(releases)`
(full log, `IsWhatsNew = false`). The launch path builds
`new ChangelogViewModel(newReleases, isWhatsNew: true, version: current)`.

### 4. Orchestration — `MainWindowViewModel.ShowWhatsNewIfUpdated()`

Reuses the existing `ChangelogReader` (read CHANGELOG.md text) and `ShowChangelog`
(open the window) seams. Adds injectable seams for the rest so tests never touch
`AppSettings` statics (mirrors the settable `ChangelogReader`):

```csharp
public Func<string>?   LastSeenVersionGetter { get; set; }   // default: () => AppSettings.LastSeenVersion
public Action<string>? LastSeenVersionSetter { get; set; }   // default: v => AppSettings.LastSeenVersion = v
public Func<string>?   CurrentVersionProvider { get; set; }  // default: () => AppVersion.Current

public void ShowWhatsNewIfUpdated();
```

Method flow:
1. `current = CurrentVersionProvider()`. If `current` is null/empty/`"unknown"` →
   return (no show, no persist — never poison the baseline with a bad version).
2. `lastSeen = LastSeenVersionGetter()`.
3. Read + parse the changelog (reusing `ChangelogReader` / `ChangelogParser`, same
   as the `Changelog()` command; a null read yields an empty release list).
4. `result = WhatsNewDecider.Decide(lastSeen, current, releases)`.
5. If `result.ReleasesToShow.Count > 0` →
   `ShowChangelog?.Invoke(new ChangelogViewModel(result.ReleasesToShow, isWhatsNew: true, version: current))`.
6. `LastSeenVersionSetter(current)` — always (so it never re-shows for this version).

### 5. Startup trigger — `MainWindow.axaml.cs`

After the existing `vm.ShowChangelog = …` wiring, call `vm.ShowWhatsNewIfUpdated()`
once. Both startup paths converge on a constructed `MainWindow` whose wiring runs
(fresh install: onboarding → MainWindow; returning user: MainWindow directly), so a
single call site covers every case. Fresh installs hit the empty-baseline branch
and silently record the version.

## Behavior matrix

| Situation | `LastSeenVersion` | Outcome |
|---|---|---|
| Fresh install | `""` | Onboarding shows; empty branch records current, shows nothing |
| First upgrade adding the feature | `""` (key absent) | Baseline recorded silently; nothing shown |
| Normal upgrade | `1.1.0`, current `1.3.0` | Filtered window auto-opens (1.3.0 + 1.2.0); records `1.3.0` |
| Relaunch, no upgrade | `== current` | Nothing shown |
| Version `"unknown"` | any | Nothing shown, nothing persisted |
| Empty changelog (frozen, today) | non-empty ≠ current | `Decide` → `[]`; nothing shown (dormant until CHANGELOG populates) |

## Cross-cutting rules (CLAUDE.md)

- **Localisation** — new keys `WhatsNew_Header` ("What's new in {0}") and
  `WhatsNew_Title` in `Strings.axaml`; header/title via `Loc`. No hardcoded strings.
- **Tooltips** — no new interactive controls (reuses `ChangelogWindow` and its Close
  button); nothing to add.
- **Error handling** — reuses the existing changelog read, which already logs via
  `AppLog.Warn` on an unavailable file; the decider is pure and non-throwing; no bare
  catch.
- **UI Automation** — the reused window is already automation-discoverable; the
  what's-new title/header is name-bearing text.
- **Tests run serially** — the injectable seams keep `ShowWhatsNewIfUpdated` tests
  off the `AppSettings` statics, honoring the global-state test-race constraint.

## Testing (TDD, red first)

**`WhatsNewDeciderTests` (unit):**
- Empty `lastSeen` → `[]`.
- `lastSeen == current` → `[]`.
- Normal slice: `lastSeen` in the middle → releases above it only (exclusive).
- `lastSeen` is the newest release → `[]`.
- `lastSeen` not found in the changelog → all releases.
- Empty changelog → `[]`.

**`AppSettingsLastSeenVersionTests`:** default is `""`; round-trips a set value.

**`MainWindowViewModelWhatsNewTests`** (injected seams, stub `ChangelogReader`,
capturing `ShowChangelog`):
- Upgrade (lastSeen `1.0.0`, current `1.1.0`, changelog has both) → `ShowChangelog`
  invoked once with a `IsWhatsNew` VM whose releases are the new slice; setter called
  with `1.1.0`.
- Equal versions → `ShowChangelog` not invoked; setter still called with current.
- Empty lastSeen → not invoked; setter called with current (baseline).
- `current == "unknown"` → not invoked; setter **not** called.
- Empty changelog on a real upgrade → not invoked; setter called with current.

**`ChangelogViewModel` what's-new mode:** `IsWhatsNew` true and `HeaderText`
formats the version; default constructor path stays `IsWhatsNew == false`.

## Out of scope / deferred

- The CHANGELOG.md content (frozen until public release).
- Per-entry "new" highlighting within a release.
- A "don't show again" toggle — the once-per-version semantics already cover it.
- Showing what's-new as anything other than the reused Changelog window.
