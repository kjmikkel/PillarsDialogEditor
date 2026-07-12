# Lexicon generator (spell-check layer 2)

Generates the per-language **game lexicons** — the in-world vocabulary
(*adra, kith, Rauatai, …*) that layer 2 of the spell checker whitelists.

**Permanent, committed tool.** Kept (with tests) so lexicons can be regenerated
for future languages, after game patches, or for curation refreshes — don't
reinvent the wheel. Design:
`docs/superpowers/specs/2026-07-11-spell-checker-design.md`.

## What it does

1. Walks every `*.stringtable` under the given localized conversation roots
   (both games), extracting `DefaultText`/`FemaleText`.
2. Strips `[token]` / `<markup>` spans (same rules as the app's
   `TokenValidationService`), tokenises words, counts case-insensitively.
3. Subtracts every word the language's layer-1 Hunspell dictionary accepts
   (via `spylls`); the remainder approximates the in-world vocabulary.
4. Emits `word<TAB>count`, descending count — counts are kept because they make
   curation fast (count-1 oddities vs the 300-occurrence *adra*).

Outputs are committed to `DialogEditor.ViewModels/Resources/Lexicons/<lang>.txt`
(embedded) and mirrored at `data/lexicons/<lang>.txt`. English is curated
(real-world stragglers and shipped typos removed); other languages ship raw.

## Run (dev machine)

```
pip install spylls defusedxml

python tools/lexicon-gen/generate.py ^
  --game-dirs "D:/.../Pillars of Eternity II Deadfire/PillarsOfEternityII_Data/exported/localized/en/text/conversations" ^
              "D:/.../PillarsOfEternity/PillarsOfEternity_Data/data/localized/en/text/conversations" ^
  --dict-aff <path>/en_US.aff --dict-dic <path>/en_US.dic ^
  --out data/lexicons/en.txt
```

Omit `--dict-aff/--dict-dic` to emit the raw (unsubtracted) frequency list.

Layer-1 dictionaries: https://github.com/LibreOffice/dictionaries
(per-language folders; download the matching `.aff` + `.dic` pair).

## Tests

```
python tools/lexicon-gen/test_generate.py
```
