#!/usr/bin/env bash
# Tests for privacy-guard.sh and tools/privacy/install-hook.sh.
# Run: bash .github/scripts/privacy-guard.test.sh
# Every case builds a throwaway git repo under mktemp, so the real repo's
# .git/info/privacy-words and hooks are never read or written.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
guard="$here/privacy-guard.sh"
patterns="$here/../privacy-patterns.txt"
installer="$here/../../tools/privacy/install-hook.sh"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
failures=0
out="$tmp/out.txt"

report() { # report <pass|fail> <description> <got>
  if [ "$1" = "$3" ]; then echo "ok   $2"; else echo "FAIL $2 (wanted $1, got $3)"; failures=$((failures + 1)); fi
}

new_repo() { # new_repo -> sets $repo; one committed base file
  repo=$(mktemp -d "$tmp/r.XXXXXX")
  git -C "$repo" init -q -b main
  git -C "$repo" config user.email t@example.com
  git -C "$repo" config user.name t
  git -C "$repo" config core.autocrlf false
  printf 'base\n' > "$repo/base.txt"
  git -C "$repo" add base.txt
  git -C "$repo" commit -q -m base
}

private() { # private <content...>: write the private-words file of $repo (printf format per arg)
  local f; f=$(git -C "$repo" rev-parse --git-path info/privacy-words)
  case "$f" in /*|[A-Za-z]:*) ;; *) f="$repo/$f" ;; esac
  mkdir -p "$(dirname "$f")"
  printf '%b' "$1" > "$f"
}

run() { (cd "$repo" && bash "$guard" "$@") > "$out" 2>&1; }

staged() { # staged <pass|fail> <description> <file> <content> [guard args]: stage one file, scan it
  local want=$1 desc=$2 file=$3 content=$4; shift 4
  mkdir -p "$(dirname "$repo/$file")"
  printf '%s\n' "$content" > "$repo/$file"
  git -C "$repo" add -- "$file"
  if run --staged "$@"; then got=pass; else got=fail; fi
  report "$want" "$desc" "$got"
}

# fresh repo for each generic case
generic() { new_repo; staged "$@"; }

# --- Layer 1: generic patterns ---
generic pass "clean change passes"                         a.txt 'nothing to see here'
generic fail "windows backslash user path"                 a.txt 'open C:\Users\bob\Documents\x'
generic fail "windows forward-slash user path"             a.txt 'open C:/Users/bob/Documents/x'
generic fail "windows doubled-backslash user path"         a.txt '{"p":"C:\\Users\\bob\\x"}'
generic fail "appdata path with a concrete user"           a.txt 'log in D:\Profiles\bob\AppData\Local\App\a.log'
generic fail "/home/<name>/"                               a.txt 'cd /home/bob/projects'
generic fail "/Users/<name>/ (macOS)"                      a.txt 'cd /Users/bob/projects'
generic pass "placeholder %USERPROFILE%"                   a.txt 'in %USERPROFILE%\Documents'
generic pass "placeholder %LOCALAPPDATA%"                  a.txt 'in %LOCALAPPDATA%\PillarsDialogEditor\app.log'
generic pass "placeholder %TEMP%"                          a.txt 'in %TEMP%\x'
generic pass "placeholder <user>"                          a.txt 'C:\Users\<user>\Documents'
generic pass "placeholder <name>"                          a.txt 'C:\Users\<name>\AppData\Local\x\'
generic pass "placeholder \$env:"                          a.txt 'C:\Users\$env:USERNAME\x and $env:LOCALAPPDATA\AppData\Local\x'
generic pass "placeholder ellipsis"                        a.txt 'C:\Users\...\x and C:\Users\…\x'
generic pass "C:\\Users\\Public is shared"                 a.txt 'C:\Users\Public\Documents'
generic pass "home-relative AppData"                       a.txt '~/AppData/Local/x and $HOME/AppData/Roaming/y'
generic pass "game-store default folders are not generic"  a.txt 'C:\GOG Galaxy\Games\x and steamapps\common\y'
generic pass "opt-out marker allows the line"              a.txt 'C:\Users\bob\x  # privacy-guard: allow'

# only added lines count
new_repo
printf 'old C:\\Users\\bob\\x\nkeep\n' > "$repo/p.txt"
git -C "$repo" add p.txt; git -C "$repo" commit -q -m p
printf 'old C:\\Users\\bob\\x\nkeep\nadded\n' > "$repo/p.txt"
git -C "$repo" add p.txt
if run --staged; then got=pass; else got=fail; fi
report pass "pre-existing path on an unchanged line passes" "$got"
printf 'keep\nadded\n' > "$repo/p.txt"
git -C "$repo" add p.txt
if run --staged; then got=pass; else got=fail; fi
report pass "removed line with a path passes" "$got"

# the guard's own files are skipped
new_repo; staged pass "own pattern file is skipped" .github/privacy-patterns.txt 'C:\Users\bob\x'
new_repo; staged pass "own script is skipped"       .github/scripts/privacy-guard.sh 'C:\Users\bob\x'
new_repo; staged pass "own test is skipped"         .github/scripts/privacy-guard.test.sh 'C:\Users\bob\x'

# the report names file, line and rule
new_repo
printf 'one\ntwo C:\\Users\\bob\\x\n' > "$repo/n.txt"; git -C "$repo" add n.txt
run --staged
if grep -q '^n.txt:2: windows-user-path: ' "$out"; then got=pass; else got=fail; fi
report pass "report format file:line: rule: text" "$got"

# --- Layer 2: private words ---
new_repo; private '# comment\n\nacmesecret\n'
staged fail "private word, case-insensitively" a.txt 'the AcmeSecret project'
if grep -qi 'acmesecret' "$out"; then got=fail; else got=pass; fi
report pass "private word is not echoed" "$got"
if grep -q 'private-word #3' "$out" && grep -q '\*\*\*' "$out"; then got=pass; else got=fail; fi
report pass "private hit names the entry number and masks the text" "$got"

new_repo; private 'D:\\Some Games\\Folder\n'
staged fail "private entry with spaces and backslashes" a.txt 'see d:\some games\folder\x'

new_repo; private 'acmesecret\r\nother\r\n'
staged fail "CRLF private file" a.txt 'x acmesecret y'

new_repo; private 'acmesecret\n'
staged fail "private word in a file NAME" docs/acmesecret-notes.md 'harmless'
if grep -qi 'acmesecret' "$out"; then got=fail; else got=pass; fi
report pass "private word in a file name is not echoed" "$got"

new_repo; private 'acmesecret\n'
staged fail "private word and generic hit on one line: word masked" a.txt 'acmesecret C:\Users\bob\x'
if grep -qi 'acmesecret' "$out"; then got=fail; else got=pass; fi
report pass "generic hit on a private line does not echo the word" "$got"

new_repo
staged pass "missing private file: only generic layer" a.txt 'acmesecret'
new_repo; private ''
staged pass "empty private file: only generic layer" a.txt 'acmesecret'
new_repo; private '# only comments\n\n'
staged pass "comment-only private file: only generic layer" a.txt 'acmesecret'
new_repo; private 'acmesecret\n'
staged pass "--no-private ignores the private file" a.txt 'acmesecret' --no-private
new_repo; private 'acmesecret\n'
staged fail "--no-private still applies the generic layer" a.txt 'C:\Users\bob\x' --no-private

# --- PNG metadata ---
png() { # png <file> <tEXt text>: a tiny PNG with one tEXt chunk (CRCs are bogus; the guard only scans bytes)
  { printf '\211PNG\r\n\032\n'; printf '\0\0\0\15IHDR\0\0\0\1\0\0\0\1\10\2\0\0\0\220wS\336'
    printf '\0\0\0\100tEXt'; printf 'Comment\0%s' "$2"; printf '\0\0\0\0'
    printf '\0\0\0\0IEND\256B`\202'; } > "$1"
}
new_repo; png "$repo/a.png" 'saved to C:\Users\bob\Pictures\x.png'; git -C "$repo" add a.png
if run --staged; then got=pass; else got=fail; fi
report fail "PNG tEXt chunk with a user path" "$got"
if grep -q 'Check these images by eye for paths or private text: .*a.png' "$out"; then got=pass; else got=fail; fi
report pass "PNG reminder is printed" "$got"
new_repo; png "$repo/a.png" 'a harmless caption'; git -C "$repo" add a.png
if run --staged; then got=pass; else got=fail; fi
report pass "PNG with harmless metadata passes" "$got"
new_repo; private 'acmesecret\n'; png "$repo/a.png" 'made for AcmeSecret'; git -C "$repo" add a.png
if run --staged; then got=pass; else got=fail; fi
report fail "PNG tEXt chunk with a private word" "$got"

# --- --range mode ---
new_repo
git -C "$repo" checkout -q -b topic
printf 'fine\n' > "$repo/ok.txt"; git -C "$repo" add ok.txt; git -C "$repo" commit -q -m ok
if run --range main --no-private; then got=pass; else got=fail; fi
report pass "--range on a clean commit range" "$got"
printf 'C:\\Users\\bob\\x\n' > "$repo/bad.txt"; git -C "$repo" add bad.txt; git -C "$repo" commit -q -m bad
base=$(git -C "$repo" rev-parse HEAD~2)
if run --range "$base" --no-private; then got=pass; else got=fail; fi
report fail "--range finds a path added in a later commit" "$got"
if grep -q '^bad.txt:1: windows-user-path' "$out"; then got=pass; else got=fail; fi
report pass "--range report format" "$got"
printf 'C:\\Users\\bob\\x\n' > "$repo/bad.txt"
git -C "$repo" rm -q --cached bad.txt; git -C "$repo" commit -q -m rm
if run --range HEAD~1 --no-private; then got=pass; else got=fail; fi
report pass "--range ignores a file deleted in the range" "$got"
png "$repo/b.png" 'C:\Users\bob\x'; git -C "$repo" add b.png; git -C "$repo" commit -q -m png
if run --range HEAD~1 --no-private; then got=pass; else got=fail; fi
report fail "--range scans PNG metadata" "$got"

# --- usage ---
new_repo
if run; then got=pass; else got=fail; fi
report fail "no arguments is a usage error" "$got"

# --- install-hook.sh ---
hook_repo() { # hook_repo: repo with the guard and patterns committed
  new_repo
  mkdir -p "$repo/.github/scripts"
  cp "$guard" "$repo/.github/scripts/privacy-guard.sh"
  cp "$patterns" "$repo/.github/privacy-patterns.txt"
  git -C "$repo" add .github; git -C "$repo" commit -q -m guard
}
hook_repo
if (cd "$repo" && bash "$installer") >/dev/null 2>&1; then got=pass; else got=fail; fi
report pass "installer installs a pre-commit hook" "$got"
hookfile=$(cd "$repo" && git rev-parse --git-path hooks)/pre-commit
case "$hookfile" in /*|[A-Za-z]:*) ;; *) hookfile="$repo/$hookfile" ;; esac
if [ -f "$hookfile" ]; then got=pass; else got=fail; fi
report pass "hook file exists" "$got"
cp "$hookfile" "$tmp/hook.before"
if (cd "$repo" && bash "$installer") >/dev/null 2>&1 && cmp -s "$hookfile" "$tmp/hook.before"; then got=pass; else got=fail; fi
report pass "installer is idempotent" "$got"
printf 'C:\\Users\\bob\\x\n' > "$repo/bad.txt"; git -C "$repo" add bad.txt
if git -C "$repo" commit -q -m bad >/dev/null 2>&1; then got=pass; else got=fail; fi
report fail "installed hook blocks a commit with a user path" "$got"
git -C "$repo" rm -q -f --cached bad.txt; printf 'fine\n' > "$repo/good.txt"; git -C "$repo" add good.txt
if git -C "$repo" commit -q -m good >/dev/null 2>&1; then got=pass; else got=fail; fi
report pass "installed hook allows a clean commit" "$got"

hook_repo
hookfile=$(cd "$repo" && git rev-parse --git-path hooks)/pre-commit
case "$hookfile" in /*|[A-Za-z]:*) ;; *) hookfile="$repo/$hookfile" ;; esac
mkdir -p "$(dirname "$hookfile")"; printf '#!/bin/sh\necho mine\n' > "$hookfile"
if (cd "$repo" && bash "$installer") > "$out" 2>&1; then got=pass; else got=fail; fi
report fail "installer refuses to overwrite a different hook" "$got"
if grep -q 'echo mine' "$hookfile" && grep -qi 'privacy-guard.sh --staged' "$out"; then got=pass; else got=fail; fi
report pass "foreign hook is untouched and chaining is explained" "$got"

echo
if [ "$failures" -eq 0 ]; then echo "all passed"; else echo "$failures failed"; exit 1; fi
