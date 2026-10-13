# Conversations dock, part B: names and structure (issue 85)

Part B of the #85 split (see the part A spec). The user asked for A–D to proceed without
check-ins, so the decisions below are mine; each says why, so it can be overturned in review.

## Problems (#85 findings 3, 4, 8)

- **Nested folders appear flat, with a raw backslash path:** `07_neketaka_temple_district\07_animancer_one_offs`,
  `companions\poi`, and `re_scripted_interactions\bounty encounters`. They sit beside their parent
  instead of inside it.
- **Folder names are raw file-system names:** `07_neketaka_temple_district`, `00_prototype`.
- **Labels say little:** `(root)` doesn't say what it holds, and `(new)` is terse.
- **PoE1's White March content is interleaved under raw prefixes:** `px1_00_stalwart_village`,
  `px2_03_abbey` and the rest sort among the base game's `00_dyrwood` … `15_yenwood`.

These are the real folders, from the GOG installs.

- **PoE1:**
  - base game: `00_dyrwood` … `15_yenwood`, `companions`, `companions/poi`;
  - The White March: `px1_*`, `px2_*`, including `px1_companions/poi` and `px2_companions/poi`;
  - other: `px4`, `prototype_2`, `test`.
- **PoE2:**
  - base game: `00_prototype` … `28_drowned_barrows`, `07_neketaka_temple_district/07_animancer_one_offs`,
    `companions/poi`, `crew`, `generic`;
  - unclear: `lax02_01_iceberg_dungeon`, `laxi`;
  - `re_scripted_interactions/{bounty encounters, ghost_ship_encounter, takuro_encounters}`.

## Decisions

| # | Decision | Why |
|---|---|---|
| B1 | **Real nesting.** A folder path's segments become nested folders. A missing parent is created, and a folder lists its subfolders before its conversations. | That's how a file tree reads, and it removes the raw `a\b` paths. |
| B2 | **Folder names are tidied for display.** The rule (`FolderDisplayName`): drop a leading run of digits plus underscore (`07_`), turn underscores into spaces, and capitalise each word. `07_neketaka_temple_district` → "Neketaka Temple District"; `bounty encounters` → "Bounty Encounters". | Easier to scan. The rule is generic, so oddities like `lax02_01_iceberg_dungeon` → "Lax02 01 Iceberg Dungeon" stay readable, and the raw name is one hover away. |
| B3 | **Folders keep their raw order.** They sort by raw name (ordinal), so the game's numbering (02, 03 … 28) still orders them after the prefixes are hidden. | The numbers encode the game's geography and act order. Hiding them shouldn't scramble it. |
| B4 | **The raw folder path is the folder's tooltip.** | Writers who know the file layout can still see it. |
| B5 | **Conversation names stay raw** (`00_bs_companion_broodmother`). | They are identifiers: they appear in other windows, in scripts, in file names, and in search. Their prefixes (`bs_`, `cv_`) carry meaning. |
| B6 | **Conversations in the game's top conversations folder appear at the top level of the tree, after the folders.** There is no "(root)" folder. | It's how a file tree shows files at its root, and it removes a label that said nothing. |
| B7 | **The new-conversations folder is called "New conversations"**, stays first and stays expanded. | A plain label instead of "(new)". |
| B8 | **PoE1 DLC is grouped.** Top-level `px1_*` folders go under a "The White March – Part I" folder, and `px2_*` under "The White March – Part II". The groups come after the base game's folders, and each subfolder drops the `px1_`/`px2_` prefix before B2 (`px1_00_stalwart_village` → "Stalwart Village"). | It separates the expansion from the base game the way players know it. PoE2 has no DLC prefix I can map with confidence (`lax02_`, `laxi`, `px4`), so those are only tidied, not grouped or labelled. A wrong label would be worse than none. |
| B9 | **The filter keeps the nesting.** A folder is shown if any conversation in it or below it matches, and every shown folder is expanded. As today, the filter matches conversation names. | A match deep in a nested folder must still be visible without manual expanding. |
| B10 | **Expand all and Collapse all reach nested folders.** | Today they only touch top-level folders. |
| B11 | **The row's accessible (UIA) name is its display name**, as today. Conversations keep their raw names, so automation that finds a conversation by name (`Open-BrowserConversation`) is unaffected. | It matches what is on screen. |

Prototype and test content (`00_prototype`, `prototype_2`, `test`) is left alone here; that's part D.

## Shape

- **`FolderDisplayName.For(string segment)`:** a pure static helper in DialogEditor.ViewModels.
- **`ConversationFolderViewModel`:**
  - gains `Subfolders` (`ObservableCollection<ConversationFolderViewModel>`);
  - gains `Children` (subfolders, then items) for the tree;
  - gains an optional display-name override (used for the New conversations folder and the DLC groups);
  - `FolderPath` stays the raw full path.
- **`ConversationTreeBuilder.Build(files)`:** a pure static helper that returns the top-level folders and the top-level conversations.
- **`GameBrowserViewModel`:**
  - keeps `Folders`, the top-level folders;
  - gains `RootItems`;
  - `FilteredFolders` filters recursively;
  - a new `FilteredRoots` (folders, then root conversations) is what the tree binds to;
  - Expand all and Collapse all recurse.
- **Strings:**
  - `Browser_RootFolder` is removed;
  - `Label_NewConversationsFolder` becomes "New conversations";
  - new: `Browser_Dlc_WhiteMarch1` and `Browser_Dlc_WhiteMarch2`.

## Testing (TDD)

- **`FolderDisplayNameTests`:** the examples above, plus an empty segment, a name that's only digits, and an existing space.
- **`ConversationTreeBuilderTests`:**
  - nesting, including a missing parent;
  - raw-name ordering;
  - top-level conversations;
  - DLC grouping and prefix stripping;
  - no grouping for `lax02_` or `px4`.
- **`GameBrowserViewModelTests`:**
  - a nested match is kept, with its ancestors expanded;
  - root conversations are filtered;
  - Expand all and Collapse all reach nested folders;
  - the New conversations folder comes first.
- **View (headless):** a nested folder renders inside its parent; the folder tooltip is the raw path.

## Verification

Full headless suite. Dock captures for PoE2 (default) and PoE1, with folders collapsed and
expanded. #85's acceptance needs the PoE1 DLC grouping visible in a capture.
