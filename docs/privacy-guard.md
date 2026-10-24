# Privacy guard

The repository is public, so a commit that adds your Windows user name, a home directory path
or a word you want to keep private is hard to take back. The privacy guard
(`.github/scripts/privacy-guard.sh`) checks for that before the commit is made and again in CI.

## What it checks

Only **added** lines and the **names of added files** are scanned, so old content never blocks a
change. Every hit is reported as `file:line: rule: text`, and the run fails.

1. **Generic patterns** (committed, `.github/privacy-patterns.txt`): Windows user profile paths
   (`C:\Users\name`, with forward slashes or doubled backslashes), `\AppData\Local|Roaming|LocalLow\`
   under a concrete user, `/home/<name>/` and `/Users/<name>/`. Placeholders are fine:
   `%USERPROFILE%`, `%LOCALAPPDATA%`, `<user>`, `$env:...`, `C:\Users\<name>`, `C:\Users\...`,
   `C:\Users\Public`. Game-store folders (`GOG Galaxy\Games`, `steamapps\common`) are not flagged
   because the docs name the default install locations.
2. **Private words** (never committed, local only): `.git/info/privacy-words`, one entry per line.
   Blank lines and `#` comments are ignored. Entries are matched case-insensitively as plain text
   and may contain spaces and backslashes, for example `D:\Some Games\Folder`. The guard never
   prints the word itself: the hit is reported as `private-word #<line in the file>` and the match
   is masked as `***`, since terminal scrollback and CI logs get shared. A missing or empty file
   skips this layer. CI has no such file and runs with `--no-private`.

The guard's own files (`privacy-patterns.txt`, `privacy-guard.sh`, `privacy-guard.test.sh`) are
skipped. To allow one line that is genuinely fine, put `privacy-guard: allow` anywhere on it.

## Install the pre-commit hook

```
bash tools/privacy/install-hook.sh
```

This writes a `pre-commit` hook (in the directory `git rev-parse --git-path hooks` names, so it
follows `core.hooksPath` and worktrees) that runs
`bash .github/scripts/privacy-guard.sh --staged`. Running it again changes nothing. If a different
`pre-commit` hook already exists it is left alone and the script prints the line to add to chain
the guard.

You can also run it by hand: `--staged` for what is about to be committed, or
`--range <base>` for everything a branch adds (`bash .github/scripts/privacy-guard.sh --range origin/main`).

## Images

Git shows no text for a PNG, so the guard cannot read pixels. For added or modified `*.png` files
it reads the text metadata chunks (`tEXt`, `zTXt`, `iTXt` and `eXIf`, inflating the compressed
ones) and scans each line of their text. This part needs `perl`, which Git for Windows, macOS and
Linux all ship. It also prints a reminder to look at each image by eye. A path
visible in a screenshot cannot be found automatically. The editor's privacy mode (issue #270)
exists to keep paths and names out of screenshots in the first place.

## Tests

`bash .github/scripts/privacy-guard.test.sh` builds throwaway repositories, so it never reads your
real `privacy-words` file or installs a hook in your clone.
