"""Inspect serialized unit spellbooks in a WOTR save archive.

Read-only: opens the .zks zip, parses party.json/player.json, and reports
per-spellbook known-spell list sizes and duplicate blueprint occurrences.

Usage:
    python scripts/inspect_save_spellbooks.py <save.zks> [--dump]
"""
from __future__ import annotations

import json
import sys
import zipfile
from collections import Counter


def walk(o, path=""):
    if isinstance(o, dict):
        yield path, o
        for k, v in o.items():
            yield from walk(v, f"{path}/{k}")
    elif isinstance(o, list):
        for i, v in enumerate(o):
            yield from walk(v, f"{path}[{i}]")


def blueprint_id(entry):
    if isinstance(entry, dict):
        for key in ("m_Blueprint", "Blueprint", "m_BlueprintReference", "guid"):
            if key in entry:
                return json.dumps(entry[key], sort_keys=True)[:60]
        return json.dumps(entry, sort_keys=True)[:60]
    return str(entry)


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    save = sys.argv[1]
    dump = "--dump" in sys.argv
    with zipfile.ZipFile(save) as z:
        for member in ("party.json", "player.json"):
            if member not in z.namelist():
                continue
            data = json.loads(z.read(member))
            books = [(p, d) for p, d in walk(data) if isinstance(d, dict) and "m_KnownSpells" in d]
            print(f"== {member}: {len(books)} spellbook objects")
            for idx, (path, book) in enumerate(books):
                levels = book.get("m_KnownSpells") or []
                if not isinstance(levels, list):
                    continue
                total = sum(len(lv) for lv in levels if isinstance(lv, list))
                if total == 0:
                    continue
                dupes = 0
                per_level = []
                for lv_index, lv in enumerate(levels):
                    if not isinstance(lv, list) or not lv:
                        continue
                    ids = [blueprint_id(e) for e in lv]
                    c = Counter(ids)
                    dup = sum(n - 1 for n in c.values() if n > 1)
                    dupes += dup
                    per_level.append(f"L{lv_index}:{len(lv)}(dup {dup})")
                blueprint = book.get("m_Blueprint") or book.get("Blueprint")
                print(
                    f"  book#{idx} total={total} uniqueLevelBlueprints~{total - dupes} dupEntries={dupes} "
                    f"bp={str(blueprint)[:40]}"
                )
                if dump:
                    print("    " + " ".join(per_level))
                    print("    keys: " + ", ".join(sorted(book.keys()))[:400])
                    first = next((lv for lv in levels if isinstance(lv, list) and lv), None)
                    if first:
                        print("    sample entry: " + json.dumps(first[0])[:300])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
