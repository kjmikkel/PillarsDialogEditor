---
title: "privacy-guard: per-file pipelines take minutes in Git Bash; PNG text needs real chunk parsing"
author: Mikkel
date: 2026-10-24
branch: feat/271-privacy-guard
cost: { minutes: 8, tokens: 11987 }
id: l-e4796790
scope: repo
status: active
touches:
  - ".github/scripts/privacy-guard.sh"
---

## Decided
- `.github/scripts/privacy-guard.sh` reads PNG metadata with ONE perl process fed by `git cat-file --batch`, parsing the length-prefixed chunks (tEXt, zTXt, iTXt, eXIf; compressed ones inflated with Compress::Zlib) and emitting one record per line of text. IDAT is never read. perl ships with Git for Windows, macOS and ubuntu runners.
- Record filtering (skipped files, `privacy-guard: allow`) is one awk pass, not a per-record loop.
## Tried and ruled out
- `git show | tr | grep | awk` per PNG plus a per-record grep loop: on a ~290-file branch with ~200 PNGs it took over 5 minutes in Git Bash on Windows with only ~80 s of CPU; process creation dominates. The batched version takes ~14 s.
- grep for `(tEXt|iTXt)` followed by printable bytes: stops at the NUL between keyword and text, at newlines and at non-ASCII bytes, and never sees compressed zTXt/iTXt. The tests passed vacuously until cases for each gap were added.
## Watch out
- In awk, `print > file` truncates on the program's first open of that file; a second awk appending to files the first one wrote must use `>>`.
- The privacy-guard test suite takes ~4 minutes on Windows; running it alongside `dotnet test` made 6 headless-render tests fail from CPU load (they pass on rerun).
