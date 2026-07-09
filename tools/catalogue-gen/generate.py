#!/usr/bin/env python3
"""Regenerate the script/condition catalogue from the decompiled game sources.

See tools/catalogue-gen/README.md and
docs/superpowers/specs/2026-07-07-catalogue-regeneration-design.md
"""
import argparse
import glob
import json
import os
import re

# ── CLR type spelling (for fullName + `type` field) ─────────────────────────
_KEYWORD = {
    "void": "Void", "int": "Int32", "bool": "Boolean", "float": "Single",
    "string": "String", "double": "Double", "long": "Int64", "uint": "UInt32",
    "short": "Int16", "byte": "Byte", "object": "Object", "char": "Char",
}


def _clr(t):
    """CLR reflection short name for a C# type token (e.g. int->Int32, A.B->B)."""
    t = t.strip().rstrip("?")
    if t in _KEYWORD:
        return _KEYWORD[t]
    if "." in t:
        t = t.split(".")[-1]
    return t


# ── BrowserType -> lookupKind (GameData resolved separately) ────────────────
_BROWSER_KIND = {
    "GlobalVariable": "GlobalVariable",
    "Conversation": "Conversation",
    "Quest": "Quest",
    "ObjectGuid": "Speaker",
    "Chatter": "Speaker",
    # GameData -> resolved via default GUID; None/GlobalScript/GlobalConditional/
    # GlobalPreference -> no lookup.
}

# GameData $type -> lookupKind. Any *ItemGameData -> Item; otherwise strip the
# trailing "GameData". A few explicit overrides keep names aligned with the
# runtime loaders (GameDataNameService kinds).
# MUST mirror DialogEditor.Core/GameData/GameDataKindMapper.cs (the runtime
# sweep resolves the kind names this generator stamps into the catalogue).
_TYPE_KIND_OVERRIDE = {
    "BaseStatsGameData": "Class",
}

# Align a few $type-derived kind names to the runtime GameDataNameService kinds so
# the existing loader serves them. Only mappings whose objects share the loader's
# GUID space are listed; everything else stays $type-derived (dormant until a
# matching loader exists — safe per the emit-dormant policy).
_KIND_ALIAS = {
    "GenericAbility": "Ability",   # abilities.gamedatabundle is loaded as "Ability"
}

# Deliberate corrections where the decompiled source's BrowserType is unhelpful.
# (method name, param index) -> lookupKind. These faction-reputation conditions
# mark their faction reference as ObjectGuid (Speaker), but store faction GUIDs,
# so the Speaker list never matches — use Faction instead (see BUGS.md B-010).
_PARAM_KIND_OVERRIDE = {
    ("ReputationRankEquals", 0): "Faction",
    ("ReputationRankGreater", 0): "Faction",
}


def type_to_kind(typename):
    if typename in _TYPE_KIND_OVERRIDE:
        return _KIND_ALIAS.get(_TYPE_KIND_OVERRIDE[typename], _TYPE_KIND_OVERRIDE[typename])
    if typename.endswith("ItemGameData"):
        return "Item"
    kind = typename[: -len("GameData")] if typename.endswith("GameData") else typename
    return _KIND_ALIAS.get(kind, kind)


# ── attribute argument splitting ────────────────────────────────────────────
def _split_args(argstr):
    """Top-level comma split respecting double-quoted strings."""
    out, buf, inq = [], [], False
    i = 0
    while i < len(argstr):
        c = argstr[i]
        if c == '"':
            inq = not inq
            buf.append(c)
        elif c == "," and not inq:
            out.append("".join(buf).strip())
            buf = []
        else:
            buf.append(c)
        i += 1
    if buf:
        out.append("".join(buf).strip())
    return out


def _unquote(s):
    s = s.strip()
    if len(s) >= 2 and s[0] == '"' and s[-1] == '"':
        return s[1:-1]
    return s


_NUM = re.compile(r"^-?\d+(?:\.\d+)?[fFdDmM]?$")


def _render_default(raw):
    d = raw.strip()
    if not d:
        return ""
    if d[0] == '"':
        return _unquote(d)
    if _NUM.match(d):
        return d.rstrip("fFdDmM")
    if "." in d:  # enum member like Axis.Positive
        return d.split(".")[-1]
    return d  # true/false/bare identifier


_BROWSER_RE = re.compile(r"^(?:Scripts\.)?BrowserType\.(\w+)$")
_GUID_RE = re.compile(r'"([0-9a-fA-F-]{36})"')


def parse_enums(text):
    """name -> [members] for every `enum Name { ... }` in the text."""
    out = {}
    for m in re.finditer(r"\benum\s+(\w+)\s*\{([^}]*)\}", text):
        name = m.group(1)
        members = []
        for part in m.group(2).split(","):
            part = part.strip()
            if not part:
                continue
            members.append(part.split("=")[0].strip())
        out[name] = members
    return out


def parse_datatype_index(text):
    """DataTypeID GUID -> GameData class name, from `class XxxGameData ... const
    string DataTypeID = "guid"` in decompiled source text."""
    out = {}
    for m in re.finditer(
        r"class\s+(\w+GameData)\b.*?DataTypeID\s*=\s*\"([0-9a-fA-F-]{36})\"",
        text, re.DOTALL,
    ):
        out[m.group(2).lower()] = m.group(1)
    return out


_ATTR_RE = re.compile(r"^\[(\w+)\((.*)\)\]$")
_METHOD_RE = re.compile(r"public\s+static\s+(\S+)\s+(\w+)\s*\((.*?)\)")


def _sig_param_types(paramlist):
    """CLR short types of a signature's parameters, in order."""
    paramlist = paramlist.strip()
    if not paramlist:
        return []
    types = []
    for p in _split_args(paramlist):
        p = p.strip()
        if not p:
            continue
        # drop modifiers, then type is everything but the last (name) token
        toks = p.replace("params ", "").replace("ref ", "").replace("out ", "").split()
        types.append(_clr(" ".join(toks[:-1])) if len(toks) >= 2 else _clr(toks[0]))
    return types


_EXPOSE_ATTRS = ("Script", "ConditionalScript")


def parse_source(text, kind=None, enums=None, guid_index=None, game=None, datatype_index=None):
    """Parse decompiled Scripts.cs / Conditionals.cs text into catalogue entries.

    A method is catalogue-exposed if it carries [Script] or [ConditionalScript]
    (either attribute may appear in either file — some void 'scripts' are decorated
    with [ConditionalScript] and vice versa). Its kind is derived from the return
    type: bool -> condition, else script. `kind`, if given, filters to that kind.
    """
    enums = enums or {}
    guid_index = guid_index or {}
    datatype_index = datatype_index or {}

    entries = []
    attrs = []  # list of (attrname, argstr)
    for line in text.splitlines():
        s = line.strip()
        am = _ATTR_RE.match(s)
        if am:
            attrs.append((am.group(1), am.group(2)))
            continue
        mm = _METHOD_RE.search(s)
        if mm and any(a in _EXPOSE_ATTRS for a, _ in attrs):
            e = _build_entry(mm, attrs, enums, guid_index, game, datatype_index)
            classified = "condition" if _clr(mm.group(1)) == "Boolean" else "script"
            e["_kind"] = classified
            if kind is None or kind == classified:
                entries.append(e)
            attrs = []
            continue
        if s == "" or not s.startswith("["):
            attrs = []
    return entries


def _build_entry(mm, attrs, enums, guid_index, game, datatype_index):
    ret, name, paramlist = mm.group(1), mm.group(2), mm.group(3)
    clr_types = _sig_param_types(paramlist)
    full = f"{_clr(ret)} {name}({', '.join(clr_types)})"

    display, category = name, ""
    param_attrs = {}
    for aname, argstr in attrs:
        if aname in _EXPOSE_ATTRS:
            args = _split_args(argstr)
            if args:
                display = _unquote(args[0])
            if len(args) >= 2:
                path = _unquote(args[1])
                category = re.split(r"\\+", path)[-1]
        else:
            pm = re.match(r"ScriptParam(\d+)$", aname)
            if pm:
                param_attrs[int(pm.group(1))] = _split_args(argstr)

    params = []
    for i, clr in enumerate(clr_types):
        pa = param_attrs.get(i)
        p = _build_param(i, clr, pa, enums, guid_index, datatype_index)
        override = _PARAM_KIND_OVERRIDE.get((name, i))
        if override:
            p["type"] = "GameData"
            p["lookupKind"] = override
        params.append(p)

    return {
        "methodName": name,
        "fullName": full,
        "displayName": display,
        "category": category,
        "games": [game] if game else [],
        "description": "",
        "parameters": params,
    }


def _build_param(index, clr, pa, enums, guid_index, datatype_index):
    p = {"name": f"arg{index}", "type": clr, "description": "", "default": ""}
    if not pa:
        # No ScriptParamN attribute: still classify enums for the editor.
        if clr in enums:
            p["type"] = f"Enum:{clr}"
            p["options"] = enums[clr]
        return p

    p["name"] = _unquote(pa[0]) if pa else p["name"]
    p["description"] = _unquote(pa[1]) if len(pa) >= 2 else ""
    rest = pa[2:]

    browser = None
    if rest:
        bm = _BROWSER_RE.match(rest[-1])
        if bm:
            browser = bm.group(1)
            rest = rest[:-1]

    if browser:
        _apply_browser(p, clr, browser, rest, guid_index, datatype_index)
    else:
        # enum or value default
        if clr in enums:
            p["type"] = f"Enum:{clr}"
            p["options"] = enums[clr]
        if rest:
            p["default"] = _render_default(rest[0])
    return p


def _apply_browser(p, clr, browser, rest, guid_index, datatype_index):
    # GUIDs among the middle args. For the 5-arg GameData form the LAST is the
    # DataTypeID (type identifier); for the 4-arg form the only GUID is a default
    # instance value.
    guids = [_unquote(x) for x in rest if _GUID_RE.match(x.strip())]

    if browser == "ObjectGuid" or browser == "Chatter":
        p["type"] = "ObjectGuid"
        p["lookupKind"] = "Speaker"
    elif browser == "GameData":
        p["type"] = "GameData"
        kind = None
        if guids:
            # DataTypeID is the last GUID (the arg immediately before browser).
            cls = datatype_index.get(guids[-1].lower())
            if cls:
                kind = type_to_kind(cls)
            else:
                # 4-arg form: resolve a default instance GUID via the bundle index.
                for g in reversed(guids):
                    t = guid_index.get(g.lower())
                    if t:
                        kind = type_to_kind(t)
                        break
        p["lookupKind"] = kind or "GameData"
    elif browser in ("Quest", "Conversation"):
        p["type"] = "GameData"
        p["lookupKind"] = _BROWSER_KIND[browser]
    elif browser == "GlobalVariable":
        # variable name is a string; keep the CLR type
        p["lookupKind"] = "GlobalVariable"
    # GlobalScript/GlobalConditional/GlobalPreference/None -> no lookup


# ── merge both games ────────────────────────────────────────────────────────
def merge_games(entries_a, entries_b):
    by_fn = {}
    order = []
    for e in list(entries_a) + list(entries_b):
        fn = e["fullName"]
        if fn not in by_fn:
            by_fn[fn] = {k: v for k, v in e.items()}
            by_fn[fn]["games"] = list(e.get("games", []))
            order.append(fn)
        else:
            for g in e.get("games", []):
                if g not in by_fn[fn]["games"]:
                    by_fn[fn]["games"].append(g)
    for fn in order:
        by_fn[fn]["games"] = sorted(by_fn[fn]["games"])
    return [by_fn[fn] for fn in order]


# ── bundle index ────────────────────────────────────────────────────────────
def build_guid_type_index(bundle_dir):
    idx = {}
    for path in glob.glob(os.path.join(bundle_dir, "*.gamedatabundle")):
        try:
            data = json.load(open(path, encoding="utf-8-sig"))
        except Exception:
            continue
        for obj in data.get("GameDataObjects", []):
            gid = str(obj.get("ID", "")).lower()
            t = obj.get("$type", "")
            if gid and t:
                short = t.split(",")[0].split(".")[-1]
                idx[gid] = short
    return idx


def load_datatype_index(code_dir):
    """DataTypeID GUID -> GameData class name across all .cs under code_dir."""
    idx = {}
    for path in glob.glob(os.path.join(code_dir, "**", "*.cs"), recursive=True):
        try:
            idx.update(parse_datatype_index(open(path, encoding="utf-8", errors="ignore").read()))
        except Exception:
            continue
    return idx


def load_enums_from_dir(code_dir):
    enums = {}
    for path in glob.glob(os.path.join(code_dir, "**", "*.cs"), recursive=True):
        try:
            enums.update(parse_enums(open(path, encoding="utf-8", errors="ignore").read()))
        except Exception:
            continue
    return enums


# ── output ──────────────────────────────────────────────────────────────────
def _clean_entry(e):
    """Drop empty optional keys to match the existing file style."""
    out = {}
    for k in ("methodName", "fullName", "displayName", "category", "games", "description"):
        out[k] = e[k]
    params = []
    for p in e["parameters"]:
        q = {"name": p["name"], "type": p["type"],
             "description": p["description"], "default": p["default"]}
        if p.get("options"):
            q["options"] = p["options"]
        if p.get("values"):
            q["values"] = p["values"]
        if p.get("lookupKind"):
            q["lookupKind"] = p["lookupKind"]
        params.append(q)
    out["parameters"] = params
    return out


def _sort_key(e):
    return (e["category"], e["displayName"], e["fullName"])


def write_json(entries, *paths):
    entries = [_clean_entry(e) for e in sorted(entries, key=_sort_key)]
    for path in paths:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(entries, f, indent=2, ensure_ascii=False)
            f.write("\n")


def emit_usage(conversations_dir, out_path):
    used = set()

    def walk(o):
        if isinstance(o, dict):
            fn = o.get("FullName")
            if isinstance(fn, str) and fn:
                used.add(fn)
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    for path in glob.glob(os.path.join(conversations_dir, "**", "*.conversationbundle"), recursive=True):
        try:
            walk(json.load(open(path, encoding="utf-8-sig")))
        except Exception:
            continue
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        for fn in sorted(used):
            f.write(fn + "\n")
    return len(used)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--poe1-scripts")
    ap.add_argument("--poe1-conditions")
    ap.add_argument("--poe2-scripts", required=True)
    ap.add_argument("--poe2-conditions", required=True)
    ap.add_argument("--poe1-code")
    ap.add_argument("--poe2-code", required=True)
    ap.add_argument("--bundles", required=True)
    ap.add_argument("--conversations")
    ap.add_argument("--repo", required=True)
    a = ap.parse_args(argv)

    enums = load_enums_from_dir(a.poe2_code)
    if a.poe1_code:
        for k, v in load_enums_from_dir(a.poe1_code).items():
            enums.setdefault(k, v)
    guid_index = build_guid_type_index(a.bundles)
    datatype_index = load_datatype_index(a.poe2_code)
    if a.poe1_code:
        for k, v in load_datatype_index(a.poe1_code).items():
            datatype_index.setdefault(k, v)

    def read(p):
        return open(p, encoding="utf-8", errors="ignore").read() if p and os.path.exists(p) else ""

    def parse(path, game):
        # Parse a file for ALL exposed methods; classification (script/condition)
        # is by return type, not by which file the method lives in.
        return parse_source(read(path), None, enums, guid_index, game, datatype_index)

    p2 = parse(a.poe2_scripts, "poe2") + parse(a.poe2_conditions, "poe2")
    p1 = parse(a.poe1_scripts, "poe1") + parse(a.poe1_conditions, "poe1")

    def bucket(entries, kind):
        return [e for e in entries if e.get("_kind") == kind]

    scripts = merge_games(bucket(p1, "script"), bucket(p2, "script"))
    conds = merge_games(bucket(p1, "condition"), bucket(p2, "condition"))

    write_json(scripts,
               os.path.join(a.repo, "DialogEditor.ViewModels", "Resources", "scripts.json"),
               os.path.join(a.repo, "data", "scripts.json"))
    write_json(conds,
               os.path.join(a.repo, "DialogEditor.ViewModels", "Resources", "conditions.json"),
               os.path.join(a.repo, "data", "conditions.json"))
    print(f"scripts: {len(scripts)}  conditions: {len(conds)}")

    if a.conversations:
        n = emit_usage(a.conversations,
                       os.path.join(a.repo, "DialogEditor.Tests", "Fixtures", "catalogue-usage.txt"))
        print(f"usage signatures: {n}")


if __name__ == "__main__":
    main()
