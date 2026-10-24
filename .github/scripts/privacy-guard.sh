#!/usr/bin/env bash
# Block changes that add local paths or private words. Scans only ADDED lines
# and the paths of added files, so old content never trips it.
#   Layer 1: generic patterns from .github/privacy-patterns.txt (committed).
#   Layer 2: private words from $(git rev-parse --git-path info/privacy-words)
#            (never committed; one fixed string per line, case-insensitive).
# Usage: privacy-guard.sh --staged | --range <base> [--no-private]
#   --staged  what `git diff --cached` would add (pre-commit hook)
#   --range   what `git diff <base>...HEAD` adds (CI)
# Prints hits as file:line: rule: text on stderr; exits 1 if there are any.
# Private words are masked as *** in every line printed (CI logs get shared).
# Opt out of one line with the marker "privacy-guard: allow" anywhere on it.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
patterns="$here/../privacy-patterns.txt"

mode=""; base=""; private=1
while [ $# -gt 0 ]; do
  case "$1" in
    --staged) mode=staged ;;
    --range) mode=range; shift; base=${1:-} ;;
    --no-private) private=0 ;;
    *) mode="" ; break ;;
  esac
  shift
done
if [ -z "$mode" ] || { [ "$mode" = range ] && [ -z "$base" ]; }; then
  echo "usage: privacy-guard.sh --staged | --range <base> [--no-private]" >&2
  exit 2
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
meta="$work/meta"   # one "file<TAB>line" per scanned record
texts="$work/texts" # the matching text, same line numbers as $meta
words="$work/words" # private words, one per line, "<entry number><TAB><word>"
: > "$meta"; : > "$texts"; : > "$words"

if [ "$mode" = staged ]; then
  diffargs=(--cached)
else
  diffargs=("$base...HEAD")
fi
# --no-renames: a renamed file shows up as added, so its content is scanned too.
gd() { git -c core.quotepath=off diff -U0 --no-color --no-renames --diff-filter=AM \
         --src-prefix=a/ --dst-prefix=b/ "${diffargs[@]}" "$@"; }

# Never scan the guard's own files: they have to contain the patterns.
skip='^(\.github/privacy-patterns\.txt|\.github/scripts/privacy-guard\.sh|\.github/scripts/privacy-guard\.test\.sh)$'

# Added lines. The +++ header only counts before a hunk starts, so an added line
# that itself begins with "++" is still content.
gd | awk -v mf="$meta" -v tf="$texts" '
  /^diff --git /  { hdr = 1; f = ""; next }
  hdr && /^\+\+\+ / { f = substr($0, 7); sub(/\t.*$/, "", f); if ($0 ~ /^\+\+\+ \/dev\/null/) f = ""; next }
  /^@@ /          { hdr = 0; match($0, /\+[0-9]+/); ln = substr($0, RSTART + 1, RLENGTH - 1) + 0; next }
  !hdr && /^\+/   { if (f != "") { print f "\t" ln > mf; print substr($0, 2) > tf } ln++ }
'
# Paths of added files (line 0), and PNG text metadata.
gd --name-only | awk -v mf="$meta" -v tf="$texts" -v pf="$work/pngs" '
  $0 != "" { print $0 "\t0" >> mf; print >> tf; if (tolower($0) ~ /\.png$/) print > pf }'
png=()
[ -f "$work/pngs" ] && mapfile -t png < "$work/pngs"

# PNG text metadata: the tEXt, zTXt, iTXt and eXIf chunks, inflated where they are
# compressed, one record per line of text. Pixel data (IDAT) is never read: its
# random printable runs are slow to scan and a source of false positives.
# One perl process reads every image through `git cat-file --batch`; a pipeline
# per image costs several process starts each, which is minutes on Windows.
pngscan='
use strict; use warnings; use Compress::Zlib;
open my $m, ">>", $ENV{MF} or die; open my $t, ">>", $ENV{TF} or die;
binmode STDIN; binmode $t;
for my $f (split /\n/, $ENV{PNGS}) {
  my $hdr = <STDIN>; last unless defined $hdr;
  next unless $hdr =~ / blob (\d+)$/;
  read STDIN, my $d, $1; read STDIN, my $lf, 1;
  next unless substr($d, 0, 8) eq "\x89PNG\r\n\x1a\n";
  for (my $p = 8; $p + 12 <= length $d; ) {
    my ($len, $type) = unpack "Na4", substr($d, $p, 8);
    my $c = substr($d, $p + 8, $len); $p += 12 + $len;
    my @s;
    if ($type eq "tEXt" or $type eq "eXIf") { @s = ($c) }
    elsif ($type eq "zTXt") {
      my ($k, $z) = $c =~ /^([^\0]*)\0.(.*)$/s or next;
      @s = ($k, uncompress($z) // "");
    } elsif ($type eq "iTXt") {
      my ($k, $flag, $rest) = $c =~ /^([^\0]*)\0(.).(.*)$/s or next;
      my ($lang, $tk, $text) = split /\0/, $rest, 3; $text //= "";
      $text = uncompress($text) // "" if $flag eq "\1";
      @s = ($k, $lang, $tk, $text);
    }
    for my $s (@s) {
      # Control bytes split a value into lines; other bytes (UTF-8 included) are kept.
      for my $line (split /[\x00-\x1f\x7f]+/, $s // "") {
        next if $line =~ /^\s*$/;
        print $m "$f\t0\n"; print $t "$line\n";
      }
    }
  }
}'
if [ ${#png[@]} -gt 0 ]; then
  if ! command -v perl >/dev/null 2>&1; then
    echo "privacy-guard: perl is needed to read PNG metadata (Git for Windows ships it)." >&2
    exit 2
  fi
  rev=$([ "$mode" = staged ] && echo "" || echo HEAD)
  printf "$rev:%s\n" "${png[@]}" | git cat-file --batch \
    | PNGS="$(printf '%s\n' "${png[@]}")" MF="$meta" TF="$texts" perl -e "$pngscan"
fi

# Drop records for skipped files and for lines carrying the opt-out marker,
# in one pass (a per-record loop forks a process per line: minutes on a big diff).
# The regex goes through the environment: awk -v would eat its backslashes.
SKIP="$skip" awk -v mo="$work/m2" -v to="$work/t2" '
  NR == FNR { t[FNR] = $0; next }
  { f = $0; sub(/\t.*$/, "", f)
    if (f ~ ENVIRON["SKIP"] || index(t[FNR], "privacy-guard: allow")) next
    print $0 > mo; print t[FNR] > to }
' "$texts" "$meta"
touch "$work/m2" "$work/t2"
meta="$work/m2"; texts="$work/t2"

# Private words, if the file exists and has entries.
pfile=$(git rev-parse --git-path info/privacy-words 2>/dev/null || true)
if [ "$private" = 1 ] && [ -n "$pfile" ] && [ -f "$pfile" ]; then
  n=0
  while IFS= read -r w || [ -n "$w" ]; do
    n=$((n + 1)); w=${w%$'\r'}
    case "$w" in ''|'#'*) continue ;; esac
    printf '%s\t%s\n' "$n" "$w" >> "$words"
  done < "$pfile"
fi

hits="$work/hits"; : > "$hits"
emit() { # emit <record number> <rule> <text>
  local m; m=$(sed -n "$1p" "$meta")
  printf '%s:%s: %s: %.100s\n' "${m%%$'\t'*}" "${m#*$'\t'}" "$2" "$3" >> "$hits"
}

# Layer 1. Allow rules (name starts with !) drop a hit whose matched text matches them.
allow=""
while IFS=$'\t' read -r name re; do
  case "$name" in '!'*) allow="${allow:+$allow|}$re" ;; esac
done < "$patterns"
while IFS=$'\t' read -r name re; do
  case "$name" in ''|'#'*|'!'*) continue ;; esac
  while IFS= read -r hit; do
    [ -z "$hit" ] && continue
    n=${hit%%:*}; text=${hit#*:}
    if [ -n "$allow" ] && printf '%s\n' "$text" | grep -qE -- "$allow"; then continue; fi
    emit "$n" "$name" "$text"
  done < <(grep -a -n -o -E -- "$re" "$texts")
done < "$patterns"

# Layer 2.
while IFS=$'\t' read -r num w; do
  [ -z "${w:-}" ] && continue
  while IFS=: read -r n _; do
    emit "$n" "private-word #$num" "$(sed -n "${n}p" "$texts")"
  done < <(grep -a -n -i -F -e "$w" "$texts")
done < "$words"

# Mask every private word in everything we print, then show the hits.
mask() {
  if [ -s "$words" ]; then
    PW="$(cut -f2- "$words")" awk '
      BEGIN { n = split(ENVIRON["PW"], w, "\n") }
      { line = $0; low = tolower(line)
        for (i = 1; i <= n; i++) {
          if (w[i] == "") continue
          k = tolower(w[i]); out = ""
          while ((p = index(low, k)) > 0) {
            out = out substr(line, 1, p - 1) "***"
            line = substr(line, p + length(k)); low = substr(low, p + length(k))
          }
          line = out line; low = tolower(line)
        }
        print line }'
  else
    cat
  fi
}

if [ ${#png[@]} -gt 0 ]; then
  echo "Check these images by eye for paths or private text: ${png[*]}" | mask
fi
if [ -s "$hits" ]; then
  { echo "privacy-guard: found local paths or private words in added content:"
    sort -t: -k1,1 -k2,2n "$hits"
    echo "Fix them, or add 'privacy-guard: allow' to a line that is genuinely fine."; } | mask >&2
  exit 1
fi
echo "privacy-guard: ok"
