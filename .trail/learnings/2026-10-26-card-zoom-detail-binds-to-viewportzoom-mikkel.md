---
title: "Card zoom detail binds to ViewportZoom; the card's bottom inset belongs to the footer"
author: Mikkel
date: 2026-10-26
branch: feat/221-card-lod
cost: { minutes: 13, tokens: 97379 }
id: l-2f637512
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/App.axaml
  - DialogEditor.Avalonia/Converters/CardDetailConverter.cs
  - DialogEditor.Avalonia/Views/ConversationView.axaml
  - DialogEditor.Core/Layout/CardDetail.cs
  - DialogEditor.Core/Layout/CardMetrics.cs
  - DialogEditor.Tests/Layout/CardDetailTests.cs
  - DialogEditor.Tests/Views/CanvasCardRenderTests.cs
  - DialogEditor.Tests/Controls/CanvasDotGridTests.cs
  - DialogEditor.Avalonia/Controls/CanvasDotGrid.cs
---

## Decided
GitHub issue 221: the canvas card's detail level follows `NodifyEditor.ViewportZoom` through `CardDetailConverter` bindings in the card template (`DialogEditor.Avalonia/Views/ConversationView.axaml`). The bands are in `DialogEditor.Core/Layout/CardDetail.cs`. Node positions never change with zoom: layout keeps the normal card height, and a compact card is never taller.

## Tried and ruled out
A style on a zoom class can't shrink the body: its `MaxLines` was set locally in the template, and a local value beats any style. That's why it's a binding.

## Watch out
The card's 1 px bottom inset is the footer Border's bottom margin. Hide the footer and the card is one `BorderThickness` shorter than `HeightFor(lines) - FooterHeight` (`CardMetrics.CompactHeight` accounts for this).
An "up to 8 lines above 125%" band can't keep positions fixed: an 8-line card is 64 px taller than the layout assumes, and `RowGap` is 28. It needs a re-flow or a design change, not just a converter value.
