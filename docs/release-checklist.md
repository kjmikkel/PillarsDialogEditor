# Release checklist

The steps for publishing a release of either product. Copy the relevant list into the release's
tracking issue and tick it off there.

The two products are released independently (see [building.md](building.md)):

| Product | Version file | Tag | Assets on the release |
|---|---|---|---|
| Pillars Dialog Editor | `VERSION` | `v<ver>` | `PillarsDialogEditor-<ver>.zip`, `PillarsDialogEditor-<ver>-src.zip`, `SHA256SUMS.txt` |
| Pillars Dialog Patcher | `PATCHER_VERSION` | `patcher-v<ver>` | `PillarsDialogPatcher-<ver>.zip`, `PillarsDialogPatcher-<ver>-src.zip`, `SHA256SUMS.txt` |

Pushing a tag runs `.github/workflows/release.yml`: the test gate, `build.ps1` for that product,
then a **draft** GitHub Release with the three assets. Nothing is public until you press
**Publish**, so the smoke test below runs on the draft's own zips.

---

## First public release only

Do these once, with the first editor release (1.0.0):

- [ ] **Un-freeze the changelog.** Remove the "Changelog" rule from `CLAUDE.md` (it says to
      remove itself at the initial release), and replace the `[Unreleased]` placeholder text in
      `CHANGELOG.md` with the real entry (see *Write the changelog entry* below).
- [ ] **What's New on a fresh install.** With no `settings.json`, start the release build: it
      should record the version silently and show no "What's new" window. Start it again: still
      nothing. (A later update is what shows the window.)
- [ ] **README links.** Once the Nexus pages exist (#73, #81), add their URLs to the README's
      Get started section and to `docs/nexus/*.md`.

---

## Editor release (`v<ver>`)

### Before tagging

- [ ] Every issue in the version's milestone is closed or moved to a later milestone.
- [ ] `VERSION` holds `<ver>`, committed on `main`.
- [ ] CI on `main` is green.
- [ ] **Game-data tests** pass against a real PoE1 and a real Deadfire install. They are opt-in
      and CI never runs them ([game-data-tests.md](game-data-tests.md)):

      ```powershell
      $env:DIALOGEDITOR_POE1_DIR = '<PoE1 install>'
      $env:DIALOGEDITOR_POE2_DIR = '<Deadfire install>'
      dotnet test DialogEditor.Tests --filter "Category=GameData"
      ```

      All must pass; none may be skipped.
- [ ] **GUI tests** pass on a desktop you aren't using for anything else:
      `dotnet test DialogEditor.slnx --filter "Category=Gui"`.
- [ ] If anything that writes game files changed since the last release, run the
      [canonical mod checklist](canonical-mod-checklist.md) in both games.

### Write the changelog entry

- [ ] Add a section to `CHANGELOG.md` above the previous release. The in-app Changelog and
      What's New windows parse it, so keep exactly this shape:

      ```markdown
      ## [1.0.0] — 2026-11-01

      ### Added
      - One line per change, written for users, not developers.

      ### Fixed
      - ...
      ```

      `## [<ver>] — <yyyy-mm-dd>`, then `###` group headings (Added, Changed, Fixed, Removed),
      then `-` bullets. Other Markdown is ignored by the parser.
- [ ] Open **Help › Changelog…** in a local build and check the entry reads correctly.

### Tag and review the draft

- [ ] Tag and push:

      ```powershell
      git tag v<ver>
      git push origin v<ver>
      ```

- [ ] The Release workflow finished green. If it failed on the version check, the tag doesn't
      match `VERSION`: delete the tag, fix, and tag again.
- [ ] On the draft release:
  - [ ] the title is "Pillars Dialog Editor v<ver>";
  - [ ] the notes start with the non-affiliation notice (from `.github/release-notice.md`),
        followed by the generated list of changes;
  - [ ] the three assets above are attached;
  - [ ] `SHA256SUMS.txt` matches the zips you download from the draft:
        `Get-FileHash <zip> -Algorithm SHA256`.
- [ ] Run the [clean-machine smoke test](#clean-machine-smoke-test) on the draft's editor zip.

### Publish

- [ ] Press **Publish** on the draft.
- [ ] Update the editor's Nexus pages, per [nexus/editor.md](nexus/editor.md):
      upload the zip as the new main file, move the previous one to *Old versions*, set the
      version, and add a changelog entry linking the GitHub release.
- [ ] For a major or minor release, post the announcement from [nexus/editor.md](nexus/editor.md#release-announcement).

---

## Patcher release (`patcher-v<ver>`)

The patcher only gets a new version when code that applies patches changes.

### Before tagging

- [ ] Every issue in the patcher milestone is closed or moved.
- [ ] `PATCHER_VERSION` holds `<ver>`, committed on `main`; CI on `main` is green.
- [ ] Game-data tests and, if anything that writes game files changed, the canonical mod
      checklist, as for the editor.
- [ ] If the file formats it reads changed, `FORMAT.md` and `PROJECT-FORMAT.md` say so.

### Tag and review the draft

- [ ] `git tag patcher-v<ver>` and `git push origin patcher-v<ver>`; the workflow is green.
- [ ] The draft is titled "Pillars Dialog Patcher patcher-v<ver>", starts with the
      non-affiliation notice, has the three assets, and `SHA256SUMS.txt` matches them.
- [ ] Run the [clean-machine smoke test](#clean-machine-smoke-test) on the draft's patcher zip.

### Publish

- [ ] Press **Publish** on the draft.
- [ ] On **both** patcher Nexus pages (PoE1 and Deadfire), per [nexus/patcher.md](nexus/patcher.md):
  - [ ] upload `PillarsDialogPatcher-<ver>.zip` as the new main file and move the previous
        one to *Old versions*;
  - [ ] set the page's version to `<ver>`;
  - [ ] add a changelog entry linking the GitHub release;
  - [ ] if the Patch Manager window changed, retake the screenshots and replace the images.

---

## Clean-machine smoke test

Development machines have the .NET SDK, Git and existing settings. Most players have none of
them. Run this on a fresh **Windows 10** and a fresh **Windows 11** virtual machine with no
.NET, no Git, no Visual C++ redistributable and no `%LOCALAPPDATA%\PillarsDialogEditor`
folder, using the zips from the draft release. Copy a game install onto the VM, or attach one
as a shared folder.

### Editor zip

- [ ] Unzip anywhere and start `DialogEditor.Avalonia.exe`. It starts with no missing-DLL or
      .NET prompt.
- [ ] First run: the theme picker appears, then the guided tour.
- [ ] **File › Open Game Folder…** on the game install: the backup-folder setup appears, and the
      backup completes.
- [ ] **Help › Create Sample Project…** into an empty folder: the sample opens.
- [ ] **Help › Open Walkthrough…** opens the walkthrough.
- [ ] With no Git installed, Compare, History, Attribution and Branches show the
      "Git not installed" message instead of failing.
- [ ] Deadfire only: VO preview plays a line (uses the bundled `vgmstream-cli.exe`), and there
      is sound.
- [ ] Text renders in every UI font, with no missing-glyph boxes.
- [ ] **Test › Test Patch** (F5) and **Test › Restore Conversation** on the sample.
- [ ] `%LOCALAPPDATA%\PillarsDialogEditor\app.log` shows no errors from these steps.

### Patcher zip

- [ ] Unzip anywhere. The top level holds `DialogEditor.PatchManager.exe`, the player README,
      `cli\` and `runtime\`.
- [ ] Start `DialogEditor.PatchManager.exe`. It starts with no .NET prompt.
- [ ] Choose the game folder, add a `.dialogpack` (for Deadfire, use one with VO), and apply.
      The mod's lines show in game.
- [ ] **Remove all mods** restores the original files.
- [ ] Double-clicking `cli\dialog-patcher.exe` shows the note pointing to the Patch Manager and
      waits for a key.
- [ ] From a terminal, in `cli\`:
  - [ ] `dialog-patcher --version` prints the version;
  - [ ] `dialog-patcher "<game folder>" <mod>.dialogpack --dry-run` lists files and changes
        nothing;
  - [ ] applying the mod, then `dialog-patcher "<game folder>" --restore`, leaves the game
        files as they were.

File anything that fails as an issue, and fix it before publishing.
