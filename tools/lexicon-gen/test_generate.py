import os
import tempfile
from collections import Counter

import generate

FIXTURE_STRINGTABLE = """<?xml version="1.0" encoding="utf-8"?>
<StringTableFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Name>conversations\\test\\fixture</Name>
  <NextEntryID>1</NextEntryID>
  <EntryCount>3</EntryCount>
  <Entries>
    <Entry>
      <ID>1</ID>
      <DefaultText>The adra glows near [Player Name] tonight.</DefaultText>
      <FemaleText>The adra glows, <i>captain</i>.</FemaleText>
    </Entry>
    <Entry>
      <ID>2</ID>
      <DefaultText>Kith of Rauatai's storm-called fleet, 3 ships strong.</DefaultText>
      <FemaleText />
    </Entry>
  </Entries>
</StringTableFile>
"""


def test_strip_tags():
    s = generate.strip_tags("Hi [Player Name], <i>rest</i> now.")
    assert "[" not in s and "]" not in s
    assert "<" not in s and ">" not in s
    assert "Player" not in s          # token content removed, not just brackets
    assert "rest" in s and "Hi" in s  # markup content kept, tags removed


def test_tokenize():
    words = generate.tokenize("Rauatai's storm-called 3 ships x")
    assert words == ["Rauatai's", "storm-called", "ships"]  # numbers + single letters dropped


def test_count_words():
    with tempfile.TemporaryDirectory() as d:
        sub = os.path.join(d, "conv")
        os.makedirs(sub)
        with open(os.path.join(sub, "fixture.stringtable"), "w", encoding="utf-8") as f:
            f.write(FIXTURE_STRINGTABLE)
        c = generate.count_words(d)
        assert c["adra"] == 2            # once in DefaultText, once in FemaleText
        assert c["the"] >= 2             # case-insensitive counting
        assert "player" not in c         # token content stripped
        assert c["captain"] == 1         # markup tags stripped, content kept


def test_subtract():
    class FakeChecker:
        def lookup(self, w):
            return w.lower() in ("the", "ships")

    c = generate.subtract(Counter({"the": 5, "adra": 2, "ships": 1}), FakeChecker())
    assert "the" not in c and "ships" not in c
    assert c["adra"] == 2


def test_emit():
    with tempfile.TemporaryDirectory() as d:
        out = os.path.join(d, "en.txt")
        generate.emit(Counter({"adra": 2, "kith": 1, "aedyr": 2}), out)
        lines = open(out, encoding="utf-8").read().splitlines()
        # descending count, then alphabetical
        assert lines == ["adra\t2", "aedyr\t2", "kith\t1"]


if __name__ == "__main__":
    fns = [v for k, v in sorted(globals().items()) if k.startswith("test_") and callable(v)]
    for fn in fns:
        fn()
        print("ok", fn.__name__)
    print(f"{len(fns)} passed")
