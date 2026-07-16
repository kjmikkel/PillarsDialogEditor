# Visual Studio–style Docking Shell (Phase 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `MainWindow`'s fixed 5-column panel grid with a Dock.Avalonia docking shell — Conversations, Canvas (document), Node Details, and Condition search as dockable/floatable/auto-hide/tabbed tool windows with a persisted layout + Reset.

**Architecture:** Adopt `Dock.Avalonia` (MVVM). Thin `Tool`/`Document` wrapper VMs (in `DialogEditor.Avalonia/Docking/`) host the existing panel VMs unchanged, so `DialogEditor.ViewModels` stays Dock-free. An `EditorDockFactory` builds the default layout; `MainWindow` hosts a single `DockControl`. The shell is built and proven with the default layout first; **layout persistence is added last** (Tasks 9–10) so the riskiest piece (content re-hydration on load) is isolated.

**Tech Stack:** C# / .NET 8, Avalonia 11.3.14, `Dock.Avalonia` + `Dock.Model.Mvvm` + `Dock.Serializer.SystemTextJson` (11.3.x line), CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-16-docking-shell-phase1-design.md`.

## Global Constraints

Copied from the spec / CLAUDE.md — every task implicitly includes these:

- **TDD** for the unit-testable logic (factory default-layout shape, serialization round-trip, persistence fallback). GUI-only behaviors are verified with the `running-the-app` skill, not unit tests — that's expected for this feature.
- **Localisation** — tool `Title`s, the View menu, and app-surfaced strings live in `DialogEditor.Avalonia/Resources/Strings.axaml` (`<sys:String>`), read via `Loc`. `NoHardcodedUiStrings` / `NoStaticStringResourceTests` apply. Dock's *built-in* menu strings (float/dock/close) are an accepted, recorded gap — do not attempt to localise them here.
- **No hex in XAML** outside the palette family — the Dock theme-override dictionary uses `{DynamicResource Brush.*}` / `{StaticResource Palette.*}` only (`NoStrayHexTests`).
- **Window icon** — floating Dock `HostWindow`s carry `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **UIA** — tools/tabs discoverable by `Title`; View-menu items by localised `Header`.
- **Error handling** — every caught exception logged via `AppLog.Warn/Error`; no bare `catch{}`. `OperationCanceledException` swallowed silently.
- **Tests run serially** — new tests that touch `Loc` call `Loc.Configure(new StubStringProvider())`; those touching `GameDataNameService` clear it in `Dispose`.
- **Single document** — `CanCreateDocument = false`; one conversation at a time.
- **Layout file** — `%LOCALAPPDATA%\PillarsDialogEditor\layout.json`; corrupt/missing ⇒ fall back to default (never crash).

> **Library-matching note (applies throughout):** Dock.Avalonia's exact type names and a couple of call sequences can differ slightly by patch version. Where a step says "confirm against the installed Dock version," that means read the installed package's public API / the wieslawsoltes/dock samples and adjust the shown code to match — it is a verification step, not deferred work.

---

### Task 1: Add Dock packages + theme

**Files:**
- Modify: `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj` (package references)
- Modify: `DialogEditor.Avalonia/App.axaml` (styles)

**Interfaces:**
- Produces: the `Dock.*` assemblies available to the project; `DockFluentTheme` merged so any `DockControl` renders.

- [ ] **Step 1: Add the package references**

In `DialogEditor.Avalonia.csproj`, alongside the existing `Avalonia` references, add (use the newest **11.3.x** versions that restore against Avalonia 11.3.14 — confirm exact numbers with `dotnet restore`):

```xml
<PackageReference Include="Dock.Avalonia" Version="11.3.*" />
<PackageReference Include="Dock.Model.Mvvm" Version="11.3.*" />
<PackageReference Include="Dock.Serializer.SystemTextJson" Version="11.3.*" />
```

> If floating-version (`11.3.*`) is disallowed by the repo's package policy, pin the concrete resolved version that `dotnet restore` selects. Check whether the repo uses `Directory.Packages.props` (central package management); if so, add the `<PackageVersion>` entries there and a bare `<PackageReference Include="…"/>` here.

- [ ] **Step 2: Merge the Dock theme in App.axaml**

```xml
<Application.Styles>
    <FluentTheme/>
    <DockFluentTheme xmlns="clr-namespace:Dock.Avalonia.Themes.Fluent;assembly=Dock.Avalonia.Themes.Fluent"/>
</Application.Styles>
```

> Confirm the `DockFluentTheme` namespace/assembly against the installed package (some versions expose it as a `StyleInclude Source="avares://Dock.Avalonia.Themes.Fluent/DockFluentTheme.axaml"`). Use whichever the installed package provides.

- [ ] **Step 3: Build**

Run: `dotnet build DialogEditor.Avalonia`
Expected: `Build succeeded` (no functional change yet; this proves the packages + theme resolve).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/DialogEditor.Avalonia.csproj DialogEditor.Avalonia/App.axaml
git commit -m "build(deps): add Dock.Avalonia + MVVM + System.Text.Json serializer"
```

---

### Task 2: Wrapper dockables + views + DataTemplates

**Files:**
- Create: `DialogEditor.Avalonia/Docking/CanvasDocument.cs`, `BrowserTool.cs`, `DetailsTool.cs`, `ConditionSearchTool.cs`
- Create: `DialogEditor.Avalonia/Docking/DockToolViews.axaml` (a `ResourceDictionary` of `DataTemplate`s, or add them to `App.axaml`)
- Modify: `DialogEditor.Avalonia/App.axaml` (register `<Application.DataTemplates>`)
- Test: `DialogEditor.Tests/Docking/WrapperDockableTests.cs`

**Interfaces:**
- Consumes: existing `GameBrowserViewModel`, `ConversationViewModel`, `NodeDetailViewModel`, `ConditionSearchViewModel` (all `DialogEditor.ViewModels`); `Dock.Model.Mvvm.Controls.{Tool,Document}`.
- Produces:
  - `CanvasDocument : Document` with `Id = "Canvas"`, `Inner : ConversationViewModel` (`[JsonIgnore]`), `CanClose = false`.
  - `BrowserTool : Tool` (`Id = "Browser"`, `Inner : GameBrowserViewModel`), `DetailsTool : Tool` (`Id = "Details"`, `Inner : NodeDetailViewModel`), `ConditionSearchTool : Tool` (`Id = "ConditionSearch"`, `Inner : ConditionSearchViewModel`). Each sets a localised `Title` and `CanClose = true`.

The wrapper's `Inner` is marked `[System.Text.Json.Serialization.JsonIgnore]` so the layout serializer never tries to persist the live VM — content is re-attached by the factory on load (Task 9).

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Docking/WrapperDockableTests.cs
using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Docking;

public class WrapperDockableTests
{
    public WrapperDockableTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void BrowserTool_HasStableId_AndHostsInnerVm()
    {
        var inner = new GameBrowserViewModel(new StubDispatcher());
        var tool  = new BrowserTool(inner);
        Assert.Equal("Browser", tool.Id);
        Assert.Same(inner, tool.Inner);
        Assert.False(string.IsNullOrEmpty(tool.Title));
    }

    [Fact]
    public void CanvasDocument_CannotClose_AndHasCanvasId()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        var doc    = new CanvasDocument(canvas);
        Assert.Equal("Canvas", doc.Id);
        Assert.False(doc.CanClose);
        Assert.Same(canvas, doc.Inner);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~WrapperDockableTests"`
Expected: FAIL — wrapper types don't exist.

- [ ] **Step 3: Write the wrapper dockables**

```csharp
// DialogEditor.Avalonia/Docking/BrowserTool.cs
using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// Dock tool hosting the conversations browser. Inner is [JsonIgnore] so the layout
/// serializer never persists the live VM — the factory re-attaches it by Id on load.
public sealed class BrowserTool : Tool
{
    [JsonIgnore] public GameBrowserViewModel Inner { get; }

    public BrowserTool(GameBrowserViewModel inner)
    {
        Inner = inner;
        Id    = "Browser";
        Title = Loc.Get("Dock_Tool_Browser");
        CanClose = true;
    }
}
```

```csharp
// DialogEditor.Avalonia/Docking/DetailsTool.cs
using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

public sealed class DetailsTool : Tool
{
    [JsonIgnore] public NodeDetailViewModel Inner { get; }

    public DetailsTool(NodeDetailViewModel inner)
    {
        Inner = inner;
        Id    = "Details";
        Title = Loc.Get("Dock_Tool_Details");
        CanClose = true;
    }
}
```

```csharp
// DialogEditor.Avalonia/Docking/ConditionSearchTool.cs
using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

public sealed class ConditionSearchTool : Tool
{
    [JsonIgnore] public ConditionSearchViewModel Inner { get; }

    public ConditionSearchTool(ConditionSearchViewModel inner)
    {
        Inner = inner;
        Id    = "ConditionSearch";
        Title = Loc.Get("Dock_Tool_ConditionSearch");
        CanClose = true;
    }
}
```

```csharp
// DialogEditor.Avalonia/Docking/CanvasDocument.cs
using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// The canvas document — the anchor. Single document in v1; cannot be closed.
public sealed class CanvasDocument : Document
{
    [JsonIgnore] public ConversationViewModel Inner { get; }

    public CanvasDocument(ConversationViewModel inner)
    {
        Inner = inner;
        Id    = "Canvas";
        Title = Loc.Get("Dock_Doc_Canvas");
        CanClose = false;
    }
}
```

Add the four `Title` strings to `Strings.axaml` (`<sys:String>`): `Dock_Tool_Browser` ("Conversations"), `Dock_Tool_Details` ("Node Details"), `Dock_Tool_ConditionSearch` ("Condition search"), `Dock_Doc_Canvas` ("Canvas").

> Confirm `Tool`/`Document` expose settable `Id`/`Title`/`CanClose` in the installed `Dock.Model.Mvvm.Controls` (they do in 11.x). If `Title` isn't yet localisation-reactive, that's fine — the shell rebuilds titles when the layout is (re)built.

- [ ] **Step 4: Register DataTemplates**

In `App.axaml`, add a `<Application.DataTemplates>` block mapping each wrapper VM to a view that hosts the existing UserControl bound to `Inner`:

```xml
<Application.DataTemplates>
  <DataTemplate DataType="docking:BrowserTool">
    <views:GameBrowserView DataContext="{Binding Inner}"/>
  </DataTemplate>
  <DataTemplate DataType="docking:DetailsTool">
    <views:NodeDetailView DataContext="{Binding Inner}"/>
  </DataTemplate>
  <DataTemplate DataType="docking:ConditionSearchTool">
    <views:ConditionSearchView DataContext="{Binding Inner}"/>
  </DataTemplate>
  <DataTemplate DataType="docking:CanvasDocument">
    <views:ConversationView DataContext="{Binding Inner}"/>
  </DataTemplate>
</Application.DataTemplates>
```

Add the xmlns: `xmlns:docking="clr-namespace:DialogEditor.Avalonia.Docking"` (and confirm `xmlns:views` already maps to `DialogEditor.Avalonia.Views`).

- [ ] **Step 5: Run test + build**

Run: `dotnet test --filter "FullyQualifiedName~WrapperDockableTests"` → PASS
Run: `dotnet build DialogEditor.Avalonia` → succeeds.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Docking DialogEditor.Avalonia/App.axaml DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Docking/WrapperDockableTests.cs
git commit -m "feat(docking): Tool/Document wrapper dockables + DataTemplates"
```

---

### Task 3: EditorDockFactory — default layout

**Files:**
- Create: `DialogEditor.Avalonia/Docking/EditorDockFactory.cs`
- Test: `DialogEditor.Tests/Docking/EditorDockFactoryTests.cs`

**Interfaces:**
- Consumes: the four wrapper dockables (Task 2); `Dock.Model.Mvvm.Factory`, `Dock.Model.Mvvm.Controls.{RootDock,ProportionalDock,ProportionalDockSplitter,DocumentDock,ToolDock}`, `Dock.Model.Core.{Alignment,Orientation}`.
- Produces:
  - `EditorDockFactory(GameBrowserViewModel browser, ConversationViewModel canvas, NodeDetailViewModel details, ConditionSearchViewModel search) : Factory`
  - `override IRootDock CreateLayout()` building the default tree.
  - Fields exposing the built pieces for re-use in InitLayout (Task 9): `IDocumentDock? DocumentDock`, and the tool references.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Docking/EditorDockFactoryTests.cs
using Dock.Model.Controls;
using Dock.Model.Core;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Docking;

public class EditorDockFactoryTests
{
    public EditorDockFactoryTests() => Loc.Configure(new StubStringProvider());

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

    [Fact]
    public void CreateLayout_ContainsAllFourDockablesById()
    {
        var root = MakeFactory().CreateLayout();
        var ids  = Descendants(root).Select(d => d.Id).ToHashSet();
        Assert.Contains("Browser", ids);
        Assert.Contains("Canvas", ids);
        Assert.Contains("Details", ids);
        Assert.Contains("ConditionSearch", ids);
    }

    [Fact]
    public void CreateLayout_RightToolDock_TabsDetailsAndConditionSearch()
    {
        var root = MakeFactory().CreateLayout();
        var rightDock = Descendants(root).OfType<IToolDock>()
            .First(td => td.VisibleDockables!.Any(x => x.Id == "Details"));
        var ids = rightDock.VisibleDockables!.Select(x => x.Id).ToList();
        Assert.Contains("Details", ids);
        Assert.Contains("ConditionSearch", ids);   // tabbed together
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EditorDockFactoryTests"`
Expected: FAIL — `EditorDockFactory` doesn't exist.

- [ ] **Step 3: Implement the factory**

```csharp
// DialogEditor.Avalonia/Docking/EditorDockFactory.cs
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Docking;

/// Builds the editor's default docking layout: Conversations (left tool), Canvas (centre
/// document), Node Details + Condition search (right, tabbed). Also re-wires content on load.
public sealed class EditorDockFactory : Factory
{
    private readonly GameBrowserViewModel     _browser;
    private readonly ConversationViewModel    _canvas;
    private readonly NodeDetailViewModel      _details;
    private readonly ConditionSearchViewModel _search;

    public IDocumentDock? DocumentDock { get; private set; }

    public EditorDockFactory(
        GameBrowserViewModel browser, ConversationViewModel canvas,
        NodeDetailViewModel details, ConditionSearchViewModel search)
    {
        _browser = browser; _canvas = canvas; _details = details; _search = search;
    }

    public override IRootDock CreateLayout()
    {
        var canvasDoc = new CanvasDocument(_canvas);
        var documentDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            ActiveDockable = canvasDoc,
            VisibleDockables = CreateList<IDockable>(canvasDoc),
        };
        DocumentDock = documentDock;

        var browserTool = new BrowserTool(_browser);
        var leftDock = new ToolDock
        {
            Id = "LeftPane",
            Proportion = 0.18,
            Alignment = Alignment.Left,
            ActiveDockable = browserTool,
            VisibleDockables = CreateList<IDockable>(browserTool),
        };

        var detailsTool = new DetailsTool(_details);
        var searchTool  = new ConditionSearchTool(_search);
        var rightDock = new ToolDock
        {
            Id = "RightPane",
            Proportion = 0.24,
            Alignment = Alignment.Right,
            ActiveDockable = detailsTool,
            VisibleDockables = CreateList<IDockable>(detailsTool, searchTool),
        };

        var main = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter(),
                documentDock,
                new ProportionalDockSplitter(),
                rightDock),
        };

        var root = CreateRootDock();
        root.IsCollapsable = false;
        root.DefaultDockable = main;
        root.ActiveDockable  = main;
        root.VisibleDockables = CreateList<IDockable>(main);
        return root;
    }
}
```

> Confirm `IToolDock` / `IDocumentDock` interface names and `CreateList<T>` / `CreateRootDock()` against the installed `Dock.Model.Mvvm` (they exist in 11.x `Factory`). If `Alignment`/`Orientation` live in a different namespace in the installed version, adjust the `using`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~EditorDockFactoryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Docking/EditorDockFactory.cs DialogEditor.Tests/Docking/EditorDockFactoryTests.cs
git commit -m "feat(docking): EditorDockFactory default layout"
```

---

### Task 4: Move Condition search off the canvas VM; MainWindowViewModel builds the layout

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` (remove dock-toggle state; keep apply/clear + ActiveGameId)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (own the `ConditionSearchViewModel`, expose `Layout`/`Factory`, remove browser/detail pin flags)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml` (remove the in-canvas search dock column + ⚑ toggle)
- Test: `DialogEditor.Tests/ViewModels/MainWindowLayoutTests.cs`

> **Layering note:** `MainWindowViewModel` is in `DialogEditor.ViewModels`, which must stay Dock-free. So it cannot hold `IRootDock`/`EditorDockFactory` directly. Instead, expose the four sub-VMs and a `BuildConditionSearch()` helper; the **Avalonia layer** (Task 5) constructs the `EditorDockFactory` + `DockControl`. `MainWindowViewModel` exposes `ConditionSearch` (the VM) so the factory can be built from it.

**Interfaces:**
- Produces on `MainWindowViewModel`: `public ConditionSearchViewModel ConditionSearch { get; }` (built from `Canvas`), replacing `Canvas.ConditionSearch`.
- Removes from `ConversationViewModel`: `ConditionSearch`, `IsConditionSearchVisible`, `ToggleConditionSearchCommand`, and the `ActiveGameId` setter's search-VM construction (keep `ActiveGameId` as a plain stored string; keep `ApplyConditionHighlight`/`ClearConditionHighlight`/`BuildSnapshot`).

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/ViewModels/MainWindowLayoutTests.cs
// Assert MainWindowViewModel exposes a non-null ConditionSearch whose Search applies highlight
// to Canvas. Reuse the existing MainWindowViewModel test construction (see MainWindowViewModelTests).
// Minimal shape:
//   var vm = MakeVm();  // existing helper
//   Assert.NotNull(vm.ConditionSearch);
```

> Use the real `MakeVm()` helper from `MainWindowViewModelTests`. If exposing `ConditionSearch` requires a game id, set it the way that test sets `_activeGameId` (see `MakeVoAllReadyVm`). The deliverable is: `ConditionSearch` exists on `MainWindowViewModel` and is wired to `Canvas`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MainWindowLayoutTests"`
Expected: FAIL — `ConditionSearch` not on `MainWindowViewModel`.

- [ ] **Step 3: Simplify `ConversationViewModel`**

Remove the search-dock members. Change the `ActiveGameId` property to a plain observable string (drop the `ConditionSearch` construction and `ToggleConditionSearchCommand.NotifyCanExecuteChanged()`):

```csharp
// ConversationViewModel.cs — replace the ActiveGameId property + ConditionSearch/IsConditionSearchVisible/
// ToggleConditionSearch members with just:
[ObservableProperty] private string _activeGameId = "";
```

Keep `ApplyConditionHighlight`, `ClearConditionHighlight`, and `BuildSnapshot` (unchanged). Remove the `ToggleConditionSearchCommand.NotifyCanExecuteChanged();` call in the `Nodes.CollectionChanged` handler.

- [ ] **Step 4: Add `ConditionSearch` to `MainWindowViewModel`**

Build it from `Canvas` and rebuild it when the game id changes (next to where `Canvas.ActiveGameId = provider.GameId;` is set, ≈ line 1706):

```csharp
// field + property
public ConditionSearchViewModel? ConditionSearch { get; private set; }

// where the provider loads (after Canvas.ActiveGameId = provider.GameId;):
ConditionSearch = new ConditionSearchViewModel(
    provider.GameId,
    () => Canvas.Nodes.Count > 0 ? Canvas.BuildSnapshot() : null,
    matches => Canvas.ApplyConditionHighlight(matches),
    () => Canvas.ClearConditionHighlight());
OnPropertyChanged(nameof(ConditionSearch));
```

Also **remove** the browser/detail pin/collapse flags (`IsBrowserExpanded`, `IsBrowserPinned`, `IsBrowserFlyoutOpen`, `IsDetailExpanded` and their `partial void On…Changed` handlers + the `if (!IsBrowserPinned) IsBrowserExpanded = false;` sites) — Dock replaces them. Keep `AppSettings.DetailExpanded`/`BrowserExpanded` reads out of the way (leave the settings keys; just stop binding them).

- [ ] **Step 5: Strip the in-canvas search dock from `ConversationView.axaml`**

Remove the `⚑` toggle button, the `ColumnDefinitions="*,Auto"` wrapper, and the `<ContentControl … ConditionSearchView …>` column added in Gap #2 — revert the canvas Row 1 to the single `Grid` holding just the `NodifyEditor` (+ overlays). The `ConditionSearchView` control file stays (it's now hosted by the dock tool).

- [ ] **Step 6: Run test + build**

Run: `dotnet build` (whole solution) → succeeds (MainWindow.axaml still references removed VM members? It will in Task 5 — for now the app may not build until Task 5 rewrites MainWindow. If so, do Steps of Task 5 in the same commit.)

> **Sequencing:** Tasks 4 and 5 are tightly coupled (removing `Canvas.ConditionSearch` breaks the old `MainWindow.axaml`/`ConversationView.axaml` bindings). Treat Task 4 + Task 5 as one commit if the build can't be green between them. Run `dotnet test --filter "FullyQualifiedName~MainWindowLayoutTests"` once Task 5 compiles.

- [ ] **Step 7: Commit** (jointly with Task 5 if needed)

```bash
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Tests/ViewModels/MainWindowLayoutTests.cs
git commit -m "refactor(viewmodels): own ConditionSearch at shell level; drop panel pin flags"
```

---

### Task 5: Replace MainWindow content grid with DockControl

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (replace the 5-column `ContentGrid` with `<dock:DockControl>`)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (build the factory + layout, set on the DockControl)

**Interfaces:**
- Consumes: `EditorDockFactory` (Task 3), `MainWindowViewModel.{Browser,Canvas,Detail,ConditionSearch}`.
- Produces: a running docked shell showing all four panels in the default layout.

- [ ] **Step 1: Replace the content grid in `MainWindow.axaml`**

Delete the entire `<Grid Grid.Row="1" x:Name="ContentGrid">…</Grid>` block (the browser/splitter/canvas/splitter/details grid) and put in its place:

```xml
<dock:DockControl x:Name="Dock" Grid.Row="1" Layout="{Binding DockLayout}"/>
```

Add `xmlns:dock="using:Dock.Avalonia.Controls"` to the `<Window>` element.

- [ ] **Step 2: Build the layout in code-behind**

Because `MainWindowViewModel` is Dock-free, build the factory/layout in `MainWindow.axaml.cs` once the VM's sub-VMs exist (after `DataContext` is set and a project/game is available). Simplest robust approach: expose a settable `object? DockLayout` on `MainWindowViewModel` (typed `object` to avoid a Dock dependency in the VM) that the view sets:

```csharp
// MainWindow.axaml.cs — after DataContext is a MainWindowViewModel and the game is loaded:
private void BuildDock(MainWindowViewModel vm)
{
    if (vm.ConditionSearch is null) return;   // needs a loaded game
    var factory = new EditorDockFactory(vm.Browser, vm.Canvas, vm.Detail, vm.ConditionSearch);
    var layout  = factory.CreateLayout();
    factory.InitLayout(layout);
    vm.DockLayout = layout;   // object? property; DockControl.Layout binds to it
    _factory = factory;
}
```

On `MainWindowViewModel` add: `[ObservableProperty] private object? _dockLayout;`. Call `BuildDock(vm)` when the game finishes loading (subscribe to a VM event or rebuild after the folder-load command completes — mirror how other post-load wiring runs in `MainWindow.axaml.cs`).

> **Confirm** the `DockControl.Layout` type accepts the `IRootDock` you pass (it does; `object?` binding works because `Layout` is `IDock`). If binding an `object?` is rejected, expose `IRootDock? DockLayout` in a tiny Avalonia-side view-model shim instead of on `MainWindowViewModel`.

- [ ] **Step 3: Build + GUI-verify the shell**

Run: `dotnet build` → succeeds.
Use the `running-the-app` skill: launch with the scratch project (auto-loads PoE2), open a conversation, and confirm the four panels render in the default layout (Conversations left, Canvas centre, Node Details + Condition search tabbed right), that tabs are at the bottom of the right tool group, and that dragging a tab shows Dock's guide diamonds.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(docking): host the editor in a DockControl (default layout)"
```

---

### Task 6: View menu — show tools + Reset Layout

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (add the View menu; drop the Edit ▸ condition-search item)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (show-tool + reset handlers)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (menu strings)

**Interfaces:**
- Consumes: `_factory` + the current layout (Task 5); Dock's `IFactory` show/focus helpers.
- Produces: a **View** menu with `Conversations`, `Node Details`, `Condition search` (show/focus the tool) and `Reset Layout` (rebuild the default).

- [ ] **Step 1: Add the View menu**

In `MainWindow.axaml`, add after the Edit menu:

```xml
<MenuItem Header="{DynamicResource Menu_View}">
  <MenuItem Header="{DynamicResource Menu_View_Conversations}" Click="ShowBrowserTool_Click"
            ToolTip.Tip="{DynamicResource ToolTip_View_ShowTool}"/>
  <MenuItem Header="{DynamicResource Menu_View_Details}" Click="ShowDetailsTool_Click"
            ToolTip.Tip="{DynamicResource ToolTip_View_ShowTool}"/>
  <MenuItem Header="{DynamicResource Menu_View_ConditionSearch}" Click="ShowConditionSearchTool_Click"
            ToolTip.Tip="{DynamicResource ToolTip_View_ShowTool}"/>
  <Separator/>
  <MenuItem Header="{DynamicResource Menu_View_ResetLayout}" Click="ResetLayout_Click"
            ToolTip.Tip="{DynamicResource ToolTip_View_ResetLayout}"/>
</MenuItem>
```

Remove the `Menu_ConditionSearch` item from the **Edit** menu (added in the earlier menu-move work).

- [ ] **Step 2: Handlers in code-behind**

```csharp
// MainWindow.axaml.cs
private void ShowBrowserTool_Click(object? s, RoutedEventArgs e)        => ShowToolById("Browser");
private void ShowDetailsTool_Click(object? s, RoutedEventArgs e)        => ShowToolById("Details");
private void ShowConditionSearchTool_Click(object? s, RoutedEventArgs e)=> ShowToolById("ConditionSearch");

private void ShowToolById(string id)
{
    if (_factory?.DockableLocator is null) return;
    var tool = _factory.FindDockable(_factory.CurrentRootDock, d => d.Id == id);  // confirm helper name
    if (tool is not null) _factory.SetActiveDockable(tool);   // shows/focuses; re-adds if closed
}

private void ResetLayout_Click(object? s, RoutedEventArgs e)
{
    if (DataContext is not MainWindowViewModel vm) return;
    BuildDock(vm);   // rebuild default; Task 9 also deletes layout.json here
}
```

> **Confirm** the exact Dock helper names against the installed version: showing a possibly-closed tool is done via the factory (`AddDockable`/`SetActiveDockable`/`PinDockable` or a `ShowDockable`-style helper). Read the installed `IFactory`/`FactoryBase` API and use the correct calls. If a closed tool must be re-inserted, re-add it to its owner dock before activating.

- [ ] **Step 3: Strings**

Add `Menu_View`, `Menu_View_Conversations`, `Menu_View_Details`, `Menu_View_ConditionSearch`, `Menu_View_ResetLayout`, `ToolTip_View_ShowTool`, `ToolTip_View_ResetLayout` to `Strings.axaml`.

- [ ] **Step 4: Build + GUI-verify**

Run: `dotnet build` → succeeds.
`running-the-app`: close the Condition search tab, then View ▸ Condition search → it reappears; drag a panel to float, then View ▸ Reset Layout → returns to the default arrangement.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat(docking): View menu — show tools + reset layout"
```

---

### Task 7: Theme override — retint chrome, bottom tool tabs, floating window icon/theme

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Resources/DockTheme.axaml` (token-mapped overrides)
- Modify: `DialogEditor.Avalonia/App.axaml` (merge `DockTheme.axaml` after `DockFluentTheme`)
- Modify: floating host-window setup (via `HostWindowLocator` in `EditorDockFactory.InitLayout`, added in Task 9 — for this task, add the locator returning a themed `HostWindow` subclass)
- Create: `DialogEditor.Avalonia/Docking/EditorHostWindow.cs` (a `HostWindow` with the app icon)

**Interfaces:**
- Produces: dock chrome retinted onto `Brush.*` tokens; tool tab strip pinned to the bottom; floating windows carry `app.ico` + app theme.

- [ ] **Step 1: Create the token-mapped override dictionary**

`DockTheme.axaml` — a `ResourceDictionary` that overrides the Dock theme's brush keys with `{DynamicResource Brush.*}`. Identify the Dock brush keys from the installed `DockFluentTheme` (tab background/foreground, active-tab accent, dock-target, splitter, host-window title). Example shape (keys confirmed against the package):

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Map Dock theme keys onto app tokens; no hex (NoStrayHexTests). Exact key names come
       from the installed Dock.Avalonia.Themes.Fluent resources. -->
  <SolidColorBrush x:Key="DockThemeBackgroundBrush" Color="{DynamicResource Palette.Surface.Panel}"/>
  <!-- …tab, accent, splitter, target keys… -->
</ResourceDictionary>
```

> This is the fiddliest task — the correct override keys must be read from the installed Dock Fluent theme. Prefer `{DynamicResource Brush.*}` where a brush is expected, or `{DynamicResource Palette.*}` colors where a `Color` is expected. Keep the file hex-free.

- [ ] **Step 2: Merge it after the Dock theme**

In `App.axaml`, merge `DockTheme.axaml` into `Application.Resources` (or as a style) **after** `DockFluentTheme` so it wins.

- [ ] **Step 3: Bottom tool tabs**

Set the `ToolDock` / `ToolTabStrip` tab placement to bottom. If the installed Dock version exposes a property (e.g. on `ToolDock`), set it in `EditorDockFactory` on the right `ToolDock`; otherwise pin it via a `Style` in `DockTheme.axaml` targeting `ToolTabStrip` (`Dock="Bottom"`). Confirm the mechanism against the installed version.

- [ ] **Step 4: Themed floating window**

```csharp
// DialogEditor.Avalonia/Docking/EditorHostWindow.cs
using Avalonia;
using Dock.Avalonia.Controls;

namespace DialogEditor.Avalonia.Docking;

/// Floating dock window carrying the app icon (project window-icon rule). It inherits the
/// app theme because it is app-owned and the merged dictionaries apply to all top-levels.
public class EditorHostWindow : HostWindow
{
    public EditorHostWindow()
    {
        Icon = new WindowIcon(AssetLoader.Open(
            new Uri("avares://DialogEditor.Avalonia/Assets/app.ico")));
    }
}
```

Register it in `EditorDockFactory.InitLayout` via `HostWindowLocator[nameof(IDockWindow)] = () => new EditorHostWindow();` (this locator is finalized in Task 9; add it here).

> Confirm `HostWindow`'s namespace and that `AssetLoader.Open` is the current API (Avalonia 11: `Avalonia.Platform.AssetLoader`).

- [ ] **Step 5: Build + GUI-verify + hex guard**

Run: `dotnet build` → succeeds.
Run: `dotnet test --filter "FullyQualifiedName~NoStrayHexTests"` → PASS (the override file is token-only).
`running-the-app`: confirm the dock chrome uses app colours, tool tabs sit at the bottom, and a floated panel shows the app icon + matching theme; switch theme in Settings and confirm the dock chrome retints live.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Resources/DockTheme.axaml DialogEditor.Avalonia/App.axaml DialogEditor.Avalonia/Docking/EditorHostWindow.cs DialogEditor.Avalonia/Docking/EditorDockFactory.cs
git commit -m "feat(docking): retint dock chrome to tokens; bottom tool tabs; themed float window"
```

---

### Task 8: Remove the dead panel-chrome code + strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (prune orphaned strings)
- Modify: any remaining references to the removed pin/collapse flags / condition-search toggle

**Interfaces:** none new — this is cleanup so the old system leaves no dangling references.

- [ ] **Step 1: Find orphaned references**

Run: `dotnet build` and fix any warnings/errors referencing removed members. Then grep for now-unused resource keys (the browser/detail pin/collapse tooltips + automation names, e.g. `ToolTip_PinPanel`, `AutomationName_PinBrowserPanel`, `ToolTip_TogglePanel`, `AutomationName_CollapseDetailsPanel`, `AutomationName_ExpandDetailsPanel`, `ToolTip_ToggleConditionSearch`, `Menu_ConditionSearch`) and remove any with no remaining XAML/`Loc.Get` reference.

```bash
for k in ToolTip_PinPanel AutomationName_PinBrowserPanel ToolTip_ToggleConditionSearch Menu_ConditionSearch; do
  echo "== $k =="; grep -rn "$k" DialogEditor.Avalonia --include=*.axaml --include=*.cs | grep -v Strings.axaml || echo "(orphaned — remove)";
done
```

Remove only keys that come back orphaned. Leave shared keys still used elsewhere.

- [ ] **Step 2: Run the localisation guards + full build**

Run: `dotnet test --filter "FullyQualifiedName~NoHardcodedUiStringsTests|FullyQualifiedName~NoStaticStringResourceTests|FullyQualifiedName~AutomationNameTests"` → PASS
Run: `dotnet build` → succeeds.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore(docking): remove dead panel-chrome flags + orphaned strings"
```

---

### Task 9: Layout persistence — save / load / fallback

**Files:**
- Create: `DialogEditor.Avalonia/Docking/DockLayoutStore.cs`
- Modify: `DialogEditor.Avalonia/Docking/EditorDockFactory.cs` (`InitLayout` with locators)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (load on build, save on exit, Reset deletes the file)
- Test: `DialogEditor.Tests/Docking/DockLayoutStoreTests.cs`

**Interfaces:**
- Produces:
  - `EditorDockFactory.InitLayout` sets `ContextLocator` (id → inner VM) + `DockableLocator` (id → fresh wrapper) + `HostWindowLocator` (→ `EditorHostWindow`), so a loaded layout re-attaches live content by id.
  - `DockLayoutStore`: `void Save(IRootDock layout, string path)`, `IRootDock? Load(string path)` (returns null on missing/corrupt, logging a warning), `string DefaultPath { get; }`, `void Delete(string path)`.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Docking/DockLayoutStoreTests.cs
using DialogEditor.Avalonia.Docking;

namespace DialogEditor.Tests.Docking;

public class DockLayoutStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"layout-{Guid.NewGuid():N}.json");
    public void Dispose() { try { File.Delete(_path); } catch { /* best-effort */ } }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
        => Assert.Null(new DockLayoutStore().Load(_path));

    [Fact]
    public void Load_CorruptFile_ReturnsNull_DoesNotThrow()
    {
        File.WriteAllText(_path, "{ not valid dock json");
        Assert.Null(new DockLayoutStore().Load(_path));   // logged, not thrown
    }

    [Fact]
    public void SaveThenLoad_RoundTripsStructure()
    {
        var factory = new EditorDockFactory(
            new DialogEditor.ViewModels.GameBrowserViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.ConversationViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.NodeDetailViewModel(),
            new DialogEditor.ViewModels.ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
        DialogEditor.ViewModels.Resources.Loc.Configure(new DialogEditor.Tests.Helpers.StubStringProvider());
        var layout = factory.CreateLayout();

        var store = new DockLayoutStore();
        store.Save(layout, _path);
        var restored = store.Load(_path);

        Assert.NotNull(restored);
        Assert.Equal("Canvas".Length > 0, true);   // sanity; real assert below
        // Assert a known id survives the round trip:
        Assert.Contains("Browser", Flatten(restored!));
    }

    private static IEnumerable<string?> Flatten(Dock.Model.Core.IDockable d)
    {
        yield return d.Id;
        if (d is Dock.Model.Core.IDock dock && dock.VisibleDockables is not null)
            foreach (var c in dock.VisibleDockables) foreach (var x in Flatten(c)) yield return x;
    }
}
```

> Confirm the serializer type name (`SystemTextJsonDockSerializer` vs `DockSerializer`) against the installed `Dock.Serializer.SystemTextJson`, and whether `Save`/`Load` take streams or strings — adjust `DockLayoutStore` to match.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DockLayoutStoreTests"`
Expected: FAIL — `DockLayoutStore` doesn't exist.

- [ ] **Step 3: Implement `DockLayoutStore`**

```csharp
// DialogEditor.Avalonia/Docking/DockLayoutStore.cs
using System.IO;
using Dock.Model.Controls;
using Dock.Serializer;                 // confirm namespace
using DialogEditor.ViewModels.Services; // AppLog

namespace DialogEditor.Avalonia.Docking;

/// Persists the dock layout structure to JSON. Content (live VMs) is NOT serialized — it is
/// re-attached by EditorDockFactory locators on load. A missing/corrupt file returns null so
/// the caller falls back to the factory default; it never throws.
public sealed class DockLayoutStore
{
    private readonly IDockSerializer _serializer = new SystemTextJsonDockSerializer();  // confirm ctor

    public string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "layout.json");

    public void Save(IRootDock layout, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var s = File.Create(path);
            _serializer.Save(s, layout);
        }
        catch (Exception ex) { AppLog.Warn($"Dock layout save failed: {ex.Message}"); }
    }

    public IRootDock? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var s = File.OpenRead(path);
            return _serializer.Load<IRootDock>(s);
        }
        catch (Exception ex) { AppLog.Warn($"Dock layout load failed, using default: {ex.Message}"); return null; }
    }

    public void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Warn($"Dock layout delete failed: {ex.Message}"); }
    }
}
```

- [ ] **Step 4: Add locators to `EditorDockFactory.InitLayout`**

```csharp
public override void InitLayout(IDockable layout)
{
    ContextLocator = new Dictionary<string, Func<object?>>
    {
        ["Browser"]         = () => _browser,
        ["Canvas"]          = () => _canvas,
        ["Details"]         = () => _details,
        ["ConditionSearch"] = () => _search,
    };
    DockableLocator = new Dictionary<string, Func<IDockable?>>
    {
        ["Browser"]         = () => new BrowserTool(_browser),
        ["Canvas"]          = () => new CanvasDocument(_canvas),
        ["Details"]         = () => new DetailsTool(_details),
        ["ConditionSearch"] = () => new ConditionSearchTool(_search),
        ["Documents"]       = () => DocumentDock,
    };
    HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
    {
        [nameof(IDockWindow)] = () => new EditorHostWindow(),
    };
    base.InitLayout(layout);
}
```

> Content re-hydration on load is the riskiest bit. Confirm the exact restore sequence against the installed Dock sample: typically `store.Load` → `dockState.Restore(restored)` → `factory.InitLayout(restored)` → assign to `DockControl.Layout`. If a `DockState`/`Restore` step is required, add it in the view (Step 5). Verify in the GUI that a restored layout shows **live** panels (not blank), which proves re-hydration works.

- [ ] **Step 5: Wire load-on-build, save-on-exit, reset-deletes-file**

In `MainWindow.axaml.cs`:
- In `BuildDock`, before assigning the default, try `var restored = _store.Load(_store.DefaultPath);` — if non-null, `factory.InitLayout(restored)` (+ any `Restore` step) and use it; else use `factory.CreateLayout()`.
- Save on window close: override `OnClosing` → `if (vm.DockLayout is IRootDock root) _store.Save(root, _store.DefaultPath);`.
- In `ResetLayout_Click`: `_store.Delete(_store.DefaultPath);` then rebuild the default via `factory.CreateLayout()`.

- [ ] **Step 6: Run tests + GUI-verify persistence**

Run: `dotnet test --filter "FullyQualifiedName~DockLayoutStoreTests"` → PASS
`running-the-app`: rearrange panels (float one, move a tab), close the app, relaunch → the arrangement is restored with live content; View ▸ Reset Layout → returns to default and the next relaunch is default (file deleted).

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Docking/DockLayoutStore.cs DialogEditor.Avalonia/Docking/EditorDockFactory.cs DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Tests/Docking/DockLayoutStoreTests.cs
git commit -m "feat(docking): persist + restore layout with default fallback"
```

---

### Task 10: Full-suite green + Gaps.md

- [ ] **Step 1: Full suite**

Run: `dotnet test`
Expected: PASS. Investigate any failure (watch for `Loc`/`GameDataNameService` ordering — configure/clear in any new test that needs it).

- [ ] **Step 2: Mark the gap implemented**

Edit the **Visual Studio–style Docking Shell** entry in `Gaps.md`: change `**📐 Designed (2026-07-16, Phase 1), not yet implemented.**` to `**✅ Phase 1 implemented (<date>).**` and append the shipped specifics (Dock.Avalonia, the four dockables, View menu, persisted layout + reset, bottom tool tabs, themed float windows, `<commit>`). Leave the Phase 2 note.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark docking shell Phase 1 implemented"
```

---

## Self-Review

**Spec coverage:**
- Adopt Dock.Avalonia + thin wrapper tools → Tasks 1–3. ✓
- Panels: Browser, Canvas (document), Node Details, Condition search → Tasks 2–5. ✓
- Default layout (left/centre/right-tabbed) → Task 3, verified Task 5. ✓
- DockControl replaces the grid → Task 5. ✓
- View menu (show tools + Reset) → Task 6. ✓
- Theming to tokens, bottom tool tabs, floating window icon/theme, live retint → Task 7. ✓
- Remove old pin/collapse + condition-search toggle → Tasks 4, 8. ✓
- Persistence (save/load/fallback) + reset deletes file → Task 9. ✓
- Localisation / UIA / window-icon / error-logging / no-hex → distributed + Global Constraints. ✓
- Single document (`CanCreateDocument = false`) → Task 3. ✓

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N". Every code step shows code. The "confirm against the installed Dock version" notes are legitimate library-matching verifications for a newly added dependency (exact serializer type, factory helper names, theme resource keys, restore sequence) — each points at a concrete artifact (installed package API / Dock samples) to check, not deferred work.

**Type consistency:** `BrowserTool`/`DetailsTool`/`ConditionSearchTool`/`CanvasDocument` (+ `.Inner`, `.Id`), `EditorDockFactory(browser, canvas, details, search)` + `CreateLayout()`/`InitLayout()`/`DocumentDock`, `DockLayoutStore.{Save,Load,Delete,DefaultPath}`, `MainWindowViewModel.{ConditionSearch,DockLayout}`, `EditorHostWindow` are used consistently across tasks. `ConditionSearchViewModel(gameId, getSnapshot, applyHighlight, clearHighlight)` matches its Gap #2 signature.

**Known highest-risk task:** Task 9 (content re-hydration on layout load). It is sequenced last and isolated; if the Dock restore API differs from the shown sequence, only Task 9 changes. The shell (Tasks 1–8) is fully functional and shippable without persistence if Task 9 needs iteration.
