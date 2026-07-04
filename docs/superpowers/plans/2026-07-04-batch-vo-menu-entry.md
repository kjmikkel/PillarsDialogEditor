# Batch VO Import Test-Menu Entry Point Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the per-conversation batch VO import discoverable via a Test-menu item that auto-disables when it cannot run, per `docs/superpowers/specs/2026-07-04-batch-vo-menu-entry-design.md`.

**Architecture:** Pure XAML + strings change. One `MenuItem` in `MainWindow.axaml`'s Test menu binds `Canvas.BatchImportVoCommand`; Avalonia auto-disables it from the command's `CanExecuteChanged` (already notified on project-path and node changes at `ConversationViewModel.cs:28` and `:83`). No ViewModel changes.

**Tech Stack:** Avalonia 11 XAML, `Strings.axaml` resource dictionary.

## Global Constraints

- No user-visible text hard-coded in XAML or C# — everything via `Strings.axaml` keys.
- Every interactive control gets `ToolTip.Tip` **and** `AutomationProperties.HelpText`.
- `CHANGELOG.md` is frozen — do not touch it.
- No new logic ⇒ no new unit tests (spec: verification is manual); do not add an `IsEnabled`/`IsVisible` binding — the command gate is the single source of truth.

---

### Task 1: Menu item + tooltip string

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (batch block, next to `Menu_BatchImportVo` ~line 1226)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (Test menu, after the `Menu_ValidateVO` item ending ~line 157)

**Interfaces:**
- Consumes: existing `Menu_BatchImportVo` string key; `MainWindowViewModel.Canvas` (a `ConversationViewModel`) and its `BatchImportVoCommand`.
- Produces: string key `ToolTip_Menu_BatchImportVo_Main` (Task 2's Gaps text references the feature, not the key).

- [ ] **Step 1: Add the tooltip string**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, directly below the line

```xml
    <sys:String x:Key="ToolTip_Menu_BatchImportVo">Open the batch voice-over import dialog for this conversation.</sys:String>
```

add:

```xml
    <!-- Main-menu variant: the Test-menu item disables (rather than hides), so its
         tooltip must explain the enablement conditions. -->
    <sys:String x:Key="ToolTip_Menu_BatchImportVo_Main">Open the batch voice-over import dialog for this conversation. Requires an open project and a conversation with at least one voiced node.</sys:String>
```

- [ ] **Step 2: Add the menu item**

In `DialogEditor.Avalonia/Views/MainWindow.axaml`, inside the Test menu, directly after

```xml
                        <MenuItem Header="{DynamicResource Menu_ValidateVO}"
                                  Click="ValidateVO_Click"
                                  IsEnabled="{Binding CanValidateVO}"
                                  ToolTip.Tip="{DynamicResource ToolTip_ValidateVO}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_ValidateVO}"/>
```

and before the closing `</MenuItem>` of the Test menu, add:

```xml
                        <!-- Batch VO import entry point (2026-07-04): previously only in the
                             canvas context menu, which hides the item when it can't run —
                             invisible to users who never right-click at the right moment.
                             Deliberately NO IsEnabled/IsVisible binding: the MenuItem
                             auto-disables from BatchImportVoCommand.CanExecuteChanged
                             (visible-but-disabled is the discoverability point), and the
                             command gate stays the single source of truth. -->
                        <MenuItem Header="{DynamicResource Menu_BatchImportVo}"
                                  Command="{Binding Canvas.BatchImportVoCommand}"
                                  ToolTip.Tip="{DynamicResource ToolTip_Menu_BatchImportVo_Main}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_BatchImportVo_Main}"/>
```

- [ ] **Step 3: Build and run the full test suite**

Run: `dotnet build && dotnet test --nologo`
Expected: build success (no `AVLN` XAML errors), all tests pass (1800 as of plan date).

- [ ] **Step 4: Commit**

```bash
git add "DialogEditor.Avalonia/Views/MainWindow.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml"
git commit -m "feat(vo): batch VO import entry point in Test menu

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Gaps.md resolution + manual verification

**Files:**
- Modify: `Gaps.md` (the "Batch VO import is only reachable via canvas right-click" bullet under Voice-Over Integration → Remaining gaps)

**Interfaces:**
- Consumes: Task 1's menu item (feature must be merged/present in the working copy).
- Produces: nothing downstream.

- [ ] **Step 1: Rewrite the Gaps.md bullet**

Replace the bullet beginning `- **Batch VO import is only reachable via canvas right-click**` (keep the bullet, rewrite its text) with:

```markdown
- **Batch VO import entry point ✓ resolved (2026-07-04):** "Batch import VO…" now sits in
  the Test menu after Validate Voice-Over, auto-disabling via the command gate when no
  project is open or the conversation has no voiced nodes (visible-but-disabled for
  discoverability; tooltip names the conditions). The canvas context-menu item remains as
  a shortcut. The separate "all conversations" gap above stays open (scope decision
  2026-07-04).
```

- [ ] **Step 2: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark batch VO import entry-point gap resolved

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 3: Manual verification checklist** — `dotnet run --project DialogEditor.Avalonia`, open the Deadfire game folder.

- [ ] Test menu shows "Batch import VO…" directly after Validate Voice-Over
- [ ] With no project open: item visible but disabled; tooltip shows and names the conditions
- [ ] Open the project + `08_cv_atsura`: item enabled; clicking opens the batch import dialog (aliased rows still show "shared")
- [ ] Open a conversation with no voiced nodes (any PoE1 conversation, or a new empty one): item disabled
- [ ] Canvas right-click "Batch import VO…" still works unchanged

- [ ] **Step 4: Report results** — any failure: fix, `dotnet build && dotnet test --nologo`, re-verify, commit as `fix(vo): …`.
