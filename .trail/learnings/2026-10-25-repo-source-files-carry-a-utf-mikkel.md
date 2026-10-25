---
title: Repo source files carry a UTF-8 BOM; edit scripts must write it back
author: Mikkel
date: 2026-10-25
branch: chore/restore-boms
cost: { minutes: 1, tokens: 2045 }
id: l-545193ef
scope: repo
status: active
touches:
  - DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs
  - DialogEditor.Avalonia/Resources/Strings.axaml
---

## Decided
Most `.cs` and `.axaml` files in this repo start with a UTF-8 byte-order mark, for example `DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs` and `DialogEditor.Avalonia/Resources/Strings.axaml`. A script that rewrites them must keep the BOM: in Python, read with `encoding='utf-8-sig'` and also write with `encoding='utf-8-sig'` when the file had one.

## Watch out
Reading with `utf-8-sig` and writing with plain `utf-8` silently drops the BOM. `git diff --stat` then shows an extra changed line 1 in every touched file. No `.editorconfig` rule or test catches it.

In October 2026 this happened to these files, among others:
- `BatchReplaceViewModel.cs`, `ProjectFindViewModel.cs` and `ScriptEditorViewModel.cs`
- `FindInProjectWindow.axaml` and `BatchReplaceWindow.axaml`

It was fixed in the chore/restore-boms PR.

To check a branch, compare `git show <base>:<f> | head -c3 | xxd -p` with the same command on the tip, for every file `git diff --name-only` lists. A file that had a BOM shows `efbbbf`.
