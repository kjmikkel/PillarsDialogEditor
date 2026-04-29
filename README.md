# Pillars Dialog Editor

A read-only dialog graph viewer for *Pillars of Eternity* (PoE1) and *Pillars of Eternity II: Deadfire* (PoE2). Conversations are rendered as directed node graphs; inspect conditions, scripts, text variants, and voice data without modifying any game files.

Two builds share the same core and ViewModels:

| Build | Platform | Package |
|---|---|---|
| `DialogEditor.WPF` | Windows only | .NET 10, WPF + Nodify 6.2 |
| `DialogEditor.Avalonia` | Windows · macOS · Linux | .NET 8, Avalonia 11 + NodifyAvalonia 6.6 |

---

## Features

### Canvas
- **Node graph** — BFS layered auto-layout; pan with right-click drag, zoom with scroll wheel
- **Fit / Zoom** — toolbar buttons; **Ctrl+F** focuses the search box
- **Minimap** — bottom-right overlay; drag to pan the main view
- **Node cards** — colour-coded header by speaker category (NPC · Player · Narrator · Script); body shows first 80 characters; footer shows condition count and ♀ badge when a female variant exists
- **Search** — filters nodes by ID, dialogue text, or speaker name; non-matching nodes are dimmed; **Escape** clears
- **Connection colours** — ShowOnce (grey solid), Always (amber), Never (grey dashed); each highlighted distinctly when the connected node is selected
- **⌂ button** — jump to the root node (ID 0)

### Browser
- **Conversation tree** — grouped by folder; filter by filename (Escape clears)
- **Expand / collapse all** — ⊞ / ⊟ buttons
- **Pin / flyout** — 📌 pin keeps the panel open; unpinned = flyout mode (closes on click-outside or after selecting a conversation)
- Remembers the last opened game folder across sessions

### Detail panel
- Speaker, listener, full dialogue text (default and female variant)
- Conditions formatted as an indented AND/OR tree (logical grouping preserved)
- Scripts (OnEnter / OnExit / OnUpdate) with full parameters
- Display type, persistence, actor direction (PoE1), comments (PoE1)
- Voice data: file path (PoE1 `VOFilename`, PoE2 `ExternalVO`), Has VO, Hide Speaker
- Links to other nodes with random weight and QuestionNodeTextDisplay annotations

### General
- **Multi-language** — all localised languages discovered automatically; last-used language remembered
- **Speaker names** — PoE2 loads all names from `speakers.gamedatabundle`; PoE1 uses companion GUIDs
- **Dark theme** throughout, OS title bar included
- **Legend panel** — click **?** in the toolbar for an in-app reference of all colours, symbols, and keyboard shortcuts
- **Localised UI** — all strings in resource files (`Strings.xaml` / `Strings.axaml`); ready for translation
- **Settings** persisted in `%LOCALAPPDATA%\PillarsDialogEditor\settings.json`

---

## Requirements

### WPF (Windows)
- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Avalonia (cross-platform)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- **Linux only** — a few system libraries:
  ```bash
  # Debian / Ubuntu
  sudo apt install libice6 libsm6 libfontconfig1
  ```

---

## Running

### Quickest way

```bash
# macOS / Linux
./run-avalonia.sh

# Windows — WPF build
run-wpf.bat
```

### Via Make

```bash
make                # run Avalonia (default)
make run-avalonia   # same
make run-wpf        # Windows only
make test
make build
```

### Via dotnet directly

```bash
# Avalonia (cross-platform)
dotnet run --project DialogEditor.Avalonia

# WPF (Windows only)
dotnet run --project DialogEditor.WPF

# Tests
dotnet test
```

---

## Usage

1. Launch the application
2. Click **Open Game Folder…** and select the game's root directory
   - PoE1: folder containing `PillarsOfEternity_Data/`
   - PoE2: folder containing `PillarsOfEternityII_Data/`
3. The last-opened folder is remembered; it loads automatically on next launch
4. Browse conversations in the left panel — click a conversation to open it on the canvas
5. Select a language from the toolbar dropdown (reloads the open conversation immediately)
6. Click a node on the canvas to see full details in the right panel
7. Press **?** in the top-right toolbar for the in-app legend

---

## Project structure

```
DialogEditor.Core/          net8.0 — zero UI dependencies
  GameData/                 IGameDataProvider, PoE1/PoE2 providers, LanguagePicker
  Models/                   ConversationNode, NodeLink, StringEntry, LayoutPoint, …
  Parsing/                  PoE1 XML parser, PoE2 JSON parser, ConditionFormatter
  Layout/                   AutoLayoutService (BFS layered layout)
  Resources/                CoreStrings.resx — script prefixes, condition "NOT"

DialogEditor.ViewModels/    net8.0 — shared across WPF and Avalonia
  ViewModels/               MVVM view models (CommunityToolkit.Mvvm)
  Services/                 IDispatcher, IFolderPicker, IStringProvider + platform adapters
  Resources/                Loc.cs — static string accessor

DialogEditor.WPF/           net10.0-windows — Windows reference build
  Views/                    XAML views, Nodify 6.2 canvas
  Converters/               WPF value converters
  Services/                 WpfDispatcher, WpfFolderPicker, WpfStringProvider
  Resources/                Strings.xaml

DialogEditor.Avalonia/      net8.0 — cross-platform build
  Views/                    AXAML views, NodifyAvalonia 6.6 canvas
  Converters/               Avalonia value converters
  Services/                 AvaloniaDispatcher, AvaloniaFolderPicker, AvaloniaStringProvider
  Resources/                Strings.axaml

DialogEditor.Tests/         net8.0 — xUnit tests (Core only)
```

---

## Game data paths

| Game | Conversations | String tables |
|---|---|---|
| PoE1 | `PillarsOfEternity_Data/data/conversations/**/*.conversation` | `…/data/localized/{lang}/text/conversations/` |
| PoE2 | `PillarsOfEternityII_Data/exported/design/conversations/**/*.conversationbundle` | `…/exported/localized/{lang}/text/conversations/` |

---

## Notes

- **Read-only** — no game files are ever modified
- **PoE2 speaker names** — loaded from `speakers.gamedatabundle` at startup; replaced entirely when a new folder is opened
- **Conditions** — both parsers preserve the full AND/OR/NOT tree structure rather than flattening to a plain list
- **Connection types** — `QuestionNodeTextDisplay` values (`ShowOnce`, `Always`, `Never`) are rendered as distinct edge colours on the canvas
