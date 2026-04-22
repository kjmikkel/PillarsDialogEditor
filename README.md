# Pillars Dialog Editor

A read-only dialog graph viewer for *Pillars of Eternity* (PoE1) and *Pillars of Eternity II: Deadfire* (PoE2). Browse conversations as node graphs, inspect conditions, scripts, and text — including gendered text variants and multi-language support.

## Features

- **Node graph canvas** — conversations rendered as a directed graph with automatic BFS layout
- **Speaker colouring** — node cards are colour-coded by speaker category (NPC, Player, Narrator, Script)
- **Speaker name resolution** — companion and narrator GUIDs resolved to readable names; PoE2 loads all 1000+ names dynamically from `speakers.gamedatabundle`
- **Connection highlighting** — selecting a node highlights all incoming and outgoing connections
- **Detail panel** — shows speaker, listener, conditions, scripts (with parameters), display type, persistence, and both text variants
- **Gendered text** — both the default/male text and the female text variant are always visible; node cards show a ♀ badge when a female variant exists
- **Language switching** — all localised languages discovered automatically; last-used language remembered across sessions
- **Condition display** — conditions formatted as readable function calls, with nested expressions flattened
- **Script display** — OnEnter / OnExit / OnUpdate script calls shown with their parameters

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for building)
- A local installation of *Pillars of Eternity* and/or *Pillars of Eternity II: Deadfire*

## Building

```
dotnet build
```

Run the WPF application:

```
dotnet run --project DialogEditor.WPF
```

Run tests:

```
dotnet test
```

## Usage

1. Launch the application
2. Click **Open Game Folder…** and select the game's root folder
   - PoE1: the folder containing `PillarsOfEternity_Data/`
   - PoE2: the folder containing `PillarsOfEternityII_Data/`
3. Browse conversations in the left panel; click one to open it
4. Select a language from the toolbar dropdown — the open conversation reloads immediately
5. Click a node on the canvas to see full details in the right panel

## Project structure

```
DialogEditor.Core/     — Parsing, models, layout (net8.0, no UI dependencies)
  GameData/            — IGameDataProvider, PoE1 and PoE2 providers, LanguagePicker
  Models/              — ConversationNode, NodeLink, StringEntry, StringTable, Conversation
  Parsing/             — PoE1 XML parser, PoE2 JSON parser, StringTableParser, ConditionFormatter
  Layout/              — AutoLayoutService (BFS layered layout)

DialogEditor.WPF/      — WPF application (net10.0-windows)
  ViewModels/          — MVVM view models using CommunityToolkit.Mvvm
  Views/               — XAML views using Nodify for the graph canvas
  Converters/          — Value converters for XAML bindings
  Services/            — SpeakerNameService, AppSettings

DialogEditor.Tests/    — xUnit tests (net8.0)
```

## Game data paths

| Game | Conversations | String tables |
|------|--------------|---------------|
| PoE1 | `PillarsOfEternity_Data/data/conversations/**/*.conversation` | `PillarsOfEternity_Data/data/localized/{lang}/text/conversations/` |
| PoE2 | `PillarsOfEternityII_Data/exported/design/conversations/**/*.conversationbundle` | `PillarsOfEternityII_Data/exported/localized/{lang}/text/conversations/` |

## Notes

- The editor is read-only — no game files are modified
- PoE2 speaker names are loaded from `speakers.gamedatabundle` at startup; PoE1 uses a hardcoded set of companion GUIDs
- Language preference is stored in `%LOCALAPPDATA%\PillarsDialogEditor\settings.json`
