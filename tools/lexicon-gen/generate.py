#!/usr/bin/env python3
"""Generate per-language game lexicons (spell-check layer 2) from shipped
localized stringtables.

Permanent, committed tool — kept so lexicons can be regenerated for future
languages, after game patches, or for curation refreshes.
See docs/superpowers/specs/2026-07-11-spell-checker-design.md and README.md.
"""
import argparse
import glob
import os
import re
from collections import Counter

# Prefer defusedxml (XXE / entity-expansion hardening) when available; the
# stdlib fallback keeps the tool's tests dependency-free. Input is normally
# the local game install's own stringtables, but there's no reason not to be
# safe when defusedxml is present (pip install defusedxml).
try:
    import defusedxml.ElementTree as ET  # type: ignore
    from xml.etree.ElementTree import ParseError as _XmlParseError
except ImportError:  # pragma: no cover - environment-dependent
    import xml.etree.ElementTree as ET
    _XmlParseError = ET.ParseError

# Same span rules as the app's TokenValidationService: bracket tokens and
# markup tags are never spell-checked.
_BRACKET = re.compile(r"\[[^\[\]\n\r]*\]")
_MARKUP = re.compile(r"<[^>]*>")

# Tokens: word chars + apostrophes/hyphens. Tokens containing digits ("3rd")
# are skipped entirely rather than yielding letter fragments ("rd"); single
# letters and pure numbers are not words. Mirrors SpellCheckService.
_TOKEN = re.compile(r"[\w'’-]+", re.UNICODE)
_HAS_DIGIT = re.compile(r"[\d_]")


def strip_tags(text):
    """Remove [token] and <markup> spans (content of brackets removed, markup
    tag names removed but their inner text kept by virtue of tags being
    matched individually)."""
    text = _BRACKET.sub(" ", text)
    return _MARKUP.sub(" ", text)


def tokenize(text):
    """Words of letters + internal apostrophes/hyphens; tokens containing
    digits, single letters, and pure numbers dropped."""
    out = []
    for tok in _TOKEN.findall(text):
        if _HAS_DIGIT.search(tok):
            continue
        tok = tok.strip("'’-")
        if len(tok) > 1:
            out.append(tok)
    return out


def _entry_texts(stringtable_path):
    """Yield the text of every DefaultText/FemaleText entry. Uses itertext()
    so both escaped markup (&lt;i&gt;) and raw mixed-content markup parse."""
    try:
        root = ET.parse(stringtable_path).getroot()
    except _XmlParseError:
        return  # tolerate the odd malformed file
    for entry in root.iter("Entry"):
        for tag in ("DefaultText", "FemaleText"):
            el = entry.find(tag)
            if el is not None:
                text = "".join(el.itertext())
                if text.strip():
                    yield text


def count_words(stringtable_dir):
    """Case-insensitive word counts over every *.stringtable under the dir.
    Keys are lower-case; a representative original casing is tracked so the
    emitted lexicon stays human-readable."""
    counts = Counter()
    casing = {}
    for path in glob.glob(os.path.join(stringtable_dir, "**", "*.stringtable"),
                          recursive=True):
        for text in _entry_texts(path):
            for word in tokenize(strip_tags(text)):
                key = word.lower()
                counts[key] += 1
                casing.setdefault(key, word)
    counts.casing = casing  # attached for emit(); Counter subclassing is overkill
    return counts


def subtract(counter, checker):
    """Drop every word the layer-1 dictionary accepts. `checker` needs only
    .lookup(word) -> bool (spylls Dictionary satisfies this)."""
    out = Counter({w: c for w, c in counter.items() if not checker.lookup(w)})
    out.casing = getattr(counter, "casing", {})
    return out


def emit(counter, out_path):
    """word<TAB>count, descending count then alphabetical. Counts are kept for
    curation (count-1 oddities vs 300-occurrence 'adra') per the tags.json
    precedent."""
    casing = getattr(counter, "casing", {})
    rows = sorted(counter.items(), key=lambda kv: (-kv[1], kv[0]))
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        for word, count in rows:
            f.write(f"{casing.get(word, word)}\t{count}\n")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--game-dirs", nargs="+", required=True,
                    help="localized conversation-text roots (both games)")
    ap.add_argument("--dict-aff", help=".aff of the layer-1 dictionary (optional)")
    ap.add_argument("--dict-dic", help=".dic of the layer-1 dictionary (optional)")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)

    total = Counter()
    casing = {}
    for d in a.game_dirs:
        c = count_words(d)
        total.update(c)
        for k, v in getattr(c, "casing", {}).items():
            casing.setdefault(k, v)
    total.casing = casing
    print(f"raw words: {len(total)}")

    if a.dict_aff and a.dict_dic:
        from spylls.hunspell import Dictionary
        base = os.path.splitext(a.dict_dic)[0]
        checker = Dictionary.from_files(base)
        total = subtract(total, checker)
        print(f"after subtracting dictionary: {len(total)}")

    emit(total, a.out)
    print(f"wrote {a.out}")


if __name__ == "__main__":
    main()
