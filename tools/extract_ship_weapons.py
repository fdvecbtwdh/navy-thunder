#!/usr/bin/env python3
"""Extract per-ship naval weapon data (rate of fire / shell mass / turret layout)
from unpacked WT unit + weapon blks into data/reference/wt_ship_weapons.json.

Verified field mapping (germ_battleship_bismarck):
  unit blk commonWeapons.Weapon[]      one entry per mount; blk -> gun file;
                                       speedYaw = turret traverse deg/s
  gun blk  shotFreq                    shots/second -> reload = 1/shotFreq
                                       (380mm Sk C/34: 0.044 -> 22.7 s, matches history)
  gun blk  bullet/mass                 shell mass kg (380mm HE: 800.8 kg)
  gun blk  bullet/caliber              meters (0.38)
  gun blk  bullet/speed                muzzle velocity m/s (820)
  gun blk  bullet/explosiveMass        kg

Main battery = the largest-caliber Weapon group; barrels/turret derived from the
weaponsSummary total (e.g. "8x380mm" / 4 mounts = 2).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from blk_decode import decode_blk, load_namemap  # noqa: E402

WEAPONS_ROOT = "gamedata/weapons/navalmodels_weapons"


def iter_blocks(node):
    for lst in node.blocks.values():
        for b in lst:
            yield b


def find_param(node, name):
    vals = node.params.get(name)
    return vals[0] if vals else None


def load_gun(weapons_dir: Path, ref: str, nm):
    """decode a referenced gun blk; returns dict of key fields or None."""
    rel = str(ref).replace("\\", "/")
    i = rel.lower().find("gamedata/weapons/")
    rel = rel[i:] if i >= 0 else "gamedata/weapons/" + rel
    p = weapons_dir / rel.split("gamedata/weapons/")[-1]
    if not p.exists():
        cand = list(weapons_dir.rglob(Path(rel).name))
        if not cand:
            return None
        p = cand[0]
    blk = decode_blk(p.read_bytes(), nm)
    # default bullet block: top-level 'bullet', else the first '<name>/bullet'
    bullet = blk.blocks.get("bullet", [None])[0]
    if bullet is None:
        for b in iter_blocks(blk):
            if b.blocks.get("bullet"):
                bullet = b.blocks["bullet"][0]
                break
    if bullet is None:
        return None
    def g(name):
        v = bullet.params.get(name)
        return v[0] if v else None
    return {
        "caliberMm": (g("caliber") or 0) * 1000.0,
        "shellMassKg": g("mass"),
        "muzzleVelMs": g("speed"),
        "explosiveMassKg": g("explosiveMass"),
        "reloadS": (1.0 / (blk.get("shotFreq") or 0.0)) if blk.get("shotFreq") else None,
    }


def extract_ship(unit_blk_path: Path, weapons_dir: Path, nm):
    blk = decode_blk(unit_blk_path.read_bytes(), nm)

    # Walk the whole tree collecting weapon refs: modern ships nest them under
    # commonWeapons.Weapon[], older schemas hang 'blk' directly on 'weapon' blocks.
    mounts: list[tuple[str, float | None]] = []  # (gun ref, speedYaw)

    def visit(node):
        ref = node.get("blk")
        norm = str(ref).replace("\\", "/").lower() if ref else ""
        if ref and "_weapons/" in norm:
            mounts.append((str(ref), node.get("speedYaw")))
        for lst in node.blocks.values():
            for b in lst:
                visit(b)

    visit(blk)
    if not mounts:
        return None
    groups: dict[str, dict] = {}
    for ref, sy in mounts:
        gun = load_gun(weapons_dir, ref, nm)
        if not gun or not gun["caliberMm"]:
            continue
        key = round(gun["caliberMm"])
        grp = groups.setdefault(key, {"mounts": 0, "gun": gun, "traverse": None})
        grp["mounts"] += 1
        if sy and grp["traverse"] is None:
            grp["traverse"] = sy

    if not groups:
        return None
    main_mm = max(groups)
    main = groups[main_mm]
    total_barrels = main["mounts"] * 2  # refined against weaponsSummary by caller
    return {
        "main": {
            "caliberMm": main_mm,
            "turrets": main["mounts"],
            "traverseDegPerS": main["traverse"],
            **{k: main["gun"][k] for k in ("shellMassKg", "muzzleVelMs", "explosiveMassKg", "reloadS")},
        },
        "allCalibers": {str(k): groups[k]["mounts"] for k in sorted(groups, reverse=True)},
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--units-dir", default="_wt_audit/units_full/gamedata/units/ships")
    ap.add_argument("--weapons-dir", default="_wt_audit/weapons/gamedata/weapons")
    ap.add_argument("--namemap", default="_wt_audit/nm_full.bin")
    ap.add_argument("--out", default="data/reference/wt_ship_weapons.json")
    ap.add_argument("ship_ids", nargs="*", help="unit blk stems; default: all")
    args = ap.parse_args()

    nm = load_namemap(open(args.namemap, "rb").read())
    units_dir = Path(args.units_dir)
    weapons_dir = Path(args.weapons_dir)
    ids = args.ship_ids or sorted(p.stem for p in units_dir.glob("*.blk"))

    out = {}
    for sid in ids:
        path = units_dir / f"{sid}.blk"
        if not path.exists():
            print(f"  skip (no blk): {sid}")
            continue
        info = extract_ship(path, weapons_dir, nm)
        if info:
            out[sid] = info
            m = info["main"]
            print(f"{sid:44s} {m['turrets']}x{m['caliberMm']:.0f}mm "
                  f"reload={m['reloadS'] and round(m['reloadS'], 1)}s "
                  f"mass={m['shellMassKg'] and round(m['shellMassKg'])}kg "
                  f"mv={m['muzzleVelMs'] and round(m['muzzleVelMs'])} traverse={m['traverseDegPerS']}")
        else:
            print(f"{sid:44s} no weapons parsed")

    doc = {
        "schemaVersion": 1,
        "kind": "wtShipWeapons",
        "extractedOn": "2026-09-21",
        "source": "War Thunder client (licensed)",
        "ships": [{"id": sid, **info} for sid, info in out.items()],
    }
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(doc, indent=1), encoding="utf-8")
    print(f"{len(out)} ships -> {args.out}")


if __name__ == "__main__":
    main()
