# Dialog Editor — Design Spec
*Date: 2026-04-20*

## Overview

A read-only dialog viewer for Pillars of Eternity 1 and Pillars of Eternity 2: Deadfire. Displays conversation files as a Missouri-style node canvas: color-coded cards on a freeform, pannable/zoomable surface with visible connections between nodes.

**v1 scope: read-only viewer. No editing.**

---

## Technology Stack

| Layer | Choice | Rationale |
|---|---|---|
| Language | C# (.NET 8) | Primary language comfort (95%) |
| UI framework | WPF | Best canvas support for node editors on Windows |
| Node canvas | Nodify (WPF) | Mature, MVVM-native, performant up to ~200 nodes |
| Pattern | MVVM (CommunityToolkit.Mvvm) | Standard for WPF; maximises Avalonia portability later |
| Platform | Windows only (v1) | Avalonia port planned as v2 using same Core library |

### Future portability
The solution is structured so that `DialogEditor.Core` (zero UI dependencies) reuses entirely in a future `DialogEditor.Avalonia` project. ViewModels will need minor namespace adjustments. A direct Nodify→Avalonia port library (`BAndysc/nodify-avalonia`) exists and follows the same API, meaning even the canvas XAML is largely portable.

---

## Solution Structure

```
DialogEditor.sln
├── DialogEditor.Core/          .NET 8 class library — no UI dependencies
│   ├── Models/
│   │   ├── ConversationNode.cs
│   │   ├── NodeLink.cs
│   │   ├── Conversation.cs
│   │   ├── StringEntry.cs
│   │   └── StringTable.cs
│   ├── Parsing/
│   │   ├── Poe1ConversationParser.cs   XML → Conversation
│   │   ├── Poe2ConversationParser.cs   JSON → Conversation
│   │   └── StringTableParser.cs        shared (same XML format both games)
│   └── GameData/
│       ├── IGameDataProvider.cs
│       ├── Poe1GameDataProvider.cs
│       └── Poe2GameDataProvider.cs
│
└── DialogEditor.WPF/           WPF .NET 8 application
    ├── ViewModels/
    │   ├── MainWindowViewModel.cs
    │   ├── GameBrowserViewModel.cs
    │   ├── ConversationItemViewModel.cs
    │   ├── ConversationViewModel.cs
    │   ├── NodeViewModel.cs
    │   ├── ConnectionViewModel.cs
    │   └── NodeDetailViewModel.cs
    └── Views/
        ├── MainWindow.xaml
        ├── GameBrowserView.xaml
        ├── ConversationView.xaml        Nodify canvas lives here
        └── NodeDetailView.xaml
```

---

## File Formats

### PoE1
| File | Format | Path pattern |
|---|---|---|
| Structure | XML (`.conversation`) | `{root}/PillarsOfEternity_Data/data/conversations/**/*.conversation` |
| Strings | XML (`.stringtable`) | `{root}/PillarsOfEternity_Data/data/localized/en/text/conversations/**/*.stringtable` |

**Key mapping:** `NodeID` in the conversation XML == `Entry.ID` in the stringtable. No explicit foreign key field; this is a convention.

### PoE2
| File | Format | Path pattern |
|---|---|---|
| Structure | JSON (`.conversationbundle`) | `{root}/PillarsOfEternityII_Data/exported/design/conversations/**/*.conversationbundle` |
| Strings | XML (`.stringtable`) | `{root}/PillarsOfEternityII_Data/exported/localized/en/text/conversations/**/*.stringtable` |

**Same NodeID == Entry.ID convention.** The JSON uses `$type` fields for polymorphism (`OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats`). `DisplayType` and `Persistence` are stored as integers in PoE2 JSON; the parser maps them to the same string values used by PoE1 (`0` → `"Conversation"`, `1` → `"Bark"`, etc.).

### Game detection
Presence of `PillarsOfEternity_Data/` → PoE1. Presence of `PillarsOfEternityII_Data/` → PoE2. Selected at startup when the user chooses their game root folder.

---

## Core Data Model

```csharp
record ConversationNode(
    int NodeId,
    bool IsPlayerChoice,       // IsQuestionNode in source
    string SpeakerGuid,
    string ListenerGuid,
    IReadOnlyList<NodeLink> Links,
    bool HasConditions,        // Conditionals.Components.Count > 0
    bool HasScripts,           // any OnEnter/OnExit/OnUpdate scripts non-empty
    string DisplayType,        // "Conversation", "Bark", etc.
    string Persistence         // "None", "OnceEver", etc.
);

record NodeLink(
    int FromNodeId,
    int ToNodeId,
    bool HasConditions
);

record StringEntry(
    int Id,
    string DefaultText,
    string FemaleText
);
```

Text is **not** stored on `ConversationNode` — it is looked up at display time via `StringTable[NodeId]`. This matches the games' own separation of structure from localisation.

---

## UI Layout

Three-panel layout:

```
┌─────────────────┬──────────────────────────────┬──────────────────┐
│  Conversations  │         Canvas (Nodify)       │   Node Details   │
│  (TreeView)     │  [toolbar: search/zoom/fit]   │                  │
│                 │                               │  Node ID / Type  │
│  📁 companions  │   ┌──────────┐                │  Speaker         │
│    📁 banters   │   │ Node 0   │──►┌──────────┐ │  Full Text       │
│    ► aloth_…   │   │ Root     │   │ Node 1   │ │  Female Text     │
│      eder_…    │   └──────────┘   │ NPC line │ │  Conditions      │
│  📁 quests      │                 └──────────┘ │  Scripts         │
│                 │                    Minimap ░░ │  Display/Persist │
│  🎮 PoE I badge │                              │  Links to        │
└─────────────────┴──────────────────────────────┴──────────────────┘
```

### Node appearance

| Node type | Header colour | Determined by |
|---|---|---|
| NPC line | Dark red `#7b241c` | `IsPlayerChoice == false` |
| Player choice | Dark blue `#1a5276` | `IsPlayerChoice == true` |

Each node card shows:
- **Header:** `Node {id} · {SpeakerName}` (✦ suffix for player choice)
- **Body:** first ~80 characters of `DefaultText`
- **Footer:** condition/script indicator (`⚙ N conditions`) or `[ No conditions ]`

### Missing stringtable
If the stringtable file cannot be found at the resolved path, node body text displays as `[text unavailable — stringtable not found]`. The canvas still renders with full structure intact.

### Speaker name resolution
GUIDs are resolved to human-readable names via a hardcoded lookup of known companion GUIDs for both games. Unknown GUIDs display the first 8 characters of the GUID.

### Auto-layout
Nodify requires explicit `Point` positions per node. v1 uses a **layered left-to-right layout**: nodes are grouped by their graph depth from the root node and spread vertically within each layer. This produces immediately readable layouts without manual placement.

---

## Data Flow

```
User selects root folder
        │
        ▼
GameDataProvider.Detect(root)     → IGameDataProvider (PoE1 or PoE2)
        │
        ▼
Provider.EnumerateConversations() → IEnumerable<ConversationFile>
        │                           drives left-panel TreeView
        ▼
User clicks a file
        │
        ▼
Provider.ResolveStringTablePath() → stringtable path (by convention)
        │
        ▼
Parser.Parse(convPath, stPath)    → (Conversation, StringTable)
        │
        ▼
ConversationViewModel             → NodeViewModel[], ConnectionViewModel[]
        │                           positions computed by auto-layout
        ▼
Nodify canvas renders nodes and connections
        │
        ▼
User clicks a node
        │
        ▼
NodeDetailViewModel               → right panel updates
```

---

## Out of Scope (v1)

- Editing dialog text or structure
- Creating new conversations or nodes
- Conditions/scripts editing
- Audio file references
- Any language other than English
- PoE2 emotion/animation metadata display
- Search across multiple conversations simultaneously

---

## Planned v2 (Avalonia port)

`DialogEditor.Core` reuses entirely. The WPF-specific work to replace:
- Views rewritten in Avalonia XAML (similar syntax, different namespaces)
- Nodify → `BAndysc/nodify-avalonia` (same API)
- CommunityToolkit.Mvvm ViewModels: minor namespace adjustments only
