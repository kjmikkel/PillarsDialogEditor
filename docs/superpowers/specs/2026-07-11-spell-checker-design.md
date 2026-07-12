# Spell Checker for Dialog Text — Design

**Date:** 2026-07-11
**Status:** Approved
**Gap:** `Gaps.md` › "Spell Checker for Dialog Text" (the three-layer dictionary design
recorded there is the user's and is adopted verbatim here).
**Builds on:** `2026-07-07-token-validation-design.md` (tag/markup span rules, the
did-you-mean message voice), `2026-07-09-text-tag-project-sweep-design.md` (the
project-wide sweep window this feature generalises), the `tags.json`/catalogue pattern
(committed, regenerable data artifacts), and the NAudio/vgmstream dependency +
`THIRD_PARTY_LICENSES.md` precedent.

## Problem

Nothing catches a plain misspelling in writer-touched dialog text. Token/markup
validation guards the tag vocabulary, but "The captian nods." sails through every
existing check and ships to the game. The hard part is that Eora text is full of
in-world vocabulary (*Rauataian, adra, kith, Ondra, …*) that any real-world dictionary
rejects — a naive spell checker would drown writers in false positives.

## The three-layer dictionary (settled design)

A word is correctly spelled if **any** layer accepts it:

1. **Real-world language dictionary (user-supplied).** A standard open-source Hunspell
   `.aff`/`.dic` pair per language; the user picks which to obtain and use.
2. **Generated game lexicon (committed artifact).** Scan every shipped conversation's
   text — per game, for each game language installed on the machine — **omitting all
   tags/tokens**, recording per-word frequencies. Subtract every word the language's
   layer-1 dictionary accepts; the remainder approximates the in-world vocabulary. A
   curation pass (assistant generates, user reviews) then removes residual real-world
   words — so a *shipped typo* does not get whitelisted and mod text is held to a
   higher standard than vanilla.
3. **Custom user dictionary.** Starts blank; grows via an "Add to dictionary" action.
   **Global**, not per-project (a modder's invented names span projects).

Text whose language has **no installed layer-1 dictionary is skipped entirely** — no
half-checks. This naturally excludes ko/zh (Hunspell-style checking does not fit CJK).
Every checkable text already carries a language: Default/Female text is the provider
language; each translation carries its code — so the dictionary is **auto-selected per
text**, never a global setting.

## Decisions (settled during brainstorming)

- **Engine: WeCantSpell.Hunspell** (NuGet, pure managed, reads standard `.aff`/`.dic`,
  full affix/compound support). New dependency in `DialogEditor.ViewModels`; licence
  recorded in `THIRD_PARTY_LICENSES.md` (NAudio precedent). (Rejected: hand-rolled word
  lists — no affix support, heavy false positives in de/pl/ru; bundled native
  hunspell — needless interop.)
- **Acquisition: manual drop-in folder** — the app gains **no network code**. Three
  concrete UX requirements (user's): a link to a currently-valid dictionary source
  (pinned to the LibreOffice dictionaries repository,
  `https://github.com/LibreOffice/dictionaries` — **URL verified live during
  implementation**), plain instructions in the Settings UI, and an **Open dictionaries
  folder** button. (Rejected: in-app downloader — first network feature, licence
  redistribution questions; bundling — repo bloat, frozen versions.)
- **Surfaces: everywhere, one window.** Live spelling warnings join the existing amber
  warning box in the node detail panel; the project sweep window generalises from
  "Validate Text Tags…" to **"Validate Text…"**, covering tags *and* spelling in one
  scan with an issue-type label per row. Strings-only rename — C# class names
  (`TextTagValidation*`) stay. (Rejected: a separate Spell Check window — duplicate
  sweep UX; sweep-only — typos invisible until a scan.)

## Architecture

House pattern: pure logic in `DialogEditor.ViewModels`, thin View glue in
`DialogEditor.Avalonia`; generation offline in `tools/`.

### 1. Lexicon generator — `tools/lexicon-gen/` (Python, committed)

`catalogue-gen` precedent. Inputs: both games' installed localized stringtable trees
(PoE2: 10 languages; PoE1: its localized sets) and the layer-1 `.aff`/`.dic` pairs
available locally. Per language:

1. Walk the conversation stringtables, extract text, strip `[…]`/`<…>` spans with the
   same span rules the token validator uses (one vocabulary, one stripper).
2. Tokenise into words (letters + apostrophes/hyphens; pure numbers dropped), count
   frequencies.
3. Subtract every word the language's layer-1 dictionary accepts (via a Python Hunspell
   implementation against the same `.aff`/`.dic` files; the exact library is pinned in
   the implementation plan).
4. Emit `data/lexicons/<lang>.txt` — one word + count per line, sorted by descending
   count. **Counts are kept**: they make curation fast (count-1 oddities vs the
   300-occurrence *adra*) and follow `tags.json`'s `count` precedent.

The English lexicon gets a curation pass now (assistant proposes removals, user
reviews the diff); other languages ship uncurated initially — safe in the conservative
direction (a shipped word is never flagged), curable later. The lexicons are embedded
resources (like `tags.json`) with the `data/` mirror kept byte-identical.

**The generator is a permanent, committed tool** — not a one-off script. It is kept
(with its tests and README) so lexicons can be regenerated for future languages,
after game patches, or for curation refreshes without reinventing the wheel.

### 2. Dictionary store — `SpellDictionaryStore` (`DialogEditor.ViewModels/Services`)

Owns the three layers:
- **Layer 1:** discovers `.aff`/`.dic` pairs in
  `%LOCALAPPDATA%\PillarsDialogEditor\dictionaries` (filename prefix → language code,
  `de_DE` → `de`), lazily constructs a WeCantSpell `WordList` per language on first
  use. A failed load is logged via `AppLog.Warn` and the dictionary treated as absent —
  never a crash.
- **Layer 2:** loads the embedded lexicon for the language into a case-tolerant set
  (counts ignored at runtime).
- **Layer 3:** loads/saves the user word list (plain text in app data);
  `AddWord(string)` appends, persists, and invalidates the cached set.

Exposes `HasDictionary(lang)` and `IsCorrect(word, lang)` (= layer-1 Hunspell check ∨
layer-2 set ∨ layer-3 set). Injectable/instantiable for tests (temp-folder isolation;
no global statics beyond an app-default instance).

### 3. Checker — `SpellCheckService` (`DialogEditor.ViewModels/Services`)

`Check(text, languageCode)` → misspelled words with positions. Strips tag/markup spans
(reusing the token validator's span rules), tokenises words, skips pure numbers,
checks each against the store. Messages carry Hunspell's top suggestion when one
exists — "Possible misspelling '{0}' — did you mean '{1}'?" — matching the token
validator's voice; without a suggestion, "Possible misspelling '{0}'."
`HasDictionary(lang) == false` → empty result. Pure and non-throwing.

### 4. Surfaces

- **Detail panel (live):** the existing amber warning box's content becomes the union
  of tag findings and spelling findings for Default/Female (provider language), with
  the same recompute triggers. Zero new chrome.
- **Sweep window (one window):** `ProjectTextTagScanner` emits both issue kinds; the
  row gains an issue-type label (Tag / Spelling). User-visible strings change to
  "Validate Text…" (menu item, window title, tooltips); spelling rows get an
  **Add to dictionary** button (tooltip + HelpText) that calls
  `SpellDictionaryStore.AddWord` and re-runs the scan. The three-way dirty guard is
  unchanged.
- **Settings ▸ Spelling:** instructions text, the pinned source link (opens in
  browser), the **Open dictionaries folder** button (creates the folder if missing),
  and the list of detected dictionary languages. All strings localized; buttons carry
  tooltips + mirrored HelpText.

## Cross-cutting rules (CLAUDE.md)

- **Localisation** — every message/label/tooltip is a resource; plural-safe composition
  where counts appear.
- **Tooltips / UIA** — mandatory on the new buttons (open folder, add to dictionary,
  source link); enforced by the existing structural suites.
- **Error handling** — dictionary load/IO failures logged via `AppLog.Warn` and
  degraded gracefully (no dictionary → unchecked); no bare catch.
- **Tests run serially** — the store is instance-based with temp-folder isolation;
  no new global state races.

## Testing (TDD, red first)

A tiny hand-written fixture `.aff`/`.dic` (a few stems + one affix rule) committed
under test fixtures, so tests need no real dictionary:

- **Store:** discovery by filename prefix; failed/corrupt pair → absent + logged;
  layer-3 add-word round-trip (persists across instances); `HasDictionary`.
- **Checker:** correct word passes; affixed form passes (proves real Hunspell
  evaluation); misspelling flagged with suggestion; layer-2 lexicon word passes;
  layer-3 word passes after add; `[tokens]`/`<markup>` content never spell-checked;
  numbers skipped; unknown language → empty.
- **Scanner/VM:** per-language dictionary routing (en text checked, fr skipped when
  only en installed); mixed tag + spelling rows with correct type labels; add-word
  refresh.
- **Settings VM:** folder path exposed; open-folder behind an injectable seam.
- **Generator (Python):** strip/tokenise/count on a fixture stringtable; subtraction
  against a fixture dictionary; output format.
- Enforcer suites (localisation, tooltips/HelpText, plurals, hex) cover the new UI
  automatically.

## Deferred (tracked in Gaps.md so they aren't lost — most are expected to happen)

- **Inline squiggles** in the text boxes (adorner work; shared with token validation).
- **Suggestion-apply quick fix** (click to accept "receive").
- **Curated non-English lexicons** (English is curated first; the tool supports all).
- **Per-project user dictionaries** (layer 3 is global for now).
- **Add-to-dictionary from the detail panel** (v1 offers it on sweep rows only).
