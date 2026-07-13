# Find in Project (read-only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A project-wide, read-only, navigable find across the effective text of every patched conversation — the read-only counterpart to Batch Replace.

**Architecture:** A pure `ProjectFindService` mirrors `ProjectVoRowScanner`'s walk (live snapshot for the open conversation, base+patch for the rest, skip-unreadable). A `ProjectFindViewModel` runs it off the UI thread and exposes results + a navigate event. A non-modal `FindInProjectWindow` shows results; MainWindow wires the gate, the Ctrl+Shift+F shortcut, and cross-conversation node navigation.

**Tech Stack:** C# / .NET 8, Avalonia, CommunityToolkit.Mvvm (`[RelayCommand]`/`[ObservableProperty]`), xUnit + Avalonia.Headless.XUnit.

## Global Constraints

- **Localisation:** no user-visible string hard-coded in XAML or C#. Keys in `DialogEditor.Avalonia/Resources/Strings.axaml`, referenced via `{DynamicResource}` (XAML) or `Loc.Get`/`Loc.Format` (C#). Enforced by `NoHardcodedUiStringsTests` / `NoStaticStringResourceTests`.
- **Tooltips + UIA:** every new interactive control carries a detailed `ToolTip.Tip` + mirrored `AutomationProperties.HelpText` (OK/Cancel-style buttons exempt). Enforced by `AutomationHelpTextTests`.
- **Window icon:** any new `<Window>` sets `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **Error handling (production):** every caught exception logs via `AppLog.Error`/`AppLog.Warn` except `OperationCanceledException` (swallow silently). No bare `catch {}`.
- **ViewModels layer has NO Avalonia dependency** (`DialogEditor.ViewModels` / `DialogEditor.Patch`).
- **TDD:** red → green → refactor; never implement before a failing test exists.
- **Tests run SERIALLY** (`DialogEditor.Tests`): `AppSettings`/`Loc` are global. Configure `Loc.Configure(new StubStringProvider())` in test ctors that resolve strings; `StubStringProvider` echoes keys, so assert on structure, not translated text.
- **Case comparison:** `StringComparison.Ordinal` (case-sensitive) / `OrdinalIgnoreCase`, matching Batch Replace.
- **Primary language** is `provider.Language`.

---

### Task 1: Query/row types + snippet helper

**Files:**
- Create: `DialogEditor.ViewModels/Services/ProjectFindTypes.cs`
- Create: `DialogEditor.ViewModels/Services/FindSnippet.cs`
- Test: `DialogEditor.Tests/Services/FindSnippetTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record ProjectFindQuery(string Text, bool CaseSensitive = false, bool InLinkChoice = false, bool InTranslations = false, bool InNodeComments = false)`
  - `record FindMatchRow(string ConversationName, int NodeId, string FieldLabel, string Language, string Snippet)`
  - `static string FindSnippet.Extract(string text, int matchIndex, int matchLength, int context = 30)`

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Services/FindSnippetTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class FindSnippetTests
{
    [Fact]
    public void ShortText_ReturnedWhole_NoEllipsis()
    {
        Assert.Equal("hello world", FindSnippet.Extract("hello world", 6, 5));
    }

    [Fact]
    public void LeadingContext_TrimmedWithEllipsis()
    {
        var text = new string('a', 50) + "MATCH" + new string('b', 50);
        var snip = FindSnippet.Extract(text, 50, 5, context: 10);
        Assert.StartsWith("…", snip);
        Assert.EndsWith("…", snip);
        Assert.Contains("MATCH", snip);
    }

    [Fact]
    public void Newlines_FlattenedToSpaces()
    {
        var snip = FindSnippet.Extract("line1\r\nMATCH\nline3", 7, 5, context: 10);
        Assert.DoesNotContain("\n", snip);
        Assert.DoesNotContain("\r", snip);
        Assert.Contains("MATCH", snip);
    }

    [Fact]
    public void MatchAtStart_NoLeadingEllipsis()
    {
        var snip = FindSnippet.Extract("MATCH" + new string('b', 50), 0, 5, context: 10);
        Assert.StartsWith("MATCH", snip);
        Assert.EndsWith("…", snip);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FindSnippet"`
Expected: FAIL — `FindSnippet` / `ProjectFindQuery` types don't exist (compile error).

- [ ] **Step 3: Create the types**

`DialogEditor.ViewModels/Services/ProjectFindTypes.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// A project-wide find query. Default + Female node text are always searched;
/// the three flags add optional coverage (link/choice text, all-language
/// translation overlays, writer node comments).
public sealed record ProjectFindQuery(
    string Text,
    bool CaseSensitive = false,
    bool InLinkChoice = false,
    bool InTranslations = false,
    bool InNodeComments = false);

/// One located match. Language is "" for the primary language (shown as the
/// primary label in the view); FieldLabel is a localized field-kind string.
public sealed record FindMatchRow(
    string ConversationName,
    int NodeId,
    string FieldLabel,
    string Language,
    string Snippet);
```

`DialogEditor.ViewModels/Services/FindSnippet.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// Builds a one-line, context-bounded preview around a match for a find result.
public static class FindSnippet
{
    public static string Extract(string text, int matchIndex, int matchLength, int context = 30)
    {
        var start = Math.Max(0, matchIndex - context);
        var end   = Math.Min(text.Length, matchIndex + matchLength + context);
        var slice = text[start..end].Replace("\r", " ").Replace("\n", " ");
        var prefix = start > 0 ? "…" : "";
        var suffix = end < text.Length ? "…" : "";
        return prefix + slice + suffix;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FindSnippet"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/ProjectFindTypes.cs DialogEditor.ViewModels/Services/FindSnippet.cs DialogEditor.Tests/Services/FindSnippetTests.cs
git commit -m "feat(find-in-project): query/row types + snippet helper"
```

---

### Task 2: ProjectFindService (the walk)

**Files:**
- Create: `DialogEditor.ViewModels/Services/ProjectFindService.cs`
- Test: `DialogEditor.Tests/Services/ProjectFindServiceTests.cs`

**Interfaces:**
- Consumes: `ProjectFindQuery`, `FindMatchRow`, `FindSnippet` (Task 1); `DialogProject`, `IGameDataProvider`, `ConversationSnapshotBuilder`, `PatchApplier`, `ConversationEditSnapshot` (existing), `AppLog`, `Loc`.
- Produces: `static IReadOnlyList<FindMatchRow> ProjectFindService.Search(DialogProject project, IGameDataProvider provider, string primaryLanguage, ProjectFindQuery query, string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null)`

**Reference the existing walk** in `DialogEditor.ViewModels/Services/ProjectVoRowScanner.cs` (BuildRows) — same structure: open conversation uses `openSnapshot`; others load `provider.FindConversation(convName)` → `ConversationSnapshotBuilder.Build(provider.LoadConversation(file))` → `PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true)`; an unreadable conversation is `AppLog.Warn` + `continue`. Field labels are resolved through `Loc.Get` (this service already lives in the ViewModels layer, like `ProjectTextTagScanner`).

Key correctness points (from the spec + the codebase):
- `NodeEditSnapshot.DefaultText`/`FemaleText` are `[JsonIgnore]`, so **added nodes loaded from disk carry empty text** — the primary text then lives in `patch.Translations[primaryLanguage]`. For the always-on Default/Female search, fall back to the primary translation entry when the snapshot field is empty (this is what `ProjectVoRowScanner` does at its lines ~81-83).
- To avoid double-listing the primary language, `InTranslations` searches only languages **other than** `primaryLanguage`; the primary language is already covered by the Default/Female fallback above.
- Node comments come from `patch.NodeComments` (`IReadOnlyDictionary<int,string>`), NOT the snapshot's `Comments` field (that is game data; Batch Replace does not search it either).

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Services/ProjectFindServiceTests.cs`. Use the existing test stubs/helpers the sibling `ProjectVoRowScannerTests` uses to build a `DialogProject` + a stub `IGameDataProvider` — open `DialogEditor.Tests/Services/ProjectVoRowScannerTests.cs` and reuse its project/provider construction helpers verbatim (same `StubGameDataProvider`, same patch-building). Configure `Loc.Configure(new StubStringProvider())` in the ctor.

Write these tests (adapt the project/provider setup to the sibling test's helpers):

```csharp
// Ctor: Loc.Configure(new StubStringProvider());

[Fact] // Default text match, primary language label ""
public void FindsDefaultText_PrimaryLanguage()
{
    var (project, provider) = ProjectWith(conv: "c1", nodeId: 1, defaultText: "The Watcher speaks");
    var rows = ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("Watcher"));
    var row = Assert.Single(rows);
    Assert.Equal("c1", row.ConversationName);
    Assert.Equal(1, row.NodeId);
    Assert.Equal("", row.Language);
    Assert.Contains("Watcher", row.Snippet);
}

[Fact] // Case sensitivity
public void CaseSensitive_DoesNotMatchDifferentCase()
{
    var (project, provider) = ProjectWith("c1", 1, "The Watcher");
    Assert.Empty(ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("watcher", CaseSensitive: true)));
    Assert.Single(ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("watcher", CaseSensitive: false)));
}

[Fact] // Link/choice text only when toggled
public void LinkChoiceText_OnlyWhenToggled()
{
    var (project, provider) = ProjectWithLink("c1", 1, linkText: "Ask about Caed Nua");
    Assert.Empty(ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("Caed Nua")));                         // off by default
    Assert.Single(ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("Caed Nua", InLinkChoice: true)));
}

[Fact] // Node comment only when toggled; comes from patch.NodeComments
public void NodeComment_OnlyWhenToggled()
{
    var (project, provider) = ProjectWithComment("c1", 1, comment: "revisit this line");
    Assert.Empty(ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("revisit")));
    Assert.Single(ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("revisit", InNodeComments: true)));
}

[Fact] // Translation language labeled; primary not duplicated
public void Translation_LabeledByLanguage_WhenToggled()
{
    var (project, provider) = ProjectWithTranslation("c1", 1, lang: "de", text: "Der Wächter");
    var rows = ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("Wächter", InTranslations: true));
    var row = Assert.Single(rows);
    Assert.Equal("de", row.Language);
}

[Fact] // Open conversation uses the passed live snapshot (unsaved edit), not disk
public void OpenConversation_UsesLiveSnapshot()
{
    var (project, provider) = ProjectWith("c1", 1, defaultText: "on disk");
    var live = SnapshotWith(nodeId: 1, defaultText: "unsaved edit XYZ");
    var rows = ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("XYZ"), openConversationName: "c1", openSnapshot: live);
    Assert.Single(rows);
}

[Fact] // Unreadable conversation is skipped, others returned
public void UnreadableConversation_Skipped()
{
    var (project, provider) = TwoConvsOneThrows(good: "c1", bad: "c2", text: "findme");
    var rows = ProjectFindService.Search(project, provider, "en",
        new ProjectFindQuery("findme"));
    Assert.All(rows, r => Assert.Equal("c1", r.ConversationName));
}
```

Define the small `ProjectWith*` / `SnapshotWith` / `TwoConvsOneThrows` helpers at the bottom of the test class, built on the SAME `StubGameDataProvider` and patch/snapshot construction that `ProjectVoRowScannerTests` uses (read that file and mirror it — do not invent a new provider). `SnapshotWith` returns a `ConversationEditSnapshot` with one `NodeEditSnapshot` (all the non-text positional fields can be defaults: `SpeakerCategory` default, empty strings, empty lists).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ProjectFindService"`
Expected: FAIL — `ProjectFindService` doesn't exist.

- [ ] **Step 3: Implement the service**

`DialogEditor.ViewModels/Services/ProjectFindService.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// Project-wide read-only find over the EFFECTIVE text (vanilla base + the writer's
/// edits) of every patched conversation. Mirrors ProjectVoRowScanner's walk: the open
/// conversation is searched via its live snapshot (unsaved edits included); every other
/// patched conversation is loaded vanilla + patch; an unreadable conversation is skipped.
/// Spec: docs/superpowers/specs/2026-07-12-find-in-project-design.md
public static class ProjectFindService
{
    public static IReadOnlyList<FindMatchRow> Search(
        DialogProject project, IGameDataProvider provider, string primaryLanguage,
        ProjectFindQuery query,
        string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null)
    {
        var rows = new List<FindMatchRow>();
        if (string.IsNullOrEmpty(query.Text)) return rows;
        var cmp = query.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var (convName, patch) in project.Patches)
        {
            ConversationEditSnapshot snap;
            if (convName == openConversationName && openSnapshot is not null)
            {
                snap = openSnapshot;
            }
            else
            {
                try
                {
                    var file     = provider.FindConversation(convName);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);
                    snap = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Find in project: could not load '{convName}': {ex.Message}");
                    continue;
                }
            }

            var primaryTranslations = (patch.Translations.GetValueOrDefault(primaryLanguage) ?? [])
                .ToDictionary(t => t.NodeId);

            foreach (var node in snap.Nodes)
            {
                // Added nodes loaded from disk have empty [JsonIgnore] text; fall back
                // to the primary translation entry (ProjectVoRowScanner precedent).
                var def = !string.IsNullOrEmpty(node.DefaultText) ? node.DefaultText
                        : primaryTranslations.TryGetValue(node.NodeId, out var pt) ? pt.DefaultText ?? "" : "";
                var fem = !string.IsNullOrEmpty(node.FemaleText) ? node.FemaleText
                        : primaryTranslations.TryGetValue(node.NodeId, out var pf) ? pf.FemaleText ?? "" : "";

                Check(convName, node.NodeId, "FindField_DefaultText", "", def, query.Text, cmp, rows);
                Check(convName, node.NodeId, "FindField_FemaleText",  "", fem, query.Text, cmp, rows);

                if (query.InLinkChoice)
                    foreach (var link in node.Links)
                        Check(convName, node.NodeId, "FindField_LinkChoice", "",
                              link.QuestionNodeTextDisplay, query.Text, cmp, rows);

                if (query.InNodeComments &&
                    patch.NodeComments.TryGetValue(node.NodeId, out var comment))
                    Check(convName, node.NodeId, "FindField_NodeComment", "",
                          comment, query.Text, cmp, rows);
            }

            if (query.InTranslations)
                foreach (var (lang, entries) in patch.Translations)
                {
                    if (string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase))
                        continue; // primary covered by the Default/Female fallback
                    foreach (var t in entries)
                    {
                        Check(convName, t.NodeId, "FindField_DefaultText", lang, t.DefaultText ?? "", query.Text, cmp, rows);
                        Check(convName, t.NodeId, "FindField_FemaleText",  lang, t.FemaleText ?? "", query.Text, cmp, rows);
                    }
                }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.FieldLabel, StringComparer.Ordinal)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();
    }

    private static void Check(string conv, int nodeId, string fieldKey, string lang,
        string value, string search, StringComparison cmp, List<FindMatchRow> rows)
    {
        if (string.IsNullOrEmpty(value)) return;
        var idx = value.IndexOf(search, cmp);
        if (idx < 0) return;
        rows.Add(new FindMatchRow(conv, nodeId, Loc.Get(fieldKey), lang,
            FindSnippet.Extract(value, idx, search.Length)));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ProjectFindService"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/ProjectFindService.cs DialogEditor.Tests/Services/ProjectFindServiceTests.cs
git commit -m "feat(find-in-project): ProjectFindService effective-text walk"
```

---

### Task 3: ProjectFindViewModel

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/ProjectFindViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/ProjectFindViewModelTests.cs`

**Interfaces:**
- Consumes: `ProjectFindService`, `ProjectFindQuery`, `FindMatchRow` (Tasks 1-2); `DialogProject`, `IGameDataProvider`, `ConversationEditSnapshot`.
- Produces: `ProjectFindViewModel` with `SearchText`, `CaseSensitive`, `InLinkChoice`, `InTranslations`, `InNodeComments`, `SearchCommand`, `IReadOnlyList<FindMatchRow> Results`, `string StatusText`, and `event Action<string, int>? RequestNavigate` raised by `NavigateTo(FindMatchRow)`.

Constructor takes the data + an accessor for the current open conversation, so it can re-capture the live snapshot at each search:

```csharp
public ProjectFindViewModel(
    DialogProject project, IGameDataProvider provider, string primaryLanguage,
    Func<(string? Name, ConversationEditSnapshot? Snapshot)> openConversationAccessor)
```

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/ProjectFindViewModelTests.cs` (ctor: `Loc.Configure(new StubStringProvider())`; reuse the same stub project/provider helpers as Task 2):

```csharp
[Fact]
public void Search_PopulatesResults_AndStatus()
{
    var (project, provider) = ProjectWith("c1", 1, "The Watcher");
    var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
    { SearchText = "Watcher" };
    vm.SearchCommand.Execute(null);
    Assert.Single(vm.Results);
    Assert.False(string.IsNullOrEmpty(vm.StatusText));
}

[Fact]
public void Search_EmptyText_CommandDisabled()
{
    var (project, provider) = ProjectWith("c1", 1, "x");
    var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null));
    Assert.False(vm.SearchCommand.CanExecute(null));
    vm.SearchText = "x";
    Assert.True(vm.SearchCommand.CanExecute(null));
}

[Fact]
public void Toggles_FlowIntoQuery()
{
    var (project, provider) = ProjectWithLink("c1", 1, "Ask about X");
    var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
    { SearchText = "Ask about X" };
    vm.SearchCommand.Execute(null);
    Assert.Empty(vm.Results);                 // link off
    vm.InLinkChoice = true;
    vm.SearchCommand.Execute(null);
    Assert.Single(vm.Results);
}

[Fact]
public void NavigateTo_RaisesRequestNavigate_WithTarget()
{
    var (project, provider) = ProjectWith("c1", 7, "The Watcher");
    var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
    { SearchText = "Watcher" };
    vm.SearchCommand.Execute(null);
    (string, int)? got = null;
    vm.RequestNavigate += (c, n) => got = (c, n);
    vm.NavigateTo(vm.Results[0]);
    Assert.Equal(("c1", 7), got);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ProjectFindViewModel"`
Expected: FAIL — type doesn't exist.

- [ ] **Step 3: Implement the ViewModel**

`DialogEditor.ViewModels/ViewModels/ProjectFindViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ProjectFindViewModel : ObservableObject
{
    private readonly DialogProject _project;
    private readonly IGameDataProvider _provider;
    private readonly string _primaryLanguage;
    private readonly Func<(string? Name, ConversationEditSnapshot? Snapshot)> _openAccessor;

    public ProjectFindViewModel(
        DialogProject project, IGameDataProvider provider, string primaryLanguage,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> openConversationAccessor)
    {
        _project = project;
        _provider = provider;
        _primaryLanguage = primaryLanguage;
        _openAccessor = openConversationAccessor;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _inLinkChoice;
    [ObservableProperty] private bool _inTranslations;
    [ObservableProperty] private bool _inNodeComments;
    [ObservableProperty] private string _statusText = string.Empty;

    public IReadOnlyList<FindMatchRow> Results { get; private set; } = [];

    /// Raised with (conversationName, nodeId) when the user activates a result.
    public event Action<string, int>? RequestNavigate;

    private bool CanSearch() => !string.IsNullOrEmpty(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        var (name, snap) = _openAccessor();
        var query = new ProjectFindQuery(SearchText, CaseSensitive, InLinkChoice, InTranslations, InNodeComments);
        Results = ProjectFindService.Search(_project, _provider, _primaryLanguage, query, name, snap);
        StatusText = Results.Count > 0
            ? Loc.FormatCount("FindInProject_Matches", Results.Count)
            : Loc.Get("FindInProject_NoMatches");
        OnPropertyChanged(nameof(Results));
    }

    public void NavigateTo(FindMatchRow row) => RequestNavigate?.Invoke(row.ConversationName, row.NodeId);
}
```

Note on threading: `Search` is synchronous here for testability and matches `FindReplaceViewModel`. The IO cost (loading conversations) is deferred to the caller in Task 5, which runs it via the window on a background task if needed; for a first version the synchronous button-triggered search is acceptable. Add the two resource keys `FindInProject_Matches` (plural: `_One`/`_Other`) and `FindInProject_NoMatches` to `Strings.axaml` in Task 4.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ProjectFindViewModel"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ProjectFindViewModel.cs DialogEditor.Tests/ViewModels/ProjectFindViewModelTests.cs
git commit -m "feat(find-in-project): ProjectFindViewModel with search + navigate"
```

---

### Task 4: FindInProjectWindow + strings

**Files:**
- Create: `DialogEditor.Avalonia/Views/FindInProjectWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/FindInProjectWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Views/FindInProjectWindowTests.cs`

**Interfaces:**
- Consumes: `ProjectFindViewModel` (Task 3).
- Produces: `FindInProjectWindow(ProjectFindViewModel vm)` — a non-modal results window; double-click / Enter on a result calls `vm.NavigateTo(row)`.

- [ ] **Step 1: Add resource strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add a new commented section (model the plural keys on an existing `FindReplace_Matches`-style pair):

```xml
    <!-- ─── Find in Project (read-only project-wide search) ───────────── -->
    <sys:String x:Key="FindInProject_Title">Find in Project</sys:String>
    <sys:String x:Key="Menu_FindInProject">Find in Project…</sys:String>
    <sys:String x:Key="ToolTip_FindInProject">Search the text of every conversation your project touches (all its edits, plus the vanilla lines in those conversations). Read-only — nothing is changed. Requires an open project and a game folder.</sys:String>
    <sys:String x:Key="FindInProject_SearchPlaceholder">Search text…</sys:String>
    <sys:String x:Key="FindInProject_SearchButton">Search</sys:String>
    <sys:String x:Key="ToolTip_FindInProject_Search">Search all patched conversations for this text.</sys:String>
    <sys:String x:Key="FindInProject_CaseSensitive">Case sensitive</sys:String>
    <sys:String x:Key="ToolTip_FindInProject_CaseSensitive">Match upper- and lower-case exactly.</sys:String>
    <sys:String x:Key="FindInProject_InLinkChoice">Link / choice text</sys:String>
    <sys:String x:Key="ToolTip_FindInProject_InLinkChoice">Also search player-choice and link display text.</sys:String>
    <sys:String x:Key="FindInProject_InTranslations">Translations (all languages)</sys:String>
    <sys:String x:Key="ToolTip_FindInProject_InTranslations">Also search your translated text in every language. Each match shows its language.</sys:String>
    <sys:String x:Key="FindInProject_InNodeComments">Node comments</sys:String>
    <sys:String x:Key="ToolTip_FindInProject_InNodeComments">Also search your per-node writer comments (editor notes, never game text).</sys:String>
    <sys:String x:Key="FindInProject_ColConversation">Conversation</sys:String>
    <sys:String x:Key="FindInProject_ColNode">Node</sys:String>
    <sys:String x:Key="FindInProject_ColField">Field</sys:String>
    <sys:String x:Key="FindInProject_ColLanguage">Language</sys:String>
    <sys:String x:Key="FindInProject_ColSnippet">Match</sys:String>
    <sys:String x:Key="FindInProject_PrimaryLanguage">Default</sys:String>
    <sys:String x:Key="FindInProject_NoMatches">No matches.</sys:String>
    <sys:String x:Key="FindInProject_Matches_One">{0} match</sys:String>
    <sys:String x:Key="FindInProject_Matches_Other">{0} matches</sys:String>
    <sys:String x:Key="FindField_DefaultText">Default text</sys:String>
    <sys:String x:Key="FindField_FemaleText">Female text</sys:String>
    <sys:String x:Key="FindField_LinkChoice">Link / choice</sys:String>
    <sys:String x:Key="FindField_NodeComment">Node comment</sys:String>
    <sys:String x:Key="Status_FindInProject_NodeGone">That node no longer exists — it may have been edited since the search.</sys:String>
```

- [ ] **Step 2: Write the failing test**

Create `DialogEditor.Tests/Views/FindInProjectWindowTests.cs` (model on `CommitConsentDialogTests`: `using Avalonia.Headless.XUnit`; ctor `Loc.Configure(new StubStringProvider())`; reuse Task 2/3 stub helpers to build a VM):

```csharp
[AvaloniaFact]
public void Window_BindsResults()
{
    var (project, provider) = ProjectWith("c1", 1, "The Watcher");
    var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
    { SearchText = "Watcher" };
    vm.SearchCommand.Execute(null);
    var win = new FindInProjectWindow(vm);
    win.Show();
    var list = win.FindControl<Avalonia.Controls.ItemsControl>("ResultsList")!;
    Assert.Equal(1, list.ItemCount);
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FindInProjectWindow"`
Expected: FAIL — `FindInProjectWindow` doesn't exist.

- [ ] **Step 4: Create the view**

`DialogEditor.Avalonia/Views/FindInProjectWindow.axaml` — model layout/brushes on an existing workhorse window such as `FlowAnalyticsWindow.axaml` (read it first for the token/style conventions). It MUST: set `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`; `Title="{DynamicResource FindInProject_Title}"`; a search `TextBox` (`Watermark="{DynamicResource FindInProject_SearchPlaceholder}"`, `Text="{Binding SearchText}"`); four `CheckBox`es bound to `CaseSensitive`/`InLinkChoice`/`InTranslations`/`InNodeComments`, each with `Content` + `ToolTip.Tip` + `AutomationProperties.HelpText` from the keys above; a Search `Button` (`Command="{Binding SearchCommand}"`, tooltip); a `TextBlock` bound to `StatusText`; and a results list named `ResultsList` (a `DataGrid` or an `ItemsControl` with a header row) showing columns Conversation/Node/Field/Language/Snippet from each `FindMatchRow`. Include a `FocusHintBar` (see how `FlowAnalyticsWindow` hosts one). Bind row activation in code-behind (next step) rather than in XAML.

Use `{DynamicResource}` for every visible string. For the Language cell, show the primary label when the value is empty — bind through a converter OR resolve in a tiny display wrapper; simplest: expose the row's language directly and use a value converter `EmptyToPrimaryLabelConverter` that returns `Loc.Get("FindInProject_PrimaryLanguage")` for `""`. If adding a converter is heavier than warranted, instead have the window map rows into a small display record in code-behind that pre-resolves the language label. Pick one; keep strings localized.

`DialogEditor.Avalonia/Views/FindInProjectWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class FindInProjectWindow : Window
{
    private readonly ProjectFindViewModel? _vm;

    public FindInProjectWindow() => InitializeComponent();

    public FindInProjectWindow(ProjectFindViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
    }

    // Wire double-click / Enter on a result row to navigation. The results list's
    // SelectedItem is a FindMatchRow (or the display wrapper carrying one).
    private void OnResultActivated(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if ((sender as Control)?.DataContext is FindMatchRow row)
            _vm.NavigateTo(row);
    }
}
```

Wire `OnResultActivated` to each row's `DoubleTapped` (and Enter via a `KeyBinding` or key handler) in the XAML/code-behind consistent with how the repo's other list windows do it — check `HistoryWindow`/`DiffWindow` for the row-activation pattern and match it. If a display-wrapper record is used instead of `FindMatchRow` directly, adjust the cast and have the wrapper expose the underlying row.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FindInProjectWindow"`
Expected: PASS.

- [ ] **Step 6: Run the localisation + automation guards**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NoHardcodedUiStrings|FullyQualifiedName~NoStaticStringResource|FullyQualifiedName~AutomationHelpText|FullyQualifiedName~NoNaivePlural"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/FindInProjectWindow.axaml DialogEditor.Avalonia/Views/FindInProjectWindow.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Views/FindInProjectWindowTests.cs
git commit -m "feat(find-in-project): results window + strings"
```

---

### Task 5: MainWindow wiring — gate, command, shortcut, navigation

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (Edit menu item)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (window wiring + Ctrl+Shift+F)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs` (extend — gate + navigation)

**Interfaces:**
- Consumes: `ProjectFindViewModel` (Task 3); existing `Canvas.BuildSnapshot()`, `Canvas.ConversationName`, `Canvas.Nodes` (each `NodeViewModel` has `NodeId` and is assignable to `Canvas.SelectedNode`), `OnConversationSelected`, `_provider.FindConversation`, the fields `_project`/`_provider`/`_currentGameDirectory`.
- Produces on `MainWindowViewModel`: `bool CanFindInProject`, `FindInProjectCommand`, `Func<ProjectFindViewModel, Task>? ShowFindInProject`, and `void NavigateToFoundNode(string conversationName, int nodeId)`.

- [ ] **Step 1: Write the failing tests** (append to `MainWindowViewModelTests`; ctor already isolates AppSettings/Loc there)

```csharp
[Fact]
public void CanFindInProject_RequiresProjectProviderAndGameFolder()
{
    var vm = NewVmWithNothingOpen();          // reuse the file's existing construction helper
    Assert.False(vm.CanFindInProject);
}

[Fact]
public void NavigateToFoundNode_SameConversation_SelectsNode()
{
    var vm = VmWithOpenConversation(convName: "c1", nodeIds: new[] { 1, 2, 3 });  // helper per file conventions
    vm.NavigateToFoundNode("c1", 2);
    Assert.Equal(2, vm.Canvas.SelectedNode?.NodeId);
}
```

If the file has no ready helper to open a conversation with known node ids in a unit test, add a minimal one alongside the existing helpers, or assert the gate test only and cover navigation via the GUI step (Step 6). Do not fabricate a provider that doesn't match the file's existing test setup.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: FAIL — `CanFindInProject`/`NavigateToFoundNode` don't exist.

- [ ] **Step 3: Add the gate, command, delegate, and navigation to `MainWindowViewModel`**

Near `CanBatchImportVoAll` (~line 248):

```csharp
    /// Find in Project needs a project open, a game-data provider, and a game folder
    /// (it loads each patched conversation's effective text). Unlike batch VO it is
    /// game-agnostic and needs no saved project path.
    public bool CanFindInProject =>
        _project is not null
        && _provider is not null
        && !string.IsNullOrEmpty(_currentGameDirectory);
```

Add `FindInProjectCommand.NotifyCanExecuteChanged();` everywhere `BatchImportVoAllCommand.NotifyCanExecuteChanged();` already appears (the same gate-dependency points: `SetProject`, `FinishLoad`, `DoNewProject`, Save-As, close, and the game-folder-load site ~line 1566). Search the file for `BatchImportVoAllCommand.NotifyCanExecuteChanged` and add the sibling line next to each.

Near `ShowBatchVoImportAll`/`BatchImportVoAll` (~line 518):

```csharp
    public Func<ProjectFindViewModel, Task>? ShowFindInProject { get; set; }

    [RelayCommand(CanExecute = nameof(CanFindInProject))]
    private async Task FindInProject()
    {
        if (_project is null || _provider is null) return;
        var vm = new ProjectFindViewModel(
            _project, _provider, _provider.Language,
            () => Canvas.Nodes.Count > 0
                ? (Canvas.ConversationName, Canvas.BuildSnapshot())
                : (null, (ConversationEditSnapshot?)null));
        vm.RequestNavigate += NavigateToFoundNode;
        if (ShowFindInProject is not null)
            await ShowFindInProject(vm);
    }

    /// Navigate from a find result to its node: switch conversation if needed
    /// (reusing the unsaved-changes guard), then select the node by id.
    public void NavigateToFoundNode(string conversationName, int nodeId)
    {
        if (Canvas.ConversationName == conversationName)
        {
            SelectNodeById(nodeId);
            return;
        }
        if (_provider?.FindConversation(conversationName) is not { } file) return;
        _pendingSelectNodeId = nodeId;
        OnConversationSelected(file);   // honours the dirty guard; loads the conversation
    }

    private int? _pendingSelectNodeId;

    private void SelectNodeById(int nodeId)
    {
        var node = Canvas.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node is not null) Canvas.SelectedNode = node;
        else StatusText = Loc.Get("Status_FindInProject_NodeGone");
    }
```

Then, at the END of `LoadConversationFile` (after the canvas load completes, ~line 2214+, i.e. after `Canvas.Load(...)`), drain the pending selection so navigation lands on the node once the conversation is loaded:

```csharp
            if (_pendingSelectNodeId is int pending)
            {
                _pendingSelectNodeId = null;
                SelectNodeById(pending);
            }
```

Add `using DialogEditor.Core.Editing;` if `ConversationEditSnapshot` isn't already imported in the file (it is used by batch VO, so likely present). `LoadNewConversation` (the new-conversation branch of `OnConversationSelected`) has no game nodes to select — clearing `_pendingSelectNodeId` there is unnecessary because a found node always corresponds to a real, loaded conversation; but if a switch is cancelled by the dirty guard, clear `_pendingSelectNodeId` in the cancel path so a later unrelated load doesn't inherit it. (The dirty-guard cancel path is where `_pendingFile` is abandoned — clear `_pendingSelectNodeId` alongside it.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Wire the menu, shortcut, and window in the view**

In `DialogEditor.Avalonia/Views/MainWindow.axaml`, add an Edit-menu item near Batch Replace (find the Batch Replace `MenuItem` in the Edit menu and add after it):

```xml
                        <MenuItem Header="{DynamicResource Menu_FindInProject}"
                                  Command="{Binding FindInProjectCommand}"
                                  InputGesture="Ctrl+Shift+F"
                                  ToolTip.Tip="{DynamicResource ToolTip_FindInProject}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_FindInProject}"/>
```

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`:
- Wire the show delegate where the other `vm.Show*` delegates are set (near `vm.ShowBatchVoImportAll = ...`):

```csharp
        vm.ShowFindInProject = async findVm =>
        {
            var win = new FindInProjectWindow(findVm);
            win.Show(this);          // non-modal, owned
            await Task.CompletedTask;
        };
```

- Add the Ctrl+Shift+F case to `OnKeyDownTunnel` alongside the existing shortcut handlers (match the file's existing pattern for a Ctrl+Shift chord; it must invoke `vm.FindInProjectCommand` when it `CanExecute`). Find how Ctrl+Shift+S (Save As) or Ctrl+Shift+O is dispatched there and mirror it.

- [ ] **Step 6: Build, full suite, GUI verify**

Run: `dotnet build DialogEditor.Avalonia --nologo -v q` → Build succeeded, 0 errors. (Ignore stale LSP CS0103/CS0246 for XAML-generated members — trust `dotnet build`.)
Run: `dotnet test --nologo -v q` → all pass.
Then GUI-verify with the running-the-app skill: open a project with a game folder, Edit ▸ Find in Project… (and Ctrl+Shift+F), search a term known to appear, confirm results list populates with the right columns, toggle each checkbox and re-search, double-click a result and confirm the canvas switches conversation and selects the node. Capture a screenshot of the populated window.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(find-in-project): Edit menu, Ctrl+Shift+F, cross-conversation navigation"
```

---

## Self-Review

**Spec coverage:**
- Effective-text walk over patched conversations, live snapshot for open, skip-unreadable → Task 2. ✓
- Coverage: Default+Female always; link/choice, translations (labeled, non-primary), node comments (from `patch.NodeComments`) as toggles → Task 2 + Task 3/4 checkboxes. ✓
- One locator row per matching field; snippet with context/flattened newlines/ellipsis → Tasks 1-2. ✓
- Primary-language fallback for `[JsonIgnore]` added-node text → Task 2. ✓
- Read-only, no save-before-scan; live snapshot via accessor → Tasks 3, 5. ✓
- Non-modal window, checkboxes, results list, double-click/Enter navigation, FocusHintBar, icon, tooltips → Task 4. ✓
- Edit ▸ Find in Project…, Ctrl+Shift+F, gate (project+provider+game folder) → Task 5. ✓
- Cross-conversation navigation reusing the dirty-guard switch, vanished-node status message → Task 5. ✓
- Testing per the spec's list → Tasks 2-5. ✓
- Out of scope (regex/whole-word, per-occurrence rows, vanilla-only conversations, non-primary effective text, replace) → not planned. ✓

**Placeholder scan:** Task 4's view and Task 5's key-handler/nav-helper steps intentionally point the implementer at named existing patterns (`FlowAnalyticsWindow`, `HistoryWindow` row activation, the Ctrl+Shift+S handler, the batch-VO gate points) rather than reproducing large unrelated files verbatim; the new code those steps add is given in full. All other code steps carry complete code.

**Type consistency:** `ProjectFindQuery`/`FindMatchRow`/`FindSnippet.Extract` (Task 1) are used with identical signatures in Tasks 2-4; `ProjectFindService.Search` (Task 2) is called identically in Task 3; `ProjectFindViewModel` ctor + `RequestNavigate`/`NavigateTo` (Task 3) match their use in Tasks 4-5; `NavigateToFoundNode(string,int)` matches the `RequestNavigate` event signature `Action<string,int>`. Field-label keys (`FindField_*`) produced in Task 2 are defined in Strings.axaml in Task 4.
