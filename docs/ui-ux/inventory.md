# UI inventory

Every reachable screen, window, dialog, menu and command of Pillars Dialog Editor, the
standalone Pillars Dialog Patcher and the `dialog-patcher` CLI. Written by the UI/UX pass of
2026-10-24 (whole app). Evidence lives in `reports/2026-10-24-whole-app/shots/`.

`observed`: **run** = seen running in a Debug build; **seen in code only** = enumerated from
the code but not opened (usually it needs a file picker, a git conflict or a crash).

States: `1x`/`2x` = font scale 100 %/200 %; themes `D` Dark, `L` Light, `HC` High Contrast,
`CB` Colourblind; `kbd` = keyboard only; `SR` = Narrator / UIA tree; `empty` = nothing to show;
`noproj` = game folder open, no project (the Reader's view); `first` = first launch.

## Items

### Editor: main window and docks

| id | kind | name | code | how reached | observed | states visited | components |
|---|---|---|---|---|---|---|---|
| S1 | window | Main window (menu bar, status bar) | `DialogEditor.Avalonia/Views/MainWindow.axaml` | launch | run | D L HC CB × 1x 2x, noproj, kbd, SR, 1024×700, 800×600 | menu bar, combo box (language), icon button (settings, help), status bar |
| S2 | pane | Conversations dock (game browser) | `Views/GameBrowserView.axaml` | left dock | run | D L HC CB × 1x 2x, noproj, kbd, SR | dock pane header, search box, check box, tree view |
| S3 | pane | Canvas | `Views/ConversationView.axaml` | open a conversation | run | D L HC CB × 1x 2x, noproj, kbd, SR, narrow | dock tab, toolbar (icon buttons), search box, node card, connection, connector, minimap |
| S4 | pane | Node Details dock | `Views/NodeDetailView.axaml` | select a node | run | D L HC CB × 1x 2x, noproj (editable), kbd (Enter from canvas) | text box, combo box, expander, check box, link row, icon button |
| S5 | pane | Condition search dock | `Views/ConditionSearchView.axaml` | View › Condition search | run | D L HC CB × 1x 2x | search box, button, result list |
| S6 | pane | Annotation (note / region) | `Views/AnnotationView.axaml` | canvas › + Note / Region | run | D L HC CB × 1x 2x | canvas card, text box |
| S7 | overlay | Guided tour bar | `Controls/GuidedTourBar.axaml` | Help › Start guided tour | run | D L HC CB × 1x 2x | step bar, button |
| S8 | overlay | Test mode overlay | `Views/TestModeOverlay.axaml` | F5 with a project | seen in code only | — | banner, button |
| S9 | window | Theme onboarding (first run) | `DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml` | first launch | run | first, D, kbd | combo box, preview, button, focus hint bar |

### Editor: tool windows

| id | kind | name | code | how reached | observed | states visited | components |
|---|---|---|---|---|---|---|---|
| W1 | window | Settings | `Views/SettingsWindow.axaml` | Ctrl+, / File › Settings | run | D L HC CB × 1x 2x, kbd | label + field row, combo box, button, text sample |
| W2 | window | Find / Replace | `Views/FindReplaceWindow.axaml` | Ctrl+H | run | D L HC CB × 1x 2x, kbd | text box, check box, button row |
| W3 | window | Batch Replace | `Views/BatchReplaceWindow.axaml` | Ctrl+Shift+H | run | D L HC CB × 1x 2x, empty | text box, check box, button, results list |
| W4 | window | Find in Project | `Views/FindInProjectWindow.axaml` | Ctrl+Shift+F | run | D L HC CB × 1x 2x, empty | search box, check box, results table |
| W5 | window | Flow Analytics | `Views/FlowAnalyticsWindow.axaml` | F7 | run | D L HC CB × 1x 2x | stat grid, list, combo box |
| W6 | window | Speaker line browser | `Views/SpeakerLineBrowserWindow.axaml` | Edit › Browse speaker lines | run | D L HC CB × 1x 2x | combo box, check box, results table |
| W7 | window | Rep / Disposition balance | `Views/RepDispositionBalanceWindow.axaml` | Edit › Rep/Disposition balance | run | D L HC CB × 1x 2x, empty | combo box, button, table |
| W8 | window | Compare versions (diff) | `Views/DiffWindow.axaml` | Edit › Compare versions | run | D L HC CB × 1x 2x, empty | combo box, list, radio, legend, check box, button |
| W9 | window | Diff help | `Views/DiffHelpWindow.axaml` | W8 › ? | seen in code only | — | text |
| W10 | window | History | `Views/HistoryWindow.axaml` | Edit › History | run | D L HC CB × 1x 2x | list, button |
| W11 | window | Attribution (blame) | `Views/BlameWindow.axaml` | Edit › Attribution | run | D L HC CB × 1x 2x | table, button |
| W12 | window | Branches | `Views/BranchesWindow.axaml` | Edit › Branches | run | D L HC CB × 1x 2x | list, button row |
| W13 | window | Patch Manager (in editor) | `Views/PatchManagerWindow.axaml` → `Shared/PatchManagerView.axaml` | File › Patch Manager | run | D L HC CB × 1x 2x, empty | text box, button, list, link |
| W14 | window | Export conversations | `Views/ExportConversationsWindow.axaml` | File › Export conversation | run | D L HC CB × 1x 2x | check list, radio group, button |
| W15 | window | Validate VO | `Views/VoValidationWindow.axaml` | Test › Validate VO | run | D L HC CB × 1x 2x | summary, list, button |
| W16 | window | Validate text tags | `Views/TextTagValidationWindow.axaml` | Test › Validate text tags | run | D L HC CB × 1x 2x, empty | summary, list, check box, combo box |
| W17 | window | Tag reference | `Views/TagReferenceWindow.axaml` | Help › Tag reference | run | D L HC CB × 1x 2x | search box, combo box, reference list |
| W18 | window | Changelog | `Views/ChangelogWindow.axaml` | Help › Changelog | run | D L HC CB × 1x 2x, empty | text, button |
| W19 | window | About | `Views/AboutWindow.axaml` | Help › About | run | D L HC CB × 1x 2x | text, link button |
| W20 | window | Legend | `Views/LegendWindow.axaml` | ? (toggle help) | run | D L HC CB × 1x 2x | legend list |
| W21 | window | Condition editor | `Views/ConditionEditorWindow.axaml` | S4 › Edit conditions | run | D L HC CB × 1x 2x | condition row, combo box, text box, icon button, OK/Cancel |
| W22 | window | Script editor | `Views/ScriptEditorWindow.axaml` | S4 › Edit scripts | run | D L HC CB × 1x 2x, empty | autocomplete box, button, OK/Cancel |
| W23 | window | VO alias picker | `Views/VoAliasPickerWindow.axaml` | S4 › Reuse another line's VO | run | D L HC CB × 1x 2x, empty | search box, list, table, button |
| W24 | window | Batch VO import | `Views/BatchVoImportDialog.axaml` | Test › Batch import VO | seen in code only | — | table, button |
| W25 | window | Git conflict resolution | `Views/GitConflictResolutionWindow.axaml` | merge conflict | seen in code only | — | diff view, button |
| W26 | window | Exception report | `Views/ExceptionReportWindow.axaml` | unhandled crash | seen in code only | — | text, button |

### Editor: dialogs

| id | kind | name | code | how reached | observed | states visited | components |
|---|---|---|---|---|---|---|---|
| D1 | dialog | Unsaved changes | `Views/UnsavedChangesDialog.axaml` | Ctrl+W with edits | run | D L HC CB × 1x 2x | message, button row |
| D2 | dialog | Autosave restore | `Views/AutosaveRestoreDialog.axaml` | launch after a crash with edits | run | D L HC CB × 1x 2x | message, button row |
| D3 | dialog | Recent project missing | `Views/RecentProjectMissingDialog.axaml` | open a moved recent project | seen in code only | — | message, button row |
| D4 | dialog | Conversation name | `Views/ConversationNameDialog.axaml` | new conversation | seen in code only | — | text box, button row |
| D5 | dialog | Branch name | `Views/BranchNameDialog.axaml` | W12 › New… / Rename… | seen in code only | — | text box, button row |
| D6 | dialog | Commit consent | `Views/CommitConsentDialog.axaml` | first save in a git project | seen in code only | — | message, button row |
| D7 | dialog | Conflict resolution (merge projects) | `Views/ConflictResolutionDialog.axaml` | File › Merge projects | seen in code only | — | list, radio, button row |
| D8 | dialog | Force delete | `Views/ForceDeleteDialog.axaml` | W12 › Delete unmerged | seen in code only | — | message, button row |
| D9 | dialog | Import warnings | `Views/ImportWarningsDialog.axaml` | import with warnings | seen in code only | — | message, list, button |
| D10 | dialog | Language code | `Views/LanguageCodeDialog.axaml` | Import translation | seen in code only | — | text box, button row |
| D11 | dialog | Save before scan | `Views/SaveBeforeScanDialog.axaml` | W16 with unsaved edits | seen in code only | — | message, button row |
| D12 | dialog | Alias import confirm | `Views/AliasImportConfirmDialog.axaml` | W23 › use recording | seen in code only | — | message, button row |
| D13 | dialog | VO import | `Views/VoImportDialog.axaml` | S4 › Import VO (file picker first) | seen in code only | — | form, button row |
| D14 | dialog | Confirm (shared) | `Shared/ConfirmDialog.axaml` | patch manager confirmations | seen in code only | — | message, button row |

### Editor: menus and commands

| id | kind | name | code | how reached | observed | states visited | components |
|---|---|---|---|---|---|---|---|
| M1 | menu | File (16 items) | `MainWindow.axaml` `MenuFile*` | Alt+F / click | run | kbd (Alt+F), D 1x | menu item |
| M2 | menu | Edit (12 items) | `MainWindow.axaml` `MenuEdit*` | click | run | D 1x | menu item |
| M3 | menu | View (4 items) | `MainWindow.axaml` `MenuView*` | click | run | D 1x | menu item |
| M4 | menu | Test (7 items) | `MainWindow.axaml` `MenuTest*` | click | run | D 1x | menu item |
| M5 | menu | Help (8 items) | `MainWindow.axaml` `MenuHelp*` | click | run | D 1x | menu item |
| M6 | menu | Canvas / node / connection context menus | `ConversationView.axaml` | right-click, Apps, Shift+F10 | seen in code only | — | context menu |
| C1 | command | Keyboard shortcuts (Ctrl+N/O/S/W/Z/Y/F/H, Ctrl+Shift+F/H/O/S/B, F5, F6, F7, Ctrl+,) | `MainWindow.axaml.cs:492-583` | keyboard | run | noproj: Ctrl+S, F5, F6, Ctrl+, , Ctrl+H, Ctrl+Shift+F | — |
| C2 | command | Canvas keys (arrows, Home, PgUp/PgDn, Enter, Esc, Ctrl+arrows, Ctrl+L) | `ConversationView.axaml.cs:109-145` | keyboard on canvas | run | kbd, SR: Tab, Home, Right, Enter | — |

### Patcher (standalone) and CLI

| id | kind | name | code | how reached | observed | states visited | components |
|---|---|---|---|---|---|---|---|
| P1 | window | Patcher main window | `DialogEditor.PatchManager/MainWindow.axaml` + `Shared/PatchManagerView.axaml` | launch | run | L × 1x 2x (app setting ignored), empty (no folder) | toolbar button, text box, button, list, link |
| P2 | window | Patcher settings | `PatchManager/PatchManagerSettingsWindow.axaml` | P1 › Settings… | run | L × 1x 2x | combo box, button |
| P3 | window | Patcher about | `PatchManager/PatchManagerAboutWindow.axaml` | P1 › About… | run | L × 1x | text, link button |
| P4 | dialog | Patcher confirmations | `Shared/PatchManagerDialogs.cs` | Apply / Remove all | seen in code only | — | message, button row |
| CL1 | command | `dialog-patcher` | `DialogEditor.PatchCli/PatcherCommand.cs` | terminal | run | `--help`, `-h`, `--version`, no args, `--restore` alone, missing dir, missing project, unknown flag, typo flag, `--dry-run`, `-v`, `-q -v`, `--restore`, piped, `NO_COLOR` | help text, error message, progress output |

### Flows

| id | kind | name | passes through | observed |
|---|---|---|---|---|
| FL1 | flow | First launch | S9 → S1 (empty, no game folder) | run |
| FL2 | flow | Reader reads a conversation | S1 → S2 filter → S3 → S4 (noproj) | run |
| FL3 | flow | Reader reads by keyboard / screen reader | S2 → Tab → S3 → Home/arrows → Enter → S4 | run |
| FL4 | flow | Modder edits and saves | S3 → S4 → Ctrl+S → D1 on close | run (with project) |
| FL5 | flow | Player installs a mod (GUI) | P1 → Browse… (file picker) → Add mods… → Apply | seen in code only (file pickers) |
| FL6 | flow | Player installs a mod (CLI) | CL1 `--dry-run` → apply → `--restore` | run (scratch copy) |

## Components

| component type | used in |
|---|---|
| menu bar / menu item | S1, M1–M5 |
| context menu | M6 |
| dock pane header (options, pin, close glyph buttons) | S2, S3, S4, S5 |
| dock tab | S3, S4, S5 |
| icon button (glyph only) | S1 (⚙, ?), S2 (×), S3 (+, −, ⌂, ×), S4 (×, ↑, ↓), W21 |
| text box / search box | S2, S3, S4, S5, W2, W3, W4, W17, W21, W22, W23, D4, D5, D10, P1 |
| combo box | S1, S4, W1, W5, W6, W7, W8, W16, W17, W21, S9, P2 |
| check box | S2, S4, W2, W3, W4, W6, W8, W14, W16, W21 |
| tree view | S2 |
| results table / list | W3, W4, W6, W7, W8, W10, W11, W12, W15, W16, W23 |
| node card | S3 |
| connection (link line) | S3, W20 |
| connector (node input/output port) | S3 |
| minimap | S3 |
| expander (Identity / Display / Voice / Logic / Notes) | S4 |
| dialog button row | D1–D14, W1, W14, W21, W22, P2, P3 |
| status bar | S1 |
| focus hint bar | S9, W1, W2, W3, W14, W15, W19, P2, P3 |
| empty-state message | S4 ("Select a node…"), W8, W13/P1, W16 |
| step bar (guided tour) | S7 |
| help text (CLI) | CL1 |
| error message (CLI) | CL1 |
| progress output (CLI) | CL1 |
