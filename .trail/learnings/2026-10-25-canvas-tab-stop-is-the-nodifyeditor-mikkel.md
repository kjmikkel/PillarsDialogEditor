---
title: "Canvas Tab stop is the NodifyEditor itself, which Nodify does not make a tab stop"
author: Mikkel
date: 2026-10-25
branch: fix/238-ports-out-of-tab-order
cost: { minutes: 8, tokens: 91103 }
id: l-ef761195
scope: repo
status: active
touches:
  - DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
  - DialogEditor.Avalonia/Views/ConversationView.axaml
  - DialogEditor.Avalonia/Views/ConversationView.axaml.cs
---

## Decided
Canvas ports (NodeInput/NodeOutput in `DialogEditor.Avalonia/Views/ConversationView.axaml`) are `Focusable="False"`; the NodifyEditor itself carries `IsTabStop="True"` and is the canvas's single Tab stop. `Editor_GotFocus` restores a selection and the arrow keys move from there (#238).

## Watch out
NodifyAvalonia 6.6.0 makes the editor no tab stop by default. Before #238 the canvas was reachable by Tab *only* through its ports, so making the ports non-focusable without `IsTabStop="True"` on the editor silently drops the canvas from the Tab order (search box → toolbar buttons → wraps). `Focusable="True"` alone does not help. `ConversationViewKeyboardTests.Tab_FromSearchBox_SkipsConnectorPorts_AndReachesCanvas` walks the chain with `KeyboardNavigationHandler.GetNext` and guards both halves.
