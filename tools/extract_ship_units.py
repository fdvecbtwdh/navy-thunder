#!/usr/bin/env python3
"""Batch-extract War Thunder ship unit parameters from unpacked aces.vromfs.bin blk files.

Input : directory produced by tools/unpack_vromfs.py (gamedata/units/ships/**.blk)
        plus the archive namemap payload ('nm' file, zstd-stripped by the unpacker).
Output: data/reference/wt_ship_units.json

Usage:
  python tools/extract_ship_units.py --units-dir _wt_audit/units \
      --namemap _wt_audit/nm_full.bin --out data/reference/wt_ship_units.json

Fields per ship (whatever the client file provides):
  id/name        file stem under gamedata/units/ships/
  nation         from the file-name prefix (us/uk/jp/de/it/fr/ussr/...)
  displacementT  ShipPhys{mass{Empty:r}} in kg -> metric tons
  maxSpeedKnots  ShipPhys{engines{speedToTime{row}}} terminal speed (knots).
                 NOTE: this is the in-game handling curve endpoint (arcade-tuned),
                 not the historical speed.
  weaponsSummary grouping of commonWeapons{Weapon[]} by parsed gun caliber,
                 e.g. "4x127mm; 2x40mm; 6x20mm"
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from blk_decode import BlkError, decode_blk, load_namemap  # noqa: E402

NATION_PREFIX = {
    "us": "us", "uss": "us",                       # USS ...
    "uk": "uk", "hms": "uk",                       # HMS ...
    "jp": "jp", "ijn": "jp",                       # IJN ...
    "germ": "de",
    "ussr": "ussr",
    "it": "it", "ital": "it",
    "fr": "fr",
    "sw": "swe", "swe": "swe",
    "zh": "cn",
}

_RE_CAL = re.compile(r"^(\d+(?:_\d+)*)mm")
_RE_ROCKET = re.compile(r"^rocket_")
_MS_TO_KNOTS = 1.94384


def _caliber_of(weapon_blk: str):
    """('mm', 127) | ('rocket', None) | None from a weapon .blk path/name."""
    base = weapon_blk.replace("\\", "/").rsplit("/", 1)[-1][:-4] if weapon_blk.endswith(".blk") \
        else weapon_blk.rsplit("/", 1)[-1]
    if _RE_ROCKET.match(base):
        return ("rocket", None)
    m = _RE_CAL.match(base)
    if m:
        return ("mm", float(m.group(1).replace("_", ".")))
    if "torpedo" in base:
        return ("torpedo", None)
    return (None, None)


def extract_ship(node, source_path: str) -> dict:
    stem = os.path.basename(source_path)[:-4]
    m = re.match(r"^([a-z]{2,4})_", stem)
    nation = NATION_PREFIX.get(m.group(1)) if m else None

    out = {"id": stem, "name": stem, "nation": nation}

    sp = node.get_block("ShipPhys")
    if sp:
        mb = sp.get_block("mass")
        kg = mb.get("Empty") if mb else None
        if isinstance(kg, (int, float)) and 1000 < kg < 200_000_000:
            out["displacementT"] = round(kg / 1000.0, 1)
        eng = sp.get_block("engines")
        stt = eng.get_block("speedToTime") if eng else None
        rows = stt.get_all("row") if stt else []
        # rows are [speed_m_per_s, time_s]; last row's speed is the ship's top speed
        if rows and isinstance(rows[-1], list) and len(rows[-1]) >= 2:
            spd = rows[-1][0]
            if isinstance(spd, (int, float)) and 0.5 < spd < 50.0:
                out["maxSpeedKnots"] = round(float(spd) * _MS_TO_KNOTS, 1)

    cw = node.get_block("commonWeapons")
    if cw:
        groups: list[tuple[str, float | None, int]] = []
        for w in cw.get_blocks("Weapon"):
            blk = w.get("blk")
            if not isinstance(blk, str):
                continue
            kind, cal = _caliber_of(blk)
            if kind == "mm":
                groups.append(("mm", cal, 1))
            elif kind == "rocket":
                groups.append(("rocket", None, 1))
            elif kind == "torpedo":
                groups.append(("torpedo", None, 1))
        if groups:
            agg: Counter = Counter()
            for kind, cal, _ in groups:
                agg[(kind, cal)] += 1
            def _key(item):
                (kind, cal), _n = item
                return (0 if kind == "mm" else 1, -(cal or 0))
            parts = []
            for (kind, cal), n in sorted(agg.items(), key=_key):
                parts.append(f"{n}x{cal:g}mm" if kind == "mm" else f"{n}x{kind}")
            out["weaponsSummary"] = "; ".join(parts)
    out["sourcePath"] = source_path
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description="extract WT ship units to JSON")
    ap.add_argument("--units-dir", required=True, help="vromfs unpack root (contains gamedata/units/ships)")
    ap.add_argument("--namemap", required=True, help="namemap payload file (zstd-stripped 'nm')")
    ap.add_argument("--out", required=True, help="output JSON path")
    a = ap.parse_args()

    ships_dir = os.path.join(a.units_dir, "gamedata", "units", "ships")
    if not os.path.isdir(ships_dir):
        print(f"ERROR: {ships_dir} not found", file=sys.stderr)
        return 2

    names = load_namemap(open(a.namemap, "rb").read())
    print(f"namemap: {len(names)} names", file=sys.stderr)

    files: list[str] = []
    for root, _dirs, fs in os.walk(ships_dir):
        for fn in fs:
            if fn.endswith(".blk"):
                rel = os.path.relpath(os.path.join(root, fn), a.units_dir).replace(os.sep, "/")
                if "/debris/" in "/" + rel.split("ships/", 1)[-1]:
                    continue  # debris presets are not ship units
                files.append(rel)
    files.sort()

    ships: list[dict] = []
    failures: list[tuple[str, str]] = []
    t0 = time.time()
    for rel in files:
        path = os.path.join(a.units_dir, rel)
        try:
            node = decode_blk(open(path, "rb").read(), names)
            ships.append(extract_ship(node, rel))
        except (BlkError, Exception) as e:  # noqa: BLE001 - keep batch going
            failures.append((rel, str(e)))
    dt = time.time() - t0

    n_name = sum(1 for s in ships if s.get("name"))
    n_disp = sum(1 for s in ships if s.get("displacementT") is not None)
    n_spd = sum(1 for s in ships if s.get("maxSpeedKnots") is not None)
    n_weap = sum(1 for s in ships if s.get("weaponsSummary"))
    n_nation = sum(1 for s in ships if s.get("nation"))

    doc = {
        "schemaVersion": 1,
        "kind": "wtShipUnits",
        "extractedOn": time.strftime("%Y-%m-%d", time.gmtime()),
        "source": "War Thunder client (licensed)",
        "ships": ships,
    }
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)

    print(f"decoded {len(ships)}/{len(files)} blk in {dt:.1f}s -> {a.out}", file=sys.stderr)
    print(f"  with name/id: {n_name}  nation: {n_nation}", file=sys.stderr)
    print(f"  displacement: {n_disp}  maxSpeed: {n_spd}  weapons: {n_weap}", file=sys.stderr)
    if failures:
        print(f"  failures: {len(failures)}", file=sys.stderr)
        for rel, err in failures[:10]:
            print(f"    FAIL {rel}: {err}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
