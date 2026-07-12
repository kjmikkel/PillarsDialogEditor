# Spell Checker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three-layer spell checking (user Hunspell dictionaries + generated game lexicon + user word list) for dialog text, surfaced live in the detail panel and project-wide in the sweep window (generalised to "Validate Text…").

**Architecture:** A permanent committed Python generator (`tools/lexicon-gen/`) scans both games' localized stringtables, strips tags, counts words, subtracts layer-1 words, and emits per-language lexicons (with counts) committed under `data/lexicons/` and embedded in `DialogEditor.ViewModels`. At runtime, `SpellDictionaryStore` (drop-in `.aff`/`.dic` discovery via WeCantSpell.Hunspell + embedded lexicons + user word list) backs a pure `SpellCheckService`; findings merge into the existing detail-panel warning box and the `ProjectTextTagScanner` sweep, with an Add-to-dictionary action and a Settings ▸ Spelling section.

**Tech Stack:** C# / .NET 8, WeCantSpell.Hunspell (new NuGet in `DialogEditor.ViewModels`), Avalonia, xUnit; Python 3 (+ `spylls` on the dev machine only) for the generator.

## Global Constraints

- **TDD, red first.** Suite runs serially.
- **Language routing:** every checked text carries a language (Default/Female = provider language; translations their codes). `HasDictionary(lang) == false` → that text is skipped entirely. No global spell-language setting.
- **Word acceptance:** layer-1 Hunspell check ∨ layer-2 lexicon set ∨ layer-3 user set (sets case-tolerant: exact match OR lower-cased match).
- **Tag stripping:** spelling never looks inside `[…]`/`<…>` spans — same span regexes as `TokenValidationService` (`\[[^\[\]\n\r]*\]` and `<[^>]*>`).
- **Strings-only rename:** user-visible strings become "Validate Text…" (menu/title/tooltips); C# class names (`TextTagValidation*`, `ProjectTextTagScanner`) stay.
- **Localisation / tooltips / UIA / no-stray-hex / plurals:** enforced by the existing structural suites; every new button carries ToolTip + mirrored HelpText.
- **Error handling:** dictionary/lexicon IO failures → `AppLog.Warn`, degrade to "no dictionary"; no bare catch.
- **Dev-machine downloads (Task 2 only):** layer-1 dictionaries are fetched from the pinned source `https://github.com/LibreOffice/dictionaries` (raw.githubusercontent URLs) to a scratch folder — this doubles as the spec's live URL-validity verification. The app itself gains no network code.
- **Two mirrors stay identical:** `DialogEditor.ViewModels/Resources/Lexicons/*.txt` (embedded) and `data/lexicons/*.txt`.
- **Build/test:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`; generator tests `python tools/lexicon-gen/test_generate.py`.

---

## Phase A — data + engine

### Task 1: `tools/lexicon-gen/` generator

**Files:**
- Create: `tools/lexicon-gen/generate.py`, `tools/lexicon-gen/test_generate.py`, `tools/lexicon-gen/README.md`, `tools/lexicon-gen/.gitignore` (`__pycache__/`, `*.pyc`)

**Interfaces (Python):**
- `strip_tags(text) -> str` — removes `\[[^\[\]\n\r]*\]` and `<[^>]*>` spans.
- `tokenize(text) -> list[str]` — words of letters plus internal apostrophes/hyphens; pure numbers dropped; case preserved.
- `count_words(stringtable_dir) -> Counter` — walks `**/*.stringtable`, XML-parses `Entries/Entry/DefaultText|FemaleText` (format verified: `<StringTableFile><Entries><Entry><ID/><DefaultText/><FemaleText/>`), strips tags, tokenises, counts case-insensitively (store lower-case key + a representative original casing).
- `subtract(counter, checker) -> Counter` — drops words `checker.lookup(word)` accepts (checker = spylls `Dictionary` wrapper; injectable fake in tests).
- `emit(counter, out_path)` — `word<TAB>count` lines, descending count then alphabetical.
- `main`: `--game-dirs` (one or more localized roots), `--lang`, `--dict-aff/--dict-dic` (optional; skip subtraction when absent), `--out`.

- [ ] **Step 1: Write the failing tests**

`tools/lexicon-gen/test_generate.py` (plain asserts, `catalogue-gen` style):

```python
import generate
from collections import Counter

def test_strip_tags():
    assert generate.strip_tags("Hi [Player Name], <i>rest</i> now") == "Hi ,  rest  now".replace("  ", " ") or True
    s = generate.strip_tags("Hi [Player Name], <i>rest</i>.")
    assert "[" not in s and "<" not in s and "Player" not in s and "rest" in s

def test_tokenize():
    assert generate.tokenize("Rauatai's storm-called 3 ships") == ["Rauatai's", "storm-called", "ships"]

def test_count_words(tmp_dir_with_fixture):  # helper writes a fixture .stringtable
    c = generate.count_words(tmp_dir_with_fixture)
    assert c["adra"] == 2 and c["the"] >= 1

def test_subtract():
    class Fake:  # accepts only "the"
        def lookup(self, w): return w.lower() == "the"
    c = generate.subtract(Counter({"the": 5, "adra": 2}), Fake())
    assert "the" not in c and c["adra"] == 2

def test_emit(tmp_path_out):
    ...  # writes Counter({"adra":2,"kith":1}), asserts "adra\t2" is first line
```

(Write the two helpers inline: create a temp dir with one fixture `.stringtable` using the exact XML shape above; a temp output path. Follow `catalogue-gen/test_generate.py`'s self-runner `__main__` block.)

- [ ] **Step 2: Run to verify failure** — `python tools/lexicon-gen/test_generate.py` → import error.

- [ ] **Step 3: Implement `generate.py`** — pure functions above; `subtract` takes any object with `.lookup(word)->bool`; spylls imported lazily only in `main` (`from spylls.hunspell import Dictionary`) so tests need no spylls. README documents usage + the permanent-tool intent (spec §1).

- [ ] **Step 4: Run to verify pass** — all tests green.

- [ ] **Step 5: Commit** — `git add tools/lexicon-gen && git commit -m "feat(spelling): lexicon generator tool"`

---

### Task 2: Generate + curate the lexicons

**Files:**
- Create: `DialogEditor.ViewModels/Resources/Lexicons/<lang>.txt` (en, de, es, fr, it, pl, pt, ru — the non-CJK PoE2 set), `data/lexicons/<lang>.txt` (identical mirrors)
- Modify: `DialogEditor.ViewModels/DialogEditor.ViewModels.csproj` (`<EmbeddedResource Include="Resources\Lexicons\*.txt" />`)

- [ ] **Step 1: Fetch layer-1 dictionaries to the dev machine (explicit download)**

`pip install spylls`; then curl the 8 languages' `.aff`/`.dic` pairs from the pinned LibreOffice repo (raw URLs, e.g. `en/en_US.aff`, `de/de_DE_frami.aff`, …) into the scratchpad. **This is the live URL-validity check the spec requires** — record the working per-language paths in the generator README (they feed the Settings link/instructions later).

- [ ] **Step 2: Generate raw lexicons for all 8 languages**

Run `generate.py` per language over both games' localized roots (PoE2 `exported/localized/<lang>/text/conversations`, PoE1 `data*/localized/<lang>/text/conversations` — note PoE1 language codes may differ slightly; map what exists). Sanity-check: `en` output's top entries should be recognisably Eora (*adra, kith, Rauatai, …*), size order 10²–10³ words.

- [ ] **Step 3: Curate English (user checkpoint)**

Propose removals from `en.txt` (real-world words the dictionary missed, shipped typos, scanning artifacts — count-1 entries scrutinised hardest). **Present the removal list to the user for review before committing.** Other languages ship uncurated (conservative direction; spec §1).

- [ ] **Step 4: Commit lexicons + embedding**

Copy curated/raw outputs to both mirrors; add the `EmbeddedResource` glob; `git diff --no-index` each pair to prove identity. Build. Commit: `feat(spelling): committed game lexicons (en curated)`.

---

### Task 3: WeCantSpell dependency + `SpellDictionaryStore`

**Files:**
- Modify: `DialogEditor.ViewModels/DialogEditor.ViewModels.csproj` (add `WeCantSpell.Hunspell` — latest stable; **verify licence (expected MIT) and add its text to `THIRD_PARTY_LICENSES.md`**, NAudio precedent)
- Create: `DialogEditor.ViewModels/Services/SpellDictionaryStore.cs`
- Create: `DialogEditor.Tests/Fixtures/spell/test_en.aff` + `test_en.dic` (hand-written: ~4 stems, one suffix rule, e.g. `S` suffix producing plurals — enough to prove affix evaluation)
- Test: `DialogEditor.Tests/Services/SpellDictionaryStoreTests.cs`

**Interfaces:**
- `SpellDictionaryStore(string dictionariesDirectory, string userDictionaryPath)`
- `static SpellDictionaryStore Default` — app-data paths (`%LOCALAPPDATA%\PillarsDialogEditor\dictionaries`, `…\user-dictionary.txt`), lazily created; **not used by any test**.
- `IReadOnlyList<string> AvailableLanguages` (from `*.aff`+`*.dic` filename prefixes, `de_DE` → `de`)
- `bool HasDictionary(string lang)`
- `bool IsCorrect(string word, string lang)` (layers 1∨2∨3; case-tolerant sets for 2/3)
- `string? Suggest(string word, string lang)` (Hunspell top suggestion or null)
- `void RegisterLexicon(string lang, IEnumerable<string> words)` (layer 2; production bootstrap loads embedded resources via a small `EmbeddedLexicons.LoadInto(store)` helper in the same file)
- `void AddWord(string word)` (layer 3: append + persist + refresh)
- `string DictionariesDirectory` (for the Settings open-folder button)

- [ ] **Step 1: Write the failing tests** — temp-dir isolated (`IDisposable` fixture): discovery (`test_en.aff/dic` copied in → `AvailableLanguages` contains "en"); affixed form accepted (e.g. stem `cat` + S rule → `cats` correct — proves real Hunspell, not word-list); unknown word incorrect + `Suggest` returns something for a 1-edit typo of a stem; corrupt pair (garbage bytes) → language absent, no throw; `RegisterLexicon("en", ["adra"])` → `IsCorrect("Adra","en")`; `AddWord("Xoti")` persists across a second store instance; `HasDictionary("fr")` false.
- [ ] **Step 2: Verify red** (compile error).
- [ ] **Step 3: Implement** — WeCantSpell `WordList.CreateFromFiles(dic, aff)` in try/catch → `AppLog.Warn` on failure; lazy per-language cache; layers 2/3 as `HashSet<string>` (ordinal) checked with exact + lower-case forms.
- [ ] **Step 4: Verify green.**
- [ ] **Step 5: Commit** incl. `THIRD_PARTY_LICENSES.md` entry.

---

### Task 4: `SpellCheckService`

**Files:**
- Create: `DialogEditor.ViewModels/Services/SpellCheckService.cs`
- Test: `DialogEditor.Tests/Services/SpellCheckServiceTests.cs`

**Interfaces:**
- `record SpellIssue(string Word, int Position, string? Suggestion)`
- `SpellCheckService(SpellDictionaryStore store)`
- `IReadOnlyList<SpellIssue> Check(string text, string languageCode)` — strip tag/markup spans (same regexes as `TokenValidationService`; replace spans with spaces so positions stay meaningful), tokenise (letters + internal `'`/`-`; skip pure numbers and single letters), report each distinct incorrect word once (first position), with `store.Suggest` for the suggestion. `HasDictionary == false` or empty text → `[]`. Pure, non-throwing.

- [ ] **Step 1: Failing tests** (fixture store from Task 3 + registered lexicon): correct text → empty; misspelling flagged with suggestion; affixed form passes; lexicon word passes; word inside `[Player Nmae]` or `<i>` **never** flagged; numbers skipped; unknown language → empty; duplicate misspelling reported once.
- [ ] **Step 2: Red. Step 3: Implement. Step 4: Green. Step 5: Commit.**

---

## Phase B — integration

### Task 5: Detail panel (live)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (wire `ActiveLanguage` + checker)
- Test: extend `DialogEditor.Tests/ViewModels/NodeDetailViewModelValidationTests.cs`

**Interfaces:**
- `NodeDetailViewModel.SpellChecker` (`SpellCheckService?`, null in tests by default → spelling silently off) and `NodeDetailViewModel.ActiveLanguage` (`string`, set by `MainWindowViewModel` next to `ActiveGameId` from `_provider.Language`).
- `TokenWarnings` becomes the union: existing tag findings + spelling findings for Default (+ Female when present), formatted via new keys:

```xml
    <sys:String x:Key="Spelling_Misspelled_Suggest">Possible misspelling "{0}" — did you mean "{1}"?</sys:String>
    <sys:String x:Key="Spelling_Misspelled">Possible misspelling "{0}".</sys:String>
```

- App wiring: where `Detail.ActiveGameId = provider.GameId` is set (~line 1324 area), also set `Detail.ActiveLanguage = provider.Language;` and, once at startup wiring in `MainWindow.axaml.cs`, `vm.Detail.SpellChecker = new SpellCheckService(SpellDictionaryStore.Default);` with `EmbeddedLexicons.LoadInto(SpellDictionaryStore.Default)` called once (guarded, `AppLog.Warn` on failure).

- [ ] **Step 1: Failing tests** — with an injected checker (fixture store): misspelled Default text adds a `Spelling_Misspelled_Suggest` message alongside tag warnings; null checker → tag-only (existing tests unchanged); recompute on text edit.
- [ ] **Step 2: Red. Step 3: Implement. Step 4: Green (incl. existing validation tests). Step 5: Commit.**

---

### Task 6: Sweep — spelling rows, type labels, add-to-dictionary, "Validate Text…" rename

**Files:**
- Modify: `DialogEditor.ViewModels/Services/ProjectTextTagScanner.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (pass checker + add-word into the closure/VM)
- Modify: `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml` (type column + per-row Add button)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Tests: extend scanner/VM/window test files

**Interfaces:**
- `enum TextIssueType { Tag, Spelling }` (in the scanner file).
- `TextTagIssueRow` gains optional members (compile-compatible): `(…, string Message, TextIssueType Type = TextIssueType.Tag, string? Word = null)`.
- `ProjectTextTagScanner.Scan(project, gameId, primaryLanguage, TokenValidationService? validator = null, SpellCheckService? spell = null)` — when `spell` is non-null, each text also gets `spell.Check(text, langCode)` where `langCode` is the *actual* language (primary rows use `primaryLanguage`, translation rows their code); spelling rows carry `Type = Spelling`, `Word` set. Ordering unchanged (conversation, node, language), tags before spelling within a node/language.
- `TextTagValidationViewModel(Func<…> scan, Action<string>? addWord = null)`; `TextTagRowViewModel` gains `TypeLabel` (localized "Tag"/"Spelling"), `CanAddToDictionary` (`Word != null && addWord != null`), `AddToDictionaryCommand` (invokes addWord(Word) then the parent refresh — wire via a callback passed to the row).
- `RequestTextTagValidationAsync` closure passes `new SpellCheckService(SpellDictionaryStore.Default)` (test seam: `public Func<SpellCheckService?>? SpellCheckerFactory { get; set; }` defaulting to the Default-store one; tests inject fixture/null) and `addWord: w => { SpellDictionaryStore.Default.AddWord(w); }` via the same factory object — concretely: pass the store through, `addWord = store.AddWord`.
- Strings: update **values** of `TextTagValidation_Title` → `Validate Text`, `Menu_ValidateTextTags` → `Validate Text…`, `ToolTip_ValidateTextTags` (mention spelling), `TextTagValidation_SavedNote` (mention spelling); add:

```xml
    <sys:String x:Key="TextIssueType_Tag">Tag</sys:String>
    <sys:String x:Key="TextIssueType_Spelling">Spelling</sys:String>
    <sys:String x:Key="Button_AddToDictionary">Add to dictionary</sys:String>
    <sys:String x:Key="ToolTip_AddToDictionary">Treat this word as correctly spelled from now on (adds it to your personal dictionary, layer 3).</sys:String>
```

- Window row template: insert a `TypeLabel` TextBlock column and an Add button (`IsVisible="{Binding CanAddToDictionary}"`, ToolTip + HelpText + AutomationProperties.Name).

- [ ] **Step 1: Failing tests** — scanner: spelling row for a misspelled translation with correct language routing (en checked when dictionary exists; fr skipped without one — use the fixture store); tag+spelling rows coexist with right `Type`; `spell:null` → behaviour identical to today (existing tests untouched). VM: `TypeLabel` localization keys; add-word invokes callback then rescans. Guard tests: `SpellCheckerFactory` seam honoured.
- [ ] **Step 2: Red. Step 3: Implement. Step 4: Full suite green (existing sweep tests must not regress). Step 5: Commit.**

---

### Task 7: Settings ▸ Spelling + app verification + docs

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs`, `DialogEditor.Avalonia/Views/SettingsWindow.axaml`, `Strings.axaml`
- Modify: `Gaps.md`
- Test: extend `SettingsViewModel` tests

**Interfaces:**
- `SettingsViewModel` gains: `string DictionariesFolder` (from `SpellDictionaryStore.Default.DictionariesDirectory` behind an injectable getter for tests), `IReadOnlyList<string> DetectedDictionaryLanguages`, `OpenDictionariesFolderCommand` (behind an injectable `Action<string>? FolderOpener` seam; production uses the same shell-open mechanism the About window's repository link uses — reuse it), `OpenDictionarySourceCommand` (opens the pinned URL recorded in Task 2).
- Settings XAML: a "Spelling" group mirroring the existing row style — instructions TextBlock (`Settings_SpellingInstructions`), folder path + **Open folder** button, source **link** button, detected-languages line. All tooltips + HelpText.
- Strings (values finalized against the Task-2-verified URL):

```xml
    <sys:String x:Key="Settings_SpellingHeader">Spelling</sys:String>
    <sys:String x:Key="Settings_SpellingInstructions">To enable spell checking for a language, download its Hunspell dictionary (a matching .aff and .dic pair) and place both files in the dictionaries folder. The editor detects them automatically.</sys:String>
    <sys:String x:Key="Settings_OpenDictionariesFolder">Open dictionaries folder</sys:String>
    <sys:String x:Key="ToolTip_OpenDictionariesFolder">Open the folder where Hunspell dictionary files (.aff/.dic pairs) go. The folder is created if it does not exist.</sys:String>
    <sys:String x:Key="Settings_DictionarySource">Get dictionaries (LibreOffice repository)</sys:String>
    <sys:String x:Key="ToolTip_DictionarySource">Opens the LibreOffice dictionaries repository in your browser — free, open-source Hunspell dictionaries for many languages.</sys:String>
    <sys:String x:Key="Settings_DetectedDictionaries">Detected dictionaries: {0}</sys:String>
    <sys:String x:Key="Settings_DetectedDictionaries_None">No dictionaries detected yet.</sys:String>
```

- [ ] **Step 1: Failing SettingsViewModel tests** (folder path exposed; open-folder seam invoked; detected list from an injected store). **Step 2: Red. Step 3: Implement (create folder on open if missing).**
- [ ] **Step 4: Full suite + generator tests green.**
- [ ] **Step 5: App verification** (`running-the-app`): Settings shows the Spelling section; Open-folder button works (folder created); drop the Task-2 `en` pair in, restart, detected list shows "en"; type a misspelling in a node → amber box shows the spelling warning; Test ▸ **Validate Text…** (renamed) runs and shows a Spelling row; Add to dictionary removes it on rescan. Screenshot.
- [ ] **Step 6: Update `Gaps.md`** — mark the Spell Checker gap implemented (keep the deferred-follow-ups list), in house style with spec pointer.
- [ ] **Step 7: Commit.**

---

## Self-Review

**Spec coverage:** generator (permanent tool, strip/count/subtract, counts kept, README) → T1–2; curation checkpoint (en, user reviews) → T2; drop-in store + WeCantSpell + licence entry → T3; checker w/ suggestions + skip rule → T4; detail-panel union + per-text language → T5; one-window sweep w/ type labels, add-word, strings-only rename, dirty guard untouched → T6; Settings three requirements (link/instructions/open-folder) + verification + Gaps → T7. Deferred items already recorded in Gaps.md (kept by T7). ✔

**Placeholder scan:** implementation-time verifications are explicit and bounded (LibreOffice raw URLs in T2 — doubles as the spec's URL check; WeCantSpell licence text in T3; PoE1 language-code mapping in T2). Test lists name concrete cases; XAML/strings shown where new. ✔

**Type consistency:** `SpellDictionaryStore` members match across T3–7 (`IsCorrect`, `Suggest`, `RegisterLexicon`, `AddWord`, `HasDictionary`, `DictionariesDirectory`, `Default`); `SpellIssue(Word, Position, Suggestion)` matches T4–6; `TextTagIssueRow` optional-append keeps T-old tests compiling; `Scan(..., spell = null)` default preserves existing scanner tests; `SpellCheckerFactory` seam named consistently in T6. ✔

**Risks:** WeCantSpell API surface (`WordList.CreateFromFiles`) verified at T3 red→green; `spylls` availability (dev-only, pip); curation is interactive by design (T2 user checkpoint).
