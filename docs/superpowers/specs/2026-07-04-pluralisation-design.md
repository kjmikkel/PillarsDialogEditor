# Plural-Aware Localisation (CLDR Categories)

**Date:** 2026-07-04
**Status:** Approved
**Gaps.md origin:** UI Localisation Readiness item 6 — naive pluralisation
("`{0} nodes`" breaks in languages with multiple plural forms).

## Problem

24 UI strings (21 in `Strings.axaml`, 3 in `SharedStrings.axaml`) use the English-only
"`{0} match(es)`" idiom. Languages differ in how many plural forms they need (Polish 4,
Russian 3, Arabic 6 — CLDR categories zero/one/two/few/many/other); a single format
string per key cannot be translated correctly into them, and English itself renders
awkward text ("1 match(es)").

## Decision

CLDR-category plural support via key suffixes, resolved by a new `Loc.FormatCount`.
Rejected: two-form singular/plural pairs (reproduces the audit's exact complaint for
Slavic/Arabic); ICU MessageFormat via NuGet (new dependency, value-level syntax that
translators must learn and that fights the flat-key CSV round-trip).

## Design

### 1. `PluralRules` (new, pure)

`DialogEditor.ViewModels/Services/PluralRules.cs`:

- `enum PluralCategory { Zero, One, Two, Few, Many, Other }`
- `static PluralCategory Category(string langTwoLetter, int n)` — integer counts only
  (every affected string counts discrete things).
- Ships CLDR cardinal rules for: `en`, `de`, `fr` (one/other — note `fr` treats 0 as
  One), `pl`, `ru` (one/few/many/other), `ar` (all six). Unknown language codes fall
  back to the `en` rule. Multi-form rules ship now to prove the mechanism with tests,
  not as speculation — they are the reference implementations a future translation
  plugs into.

### 2. `Loc.FormatCount`

`static string FormatCount(string key, int count, params object[] extraArgs)`:

1. Language = `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName`
   (set by `CoreLocale.SetCulture` on language switch; live switching keeps working
   because lookup happens per call, same as `Loc.Get`).
2. Category = `PluralRules.Category(lang, count)`.
3. Key resolution, first hit wins: `{key}_{Category}` → `{key}_Other` → `{key}`
   (legacy safety — an unmigrated key formats as before rather than crashing).
   "Hit" = the provider returns a value for the key; `IStringProvider`/
   `AvaloniaStringProvider` needs a `TryGet` (or equivalent) so missing keys are
   distinguishable from present ones.
4. `string.Format(value, count, extraArgs…)` — count is always `{0}`.

### 3. Migration of the 24 strings

- Each "`… {n} noun(s) …`" key becomes an English `_One`/`_Other` pair
  (e.g. `FindReplace_Matches_One` = "1 match", `FindReplace_Matches_Other` =
  "{0} matches"); the call site changes `Loc.Format` → `Loc.FormatCount`. The old
  un-suffixed key is deleted (the fallback chain is for future stragglers, not for
  keeping dead keys).
- The two-count strings (e.g. `BatchReplace_StatusMatches`
  "{0} match(es) across {1} conversation(s)") are split: two pluralised fragment keys
  plus a wrapper key ("{0} across {1}"), composed at the call site with two
  `FormatCount` calls. Sentence composition is an accepted tools-grade trade-off;
  each wrapper gets a translator note comment in the dictionary.
- XAML-side: none of the 24 keys are referenced directly from XAML with a count baked
  in (they are all ViewModel `Loc.Format` calls) — verify at plan time; any exception
  gets a ViewModel property instead.

### 4. Translation workflow

- CSV export walks all keys, so suffixed rows appear automatically.
- Requirement: `UiStringImportService` must accept CSV rows whose keys do **not** exist
  in the English source (a Polish translator adds `_Few`/`_Many` rows for keys English
  only ships as `_One`/`_Other`). If it currently drops unknown keys, fixing that is in
  scope.
- Translator notes at the top of `Strings.axaml` gain a short paragraph explaining the
  suffix scheme and that a language uses exactly the categories its CLDR rule defines.

### 5. Guard rail

New structural test (`DialogEditor.Tests/Accessibility` sibling style, e.g.
`DialogEditor.Tests/Localisation/NoNaivePluralTests.cs`): fails if any string value in
the three dictionaries matches the naive-plural pattern `(s)`/`(es)` — the old idiom
cannot creep back.

## Testing

- `PluralRulesTests`: theory per language against CLDR reference cases
  (en: 1→One, 0/2→Other; fr: 0→One, 2→Other; pl: 1→One, 2–4→Few, 5→Many, 22→Few,
  12→Many; ru: 1/21→One, 2/3→Few, 5/11/14→Many; ar: 0→Zero, 1→One, 2→Two, 3→Few,
  11→Many, 100→Other; unknown lang falls back to en rule).
- `LocFormatCountTests`: category suffix selection, `_Other` fallback, bare-key legacy
  fallback, extraArgs positioning — via a stub provider with controllable key presence.
- `NoNaivePluralTests` red/green against the migration itself.
- Full suite green after migration; manual spot-check of Find/Replace status line
  ("1 match" vs "3 matches") and one composed two-count status.
