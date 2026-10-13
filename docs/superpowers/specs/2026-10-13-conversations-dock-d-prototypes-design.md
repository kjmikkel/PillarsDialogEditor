# Conversations dock, part D: prototype and test content (issue 85)

Part D of the #85 split. The user asked for A–D to proceed without check-ins, so the decisions
below are mine; each says why, so it can be overturned in review.

## Problem (#85 finding 5)

The games ship developer leftovers, and the dock lists them alongside real content:

- **PoE2:** the `00_prototype` folder, sorted first because of its `00_` prefix, so it's the first
  folder a writer sees. Also top-level conversations `si_test`, `test_cv_zuhira_stub`,
  `cv_test_pet_sac` and `si_random_encounter_test`, which part B now shows at the top level.
- **PoE1:** the `prototype_2` and `test` folders.

## Decisions

| # | Decision | Why |
|---|---|---|
| D1 | **Group them, don't hide them.** One folder, "Prototype and test content", at the end of the tree and collapsed. It holds the prototype and test folders as subfolders, and the test conversations directly. | Hiding content of a game a writer is modding is worse than showing it: they may need it, and search must still find it. A collapsed group at the end keeps it out of the way. The filter and "Only this project's conversations" work inside it as anywhere else. A toggle was weighed and rejected: it's another control in a narrow dock for a few rows. |
| D2 | **What counts.** A *top-level folder* whose name, after the order prefix, is `prototype`, `prototype_<digits>` or `test`. A *top-level conversation* whose name has `test` as a whole underscore-separated word (`si_test`, `test_cv_zuhira_stub`, `cv_test_pet_sac`). Nothing inside a real folder moves. | The rule is narrow on purpose. Moving a conversation out of its location folder would hide where it lives, and a word match avoids catching names like `contest` or `testament`. |
| D3 | **The group's tooltip says what it holds:** "Developer prototypes and tests that ship with the game. Kept here so they don't crowd the real content." | A virtual folder has no raw path to show, so the tooltip explains it instead. |
| D4 | **Order:** base-game folders, then the DLC groups, then this group, then any remaining top-level conversations. | The group is a folder, so it goes before top-level conversations, as all folders do, and last among folders. |

## Shape

- **`ConversationTreeBuilder.Build`:**
  - after nesting and DLC grouping, it moves the matching top-level folders and conversations into a group folder (`ConversationTreeBuilder.PrototypesPath`, a virtual path with `*`), added last among the folders;
  - the group is created only when something matches.
- **`ConversationFolderViewModel`:** an optional `toolTip` override, used by the group.
- **Strings:** `Browser_PrototypesFolder` ("Prototype and test content") and
  `ToolTip_PrototypesFolder`.

## Testing (TDD)

`ConversationTreeBuilderTests`:
- PoE2's `00_prototype`, and PoE1's `prototype_2` and `test`, go into the group, which comes last among the folders, after the DLC groups;
- the matching top-level conversations go in, the others stay at the top level;
- `contest_scene` and a `test` conversation inside `companions` stay where they are;
- no group is created when nothing matches;
- the group is collapsed, and its tooltip is the explanation.

## Verification

Full headless suite; captures with the rest of #85 once the desktop is available.
