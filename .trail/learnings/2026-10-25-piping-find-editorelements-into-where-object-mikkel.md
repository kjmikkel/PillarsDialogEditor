---
title: Piping Find-EditorElements into Where-Object filters the whole array as one item
author: Mikkel
date: 2026-10-25
branch: fix/240-node-details-read-only
cost: { minutes: 12, tokens: 99144 }
id: l-07290fe8
scope: repo
status: active
touches:
  - DialogEditor.Tests/Controls/ThemeOnboardingWindowTests.cs
  - DialogEditor.Tests/ViewModels/NodeDetailViewModelReadOnlyTests.cs
  - DialogEditor.Tests/Views/NodeDetailViewReadOnlyTests.cs
  - DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
  - DialogEditor.Avalonia/Views/NodeDetailView.axaml
  - tools/ui-automation/CaptureSurfaces.ps1
---

## Watch out
`Find-EditorElements` (`tools/ui-automation/CaptureSurfaces.ps1`) returns its matches with `return ,@(...)`, so piping the call straight into `Where-Object` hands the filter the WHOLE array as one item: `$_.Current.Name -match 'x'` then runs on an array, returns the matching names (truthy), and every element passes. `Select-Object -First 1` then picks whatever came first (the conversation filter box, the Minimize button), which looks like the app labelling things wrong. Assign first, then filter: `$all = Find-EditorElements ...; $all | Where-Object { ... }`. Or pass `-Name` so UIA does the filtering.
