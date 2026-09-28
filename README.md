# Pillars Dialog Editor

A dialogue editing tool for *Pillars of Eternity* and *Pillars of Eternity II: Deadfire*.
Edits are stored as semantic diff files (`.dialogproject`) rather than modifying the
original game files directly, making the workflow safe, reversible, and shareable.

> **Unofficial fan tool.** Pillars Dialog Editor and Pillars Dialog Patcher are free,
> fan-made tools. They are not made, endorsed or supported by Obsidian Entertainment or the
> games' publishers. *Pillars of Eternity* and *Pillars of Eternity II: Deadfire* are
> trademarks of their respective owners. This project contains no game files: you need
> your own copy of the game.

---

## Prerequisites

The release archives (`PillarsDialogEditor-<ver>.zip` and
`PillarsDialogPatcher-<ver>.zip`) are self-contained — no .NET installation is
needed to run them.

- A PoE1 or PoE2 installation
- **Git** *(optional)* — required only for the built-in version-control features
  (Compare, History, Attribution, Branches). See
  [Git-powered features](#git-powered-features). Everything else works without it.

Building from source instead requires the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). See
[Building for distribution](#building-for-distribution).

---

## Supported game builds

Both games have had their last patch, so "the latest build" is a fixed target. These are
the builds the editor and patcher have been checked against:

| Game | Storefront | Build checked | DLC installed | Read (open, browse, all languages) | Write (Test Patch, patcher) |
|---|---|---|---|---|---|
| Pillars of Eternity | GOG | build 59799714601848948 | The White March 1 + 2 | ✅ 1,434 conversations, 8 languages | ❌ see known issues |
| Pillars of Eternity | Steam | build 23036766 | The White March 1 + 2 | ✅ identical data to GOG | ❌ see known issues |
| Pillars of Eternity (Definitive Edition) | Epic | version 3.9.5.89801 | included | ✅ identical data to GOG | ❌ see known issues |
| Pillars of Eternity II: Deadfire | GOG | build 52232433653243275 | all three expansions + free DLC | ✅ 1,130 conversations, 10 languages | ❌ see known issues |
| Pillars of Eternity II: Deadfire | Steam | build 12181035 | all three expansions + free DLC | ✅ identical data to GOG | ❌ see known issues |

*Checked 2026-09-28 ([#65](https://github.com/kjmikkel/PillarsDialogEditor/issues/65)).*

Every storefront ships byte-identical conversation and text files for the same game, so what holds
for one build holds for all of them. The Microsoft Store / Game Pass builds haven't been checked.

**Known issues blocking writes.** Saving a conversation (which is what Test Patch, `dialog-patcher`
and the Patch Manager all do) currently changes game data the editor doesn't display. Until these
are fixed, don't use either tool to write to a game you care about:
[#111](https://github.com/kjmikkel/PillarsDialogEditor/issues/111), [#112](https://github.com/kjmikkel/PillarsDialogEditor/issues/112), [#113](https://github.com/kjmikkel/PillarsDialogEditor/issues/113) (PoE1); [#113](https://github.com/kjmikkel/PillarsDialogEditor/issues/113), [#114](https://github.com/kjmikkel/PillarsDialogEditor/issues/114), [#115](https://github.com/kjmikkel/PillarsDialogEditor/issues/115), [#116](https://github.com/kjmikkel/PillarsDialogEditor/issues/116) (PoE2). This section will be updated when they're fixed.

**DLC.** Nothing in the editor or patcher depends on which DLC you own. Conversations are found by
walking the game's data folders, so an install without DLC simply has fewer of them. A mod that
edits a DLC conversation is skipped on an install without that DLC, with a "conversation not found"
warning. Nothing else is written or broken.

**Game updates and your backups.**

- **Patcher (`dialog-patcher`, Patch Manager).** Suppose the game or the storefront's "verify files"
  replaces a file a mod changed. **Remove all mods / Restore** then leaves that file alone and tells
  you. Reinstalling stops and asks before treating the new file as the original
  (`--accept-current-files`). Your update is never overwritten with an older copy.
- **Test Patch (F5) / Restore Conversation (F6).** F5 backs up the files it's about to change, so
  the backup is always fresh. Restore with F6 before updating or verifying the game. F6 currently
  leaves `.bak` files next to the restored game files
  ([#121](https://github.com/kjmikkel/PillarsDialogEditor/issues/121)). The game ignores them, and
  you can delete them.
- **Restore Full Backup (Ctrl+Shift+B).** This restores the snapshot taken the first time you opened
  the game folder, and can put back pre-update files after a game update ([#118](https://github.com/kjmikkel/PillarsDialogEditor/issues/118)). Don't use it
  after the game has been updated or verified. Use the storefront's "verify files" instead.

**Unreadable files.** If one language's text file for a conversation is damaged, that
conversation won't open in that language ([#119](https://github.com/kjmikkel/PillarsDialogEditor/issues/119)). Other languages are unaffected. Use the
storefront's "verify files" to repair it.

---

## Third-party software

The editor bundles `vgmstream-cli.exe` for PoE2 voice-over audio preview.
Full copyright notices and license terms are in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

---

## Workflow

### 1 — Open your game folder

**File › Open Game Folder** (`Ctrl+Shift+O`)

Select the root directory of your PoE1 or PoE2 installation. The conversation
browser populates in the left panel. This choice is remembered between sessions.

**First-time setup:** The first time you open a game folder, the editor will ask
you to choose a backup destination. It then copies the original conversation and
string-table files to that location. This backup is the safety net for
**Test › Restore Backup** and does not need to be repeated unless you want to
update it.

---

### 2 — Open or create a project

**File › New Project** (`Ctrl+N`) or **File › Open Project** (`Ctrl+O`)

A project file (`.dialogproject`) is the home for all your changes. It stores your
edits as a compact JSON diff against the original game files — nothing in the game
directory is touched until you explicitly test or apply the project.

A project can contain patches for **any number of conversations**. Work on as many
as you like; they are all saved into the same file and applied together at test time.

> **Browsing works without a project open.** You can navigate and read any
> conversation freely. Editing requires an open project. The status bar will
> remind you when you are in read-only mode.

---

### 3 — Browse and edit

Click any conversation in the browser to open it on the canvas. To start a brand-new
conversation that doesn't exist yet in the game folder, click the **⊕** button in the
browser header — a name dialog opens, and the new entry appears in a **(new)** folder
shown in green. The conversation file is **not** written to disk until you press
`F5`; `F6` deletes it again, leaving the project entry intact for the next test cycle.

| Action | How |
|--------|-----|
| Select a node | Click it on the canvas |
| Add a node | Double-click the canvas background |
| Add a connected node | Right-click a node › Add connected node |
| Connect two nodes | Drag from a node's output port to another node's input port |
| Delete a node | Select it, press `Delete`; or right-click › Delete node |
| Delete a connection | Right-click the connection line › Delete connection |
| Search nodes | `Ctrl+F`, or type in the search box above the canvas |
| Find / Replace text | `Ctrl+H` — searches DefaultText and FemaleText across all nodes |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` — all edits are fully undoable within a session |

#### Node detail panel

Selecting a node opens the detail panel on the right. Fields available for editing:

| Field | Description |
|-------|-------------|
| Default / Male text | The dialogue line shown to all players, or the male variant in gendered games |
| Female text | Optional female-voice override; leave blank to use the default text for both genders |
| Type | NPC Line or Player Choice |
| Speaker category | NPC, Player, Narrator, or Script — controls the node's colour on the canvas and the `xsi:type` / `$type` written to the game file |
| Speaker / Listener GUID | The characters involved; in PoE2 a name picker is available |
| Display type | Conversation (full portrait) or Bark (floating text) |
| Persistence | OnceEver hides this node after it has been shown once |
| Actor direction | Internal note for the voice actor |
| External VO | Voice-over file path or identifier |
| Comments | Internal developer notes — not shown to players |
| Translator note | Context for translators: tone, character mood, length constraints, etc. Stored in the project file and included in export files; never written to game data. Can be set on any node, including unmodified vanilla nodes. |

#### Conditions

Each node can carry a list of conditions that the engine evaluates at runtime.
Click **Edit…** next to the CONDITIONS header to open the Condition Editor.

- Pick from a searchable catalogue of 166 known conditions (game-appropriate
  filtering when a game folder is loaded; parameters use enum dropdowns or
  Guid fields as appropriate for each game).
- Toggle **AND/OR** and **NOT** per condition, including on grouped branches.
- **Link conditions:** the **⚙** button on any link row opens the Condition
  Editor for that specific connection.
- **Grouped conditions:** existing groups show an **Edit group…** button that
  opens a nested Condition Editor; **+ New group** creates an empty nested group
  from scratch.
- Use ↑ ↓ and ✕ to reorder and remove; drag the editor to any monitor.

#### Scripts

Nodes can fire scripts at three lifecycle points. Click **Edit…** next to the
LOGIC header to open the Script Editor.

Three sections — **On Enter** (node shown), **On Exit** (player leaves),
**On Update** (every tick while active).

- Search the catalogue of 37 known scripts by typing, or press the down arrow
  to browse the full list. Selecting a known script pre-populates all parameters
  with named fields, correct types (enum dropdowns with game source values), and
  `AutoCompleteBox` filtering for large lists such as the 195-entry PoE1 or
  322-entry PoE2 area dropdowns on `AreaTransition` and `PreloadScene`.
- PoE1 and PoE2 variants of the same script (different parameter types) are
  shown separately and filtered to the loaded game.
- Unknown scripts can still be added by typing the C# reflection FullName
  directly, e.g. `Void SetGlobalValue(String, Int32)`.

---

### 4 — Save to project

**File › Save Project** (`Ctrl+S`)

Records the current conversation's changes as a diff inside the open project file.
The diff captures only what changed — text edits, node additions/removals, link
changes, condition and script modifications. Game files are not modified.
The project file is plain JSON and can be version-controlled or shared freely.

---

### 5 — Translate your mod

If your mod changes dialogue text, you can produce translated versions for other languages without writing separate patches. All translated text is stored inside the same `.dialogproject` file and written to the correct game stringtable paths when the patch is applied.

#### Export for translation

**File › Export for Translation…**

Opens a save dialog followed by a language-code prompt (pre-filled with your current game language). Writes a file containing every edited line alongside writer-comment context columns for the translator.

Three formats are supported — choose in **Settings**:

| Format | Extension | Notes |
|--------|-----------|-------|
| CSV | `.csv` | RFC 4180; opens in any spreadsheet |
| JSON | `.json` | Structured; easy to diff in version control |
| XLIFF 1.2 | `.xlf` | Industry-standard CAT-tool format |

Each row/entry has: conversation name, node ID, writer comment (from the **Translator note** field on the node), source default text, source female text, and empty *translated default / female* columns for the translator to fill in.

If a language already has partial translations in the project, those are pre-populated so re-exporting never discards prior work.

#### Import translations

**File › Import Translation…**

Opens a file picker (`.csv`, `.json`, `.xlf`/`.xliff` accepted) followed by a language-code prompt. Rows where both translated columns are empty are skipped. The translations are stored in the project under that language code — **save the project afterwards** to persist them.

#### Applying multi-language patches

`F5` (Test Patch) and the `dialog-patcher` CLI both write stringtables for **every language present in the patch** that is also installed on the player's machine. If a player has the French localisation installed and your project contains a `"fr"` entry, the French stringtable is updated automatically.

---

### 6 — Test in-game

**Test › Test Patch** (`F5`)

Applies **every conversation patch in the project** to the live game files so you
can launch the game and verify dialogue in context. Before writing anything, the
editor makes a lightweight per-file backup of the affected conversations so they
can be precisely restored afterwards.

New conversations (created with the **⊕** button and not yet on disk) are written
as blank templates at this point, then patched normally.

While the game files are patched, a modal dialog blocks the editor — this is
intentional to prevent inconsistent edits during testing.

**Conflict detection:** If a game file has changed since your patch was created
(e.g. after a game update), the editor shows a conflict dialog with the affected
node, field, and the expected vs. actual values. You can choose **Force Apply**
to apply your patch's target value anyway, or **Cancel Test** to leave game files
unchanged.

---

### 7 — Restore after testing

**Test › Restore Conversation** (`F6`)

Reverts the patched game files to their pre-test state and unlocks the editor.
New conversations that were written by `F5` are deleted again.
The project file is unchanged; your edits are ready for the next test cycle.

If the editor crashes or is force-quit while in test mode, the next launch detects
the incomplete state and shows the modal automatically so you can restore before
editing.

---

### 8 — Distribute and combine patches

#### Releasing a mod

**File › Export Mod Bundle…** packs the project into one `.dialogpack` file, with its
voice-over files if it has any. Ship that file to players, not the `.dialogproject`: it
is the file the [player guide](docs/patcher/README.md) tells them to add.

Players install `.dialogpack` files with **Pillars Dialog Patcher**, the Patch Manager
and `dialog-patcher` CLI on their own, released as `patcher-v*` on the
[releases page](https://github.com/kjmikkel/PillarsDialogEditor/releases). They don't
need the editor. When you publish on Nexus Mods, add Pillars Dialog Patcher as a
**requirement** on your mod page (*Edit mod → Requirements → Nexus requirements*), so the
page tells players to install it and links to it. For a script or installer, use the
[`dialog-patcher` CLI](#dialog-patcher-cli).

#### Combining patches from multiple authors

**File › Merge Projects…**

Merges one or more other `.dialogproject` files into the currently open project.
Patches for the same conversation are merged field-by-field; the last file you
pick wins on any contested field. Layouts are merged preserving both sets of
positions. The merged file is saved immediately.

#### Patch Manager — load order and conflict detection

**File › Patch Manager…** (also the standalone `DialogEditor.PatchManager.exe` in
Pillars Dialog Patcher)

Lets you maintain an ordered stack of mods, detect conflicts before applying, and
write all patches to the game folder in one step. Use it to check your mod against
other mods before you release it:

1. **Add mods…** — pick one or more `.dialogpack` or `.dialogproject` files
2. **Reorder** with ↑ ↓ — entries lower in the list win on any conflict
3. The **conflict panel** identifies every contested field before you apply
4. **Apply patches** — writes every conversation patch to the selected game folder
5. **Save / Load load order** — persists the list as a `.patchlist` file;
   double-clicking a `.patchlist` opens the standalone app directly

Players see the same window in the standalone patcher. What it looks like to them is
in the [player guide](docs/patcher/README.md).

---

## Collaborating with Git

Because a `.dialogproject` is plain JSON, it lives comfortably in a Git
repository — branch, review, and share it like source code. The one rough edge of
text-based version control is the *merge conflict*: when two branches edit the same
project and you `git merge` or `git pull`, Git rewrites the file with
`<<<<<<<` / `=======` / `>>>>>>>` markers, which makes it invalid JSON.

The editor handles this for you. **Open Project** (`Ctrl+O`) on a conflicted file
detects the markers, reconstructs both sides, and opens a **Resolve Git Conflicts**
window instead of failing. No Git installation is required — it reads the markers
already in the file.

For each conflict you choose which side to keep:

| Conflict | What you see | Choices |
|----------|--------------|---------|
| Text / translation edit | The two versions side-by-side with the changed words highlighted, per language | Keep mine / Keep theirs |
| Other field edit (speaker, links, conditions, …) | The two field values, per field | Keep mine / Keep theirs |
| Both branches added the same node | The two node definitions | Keep mine / Keep theirs |
| One branch edited a node the other deleted | The edit vs. the deletion | Keep my edit / Accept deletion |

When every conflict has a choice, **Apply** builds the merged project and opens it
**unsaved**. Review it on the canvas, then **Save Project** (`Ctrl+S`) writes clean
JSON back to the file — after which a normal `git add` completes the merge. Cancelling
leaves the file untouched so you can fall back to your usual Git tooling.

If the project that was open when you last closed the editor has conflicts, startup
shows a status message with a **Resolve…** button rather than interrupting you with a
dialog — click it when you're ready.

> **Scope:** node text, fields, conditions/scripts, additions, and deletions are
> resolved in-app. Canvas layout positions and the list of not-yet-created
> conversations currently fall back to your side; resolve those in your Git tool if
> they matter. If either side of the conflict is too damaged to parse as JSON, the
> editor says so and asks you to resolve it in your Git tool first.

Everyone sharing a project should use the same editor version, or a newer one. A project saved by a newer version is refused rather than opened and silently truncated. Older projects open normally and are upgraded on the next save. See [PROJECT-FORMAT.md](PROJECT-FORMAT.md) for the file formats and the versioning policy.

### Git-powered features

Several optional tools read your project's Git history directly and therefore need
**Git installed** on your computer:

| Feature | What it does |
|---------|--------------|
| **Compare** | Diff your project against a branch or past commit and selectively bring changes in |
| **History** | Browse the project file's commit timeline and open any commit in Compare |
| **Attribution** | Show who last edited each node ("blame"), drawn from the Git history |
| **Branches** | Switch, create, rename, and delete local branches from inside the editor |

If Git isn't installed, these windows simply tell you so and stay empty — nothing
crashes, and the rest of the editor is unaffected. (Conflict resolution, above, is
the exception: it reads markers already written into the file and needs no Git.)

**What is Git?** Git is a free, widely used *version-control* program. It records a
project's history as a series of saved snapshots ("commits") so you can review what
changed, return to an earlier version, and collaborate without overwriting each
other's work. Because a `.dialogproject` is plain text, Git tracks it cleanly.

**Installing Git quickly:**

| OS | How |
|----|-----|
| **Windows** | `winget install --id Git.Git -e`, or download the installer from [git-scm.com/download/win](https://git-scm.com/download/win) |
| **macOS** | `brew install git` (Homebrew), or run `xcode-select --install` for Apple's bundled version |
| **Linux — Debian/Ubuntu** | `sudo apt install git` |
| **Linux — Fedora** | `sudo dnf install git` |
| **Linux — Arch** | `sudo pacman -S git` |

After installing, **restart the editor** so it detects Git on your `PATH`. To
confirm Git is available, run `git --version` in a terminal.

---

## dialog-patcher CLI

A lightweight command-line tool for scripted or automated patch application,
suitable for mod installers, CI pipelines, and power users.

```
dialog-patcher <game-dir> <project.dialogproject> [project2 ...] [options]
```

Multiple project files are merged in order before being applied (later project
wins on any contested field), matching the Patch Manager's load-order semantics.

| Option | Description |
|--------|-------------|
| `-f`, `--force` | Apply patches even when a field's baseline doesn't match — use after a game update |
| `-v`, `--verbose` | Print each conversation as it is patched |
| `-q`, `--quiet` | Suppress all output except errors; only the exit code signals success |
| `--dry-run` | List what would be patched without writing any files |
| `--version` | Print version and the newest file formats it reads, then exit (see [Patcher compatibility](PROJECT-FORMAT.md#patcher-compatibility)) |
| `-h`, `--help` | Show usage |

**Exit codes:** `0` success · `1` conflict (re-run with `--force`) · `2` error · `3` managed files were changed outside the patcher (re-run with `--accept-current-files` or `--restore`) · `4` a mod uses a newer file format than this patcher reads (nothing was changed; update `dialog-patcher`)

**Examples:**

```sh
# Apply a single mod
dialog-patcher "C:/GOG Games/Pillars of Eternity" my_mod.dialogproject

# Apply two mods in load order, verbose
dialog-patcher "C:/PoE2" base_mod.dialogproject override_mod.dialogproject --verbose

# Validate without touching game files
dialog-patcher "C:/PoE2" my_mod.dialogproject --dry-run

# Scripted install — check exit code
if dialog-patcher "$GAME_DIR" "$MOD_FILE" --quiet; then
    echo "Patched OK"
else
    echo "Patch failed" >&2
fi
```

---

## Building for distribution

The repo ships two products, versioned and released independently:

| Product | Version file | Release tag | Contains |
|---------|--------------|-------------|----------|
| Pillars Dialog Editor | `VERSION` | `v<ver>` | the editor |
| Pillars Dialog Patcher | `PATCHER_VERSION` | `patcher-v<ver>` | the Patch Manager GUI and the `dialog-patcher` CLI, for players installing mods |

The patcher only gets a new version when code that applies patches changes.
Whether it can read a mod made with a given editor depends on the file formats
it supports (see `FORMAT.md`), not on matching version numbers.

Three PowerShell scripts are provided at the repo root. Each takes
`-Product Editor|Patcher|All` (default `All`).

### build.ps1 — full release build (recommended)

Runs the test suite, then produces each selected product's binary and source
archives under `dist/`, plus `SHA256SUMS.txt` covering them all:

```powershell
.\build.ps1                          # both products, versions from the version files
.\build.ps1 -Product Patcher         # the patcher only
.\build.ps1 -Version 1.2.0           # override the editor version
.\build.ps1 -PatcherVersion 1.0.1    # override the patcher version
.\build.ps1 -SkipTests               # skip the test gate
```

Aborts immediately if any test fails. Cleans `dist/` before each run so old
files never accumulate.

### build-dist.ps1 — binary archives only

Publishes each app as a self-contained win-x64 executable (no .NET installation
required on the target machine) and zips it:

| Archive | Contents |
|---------|----------|
| `PillarsDialogEditor-<ver>.zip` | The editor |
| `PillarsDialogPatcher-<ver>.zip` | `DialogEditor.PatchManager.exe` and a player README at the top; the single-file `dialog-patcher.exe` in `cli\`; one shared .NET runtime in `runtime\` |

The two patcher apps share one bundled runtime rather than each carrying its
own: they are published framework-dependent with `AppHostDotNetSearch=AppRelative`,
so each exe looks for .NET *only* in `runtime\` next to it (never a system
install), and `build-dist.ps1` copies that runtime from the build machine's .NET
install and smoke-tests the staged CLI against it. The patcher zip keeps the
console tool in `cli\` so the only `.exe` a player sees at the top is the GUI. Double-clicking `dialog-patcher.exe` anyway shows a
note pointing to the Patch Manager and waits for a key; runs from a terminal or
script are unaffected. The player README is `docs/patcher/README.md`.

```powershell
.\build-dist.ps1
.\build-dist.ps1 -Product Patcher -PatcherVersion 1.0.1
```

### build-source.ps1 — source archives only

Copies the project folders needed to build each app (excluding `bin/`, `obj/`,
and editor artefacts), writes a trimmed `.slnx` solution file, and zips:

| Archive | Contents |
|---------|----------|
| `PillarsDialogEditor-<ver>-src.zip` | Editor source |
| `PillarsDialogPatcher-<ver>-src.zip` | Patch Manager and CLI source, one solution |

Each source archive also includes `README.md`, `VERSION`, `PATCHER_VERSION`, `LICENSE`,
`THIRD_PARTY_LICENSES.md`, `Directory.Packages.props`, and any repo file its
projects pull in from outside their own folders (the editor's `CHANGELOG.md`
and `docs/walkthrough.md`), so `dotnet build` on the extracted `.slnx` works
with no extra setup. CI checks this with `tools/ci/Test-SourceArchives.ps1`.

The archives do not include `DialogEditor.Tests`: it tests the whole repository
(it references the editor and the UI-automation tooling, and scans every
project's source), so no single-app subset can build or run it. Clone the
repository to run the test suite.

```powershell
.\build-source.ps1
.\build-source.ps1 -Product Editor -Version 1.2.0
```

### Versioning and releases

`VERSION` holds the editor's version and `PATCHER_VERSION` the patcher's. The
scripts read them when `-Version` / `-PatcherVersion` is not supplied, and stamp
them into the binaries: the editor's and Patch Manager's About windows and
`dialog-patcher --version` all report their own product's version.

Pushing a tag builds one product and attaches its zips and `SHA256SUMS.txt` to a
**draft** GitHub Release (`.github/workflows/release.yml`). The tag must match
that product's version file, or the workflow fails before building:

```powershell
git tag v1.2.0         ; git push origin v1.2.0           # editor
git tag patcher-v1.0.1 ; git push origin patcher-v1.0.1   # patcher
```

---

## Settings

**File › Settings** (`Ctrl+,`)

| Setting | Purpose |
|---------|---------|
| Backup directory | The folder chosen during first-time setup where the original game files are stored. Used for full disaster recovery via **Test › Restore Backup** (`Ctrl+Shift+B`). Can be changed here if you want to move the backup to a different location. |
| Default localization format | File format used by **File › Export for Translation…** — `Csv`, `Json`, or `Xliff`. All three formats are always selectable in the save dialog; this setting controls which is offered first. |

Settings are stored in:

```
%LOCALAPPDATA%\PillarsDialogEditor\settings.json
```

Deleting this file resets all settings to defaults (including first-run prompts such as the theme picker and the guided tour).
The log file (`app.log`) lives in the same folder.

---

## PoE1 vs. PoE2 — What's different

The editor supports both games, but the two were built with different technology and
store their data in different ways. This creates some visible differences — none of
them are bugs.

### Autocomplete and lookup dropdowns

When you edit a condition or script parameter, the editor can offer a searchable
dropdown populated from the game's own data files (Quests, Abilities, Items, Classes,
and so on). **PoE2 has far more of these dropdowns than PoE1.** Here's why and what
to expect in each game:

| Parameter type | PoE2 | PoE1 |
|----------------|------|------|
| Global variable name | Searchable list from game data | Searchable list from game data |
| Speaker / Listener | Searchable list built from loaded conversations | Searchable list built from loaded conversations |
| Quest, Item, Ability, Class, Race, Subrace, Background, Culture, Faction, Deity, Skill, Status effect, Weapon type, Map, Phrase, Keyword, Conversation | Searchable list from game data | **Type manually** (see note below) |
| Enum values (difficulty, disposition, attack type, …) | Dropdown | Dropdown |

**Why is PoE1's list shorter?**

PoE2 stores almost all of its game data in plain JSON files that sit alongside the
conversation files — the editor can read those files directly to build its
suggestion lists. PoE1 stores the same kind of data inside Unity binary asset
bundles (`.assets` files), which are a compiled, proprietary format. Decoding them
reliably requires Unity's own toolchain, which the editor does not bundle. The only
PoE1 data files that are plain text and readable without special tools are the
global-variables file and the speaker names embedded in the conversation files
themselves — so those are the only ones with live suggestions.

**What to do when there's no dropdown**

For parameters that require manual entry in PoE1 (Quest names, Item names, etc.)
you will need to type the internal string name exactly as it appears in the game
files. These strings follow a predictable naming convention — for example,
`07_qst_buried_secrets` for a quest or `NPC_Eder` for a character tag. You can
find the correct strings by opening the relevant `.conversation` files in the
editor and reading how the existing conditions are written, or by consulting the
[PoE modding wiki](https://pillarsofeternity.gamepedia.com/Modding).

### File formats

PoE1 conversations are stored as `.conversation` XML files. PoE2 conversations
use `.conversationbundle` JSON files. The editor reads and writes both; you never
need to handle the raw files yourself.

### Parameter types for the same condition

Several conditions exist in both games but accept different parameter types. For
example, `CheckFactionStanding` takes a plain string faction name in PoE1 and a
GUID in PoE2. The editor shows both variants in the catalogue and automatically
filters to the one that matches your loaded game, so you will never accidentally
add a PoE2-only condition to a PoE1 conversation.

---

## Keyboard Shortcuts

Press **`?`** in the editor to open the floating Legend window, which lists all
shortcuts alongside connection types, node colours, and canvas controls. The
Legend window can be moved outside the editor (useful on a second monitor).

| Key | Action |
|-----|--------|
| `Ctrl+N` | New project |
| `Ctrl+O` | Open project |
| `Ctrl+S` | Save project |
| `Ctrl+Shift+O` | Open game folder |
| `Ctrl+,` | Settings |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+F` | Focus node search |
| `Ctrl+H` | Find / Replace |
| `Delete` | Delete selected node |
| `F5` | Test Patch |
| `F6` | Restore Conversation |
| `Ctrl+Shift+B` | Restore Backup (all conversations) |
| `Escape` | Clear search / close browser flyout |
