#!/usr/bin/env python3
"""Generate data/shells/wt_naval_shells.json (kind: shellSet) from the AP bullets
measured in the fleet's main-gun blks (refs recorded by extract_ship_weapons.py).

Each distinct caliber gets one AP shell carrying the datamine de Marre K, mass,
muzzle velocity and explosive mass. The engine's fuse rules (fuseDelayS,
explodeThresholdMm) are engine-side approximations flagged in the source notes.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from blk_decode import decode_blk, load_namemap  # noqa: E402


def ap_bullet_from_gun(path: Path, nm) -> dict | None:
    blk = decode_blk(path.read_bytes(), nm)
    best = None
    bullets = []
    if blk.blocks.get("bullet"):
        bullets.append(blk.blocks["bullet"][0])
    for lst in blk.blocks.values():
        for b in lst:
            bullets.extend(b.blocks.get("bullet", []))
            for sub in b.blocks.values():
                for s2 in sub:
                    bullets.extend(s2.blocks.get("bullet", []))
    for bu in bullets:
        dmg = bu.blocks.get("damage", [None])[0]
        kinetic = dmg.blocks.get("kinetic", [None])[0] if dmg is not None else None
        dm_k = kinetic.get("demarrePenetrationK") if kinetic is not None else None
        if dm_k is None or dm_k < 0.5:
            continue
        rec = {
            "massKg": bu.get("mass"),
            "caliberMm": (bu.get("caliber") or 0) * 1000.0,
            "muzzleVelMs": bu.get("speed"),
            "explosiveMassKg": bu.get("explosiveMass") or 0.0,
            "demarrePenetrationK": dm_k,
        }
        # prefer the heaviest AP (naval AP fill is small; heavier = later APC)
        if best is None or rec["massKg"] > best["massKg"]:
            best = rec
    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--weapons-dir", default="_wt_audit/weapons/gamedata/weapons")
    ap.add_argument("--namemap", default="_wt_audit/nm_full.bin")
    ap.add_argument("--weapons-ref", default="data/reference/wt_ship_weapons.json")
    ap.add_argument("--out", default="data/shells/wt_naval_shells.json")
    args = ap.parse_args()

    nm = load_namemap(open(args.namemap, "rb").read())
    doc = json.loads(Path(args.weapons_ref).read_text(encoding="utf-8"))
    gun_refs = doc.get("gunRefs", {})

    shells = []
    for cal_key in sorted(gun_refs, key=int, reverse=True):
        ref = gun_refs[cal_key]
        rel = str(ref).replace("\\", "/")
        i = rel.lower().find("gamedata/weapons/")
        rel = rel[i:] if i >= 0 else "gamedata/weapons/" + rel
        p = Path(args.weapons_dir) / rel.split("gamedata/weapons/")[-1]
        if not p.exists():
            cand = list(Path(args.weapons_dir).rglob(Path(rel).name))
            if not cand:
                print(f"  skip (no gun blk): {rel}")
                continue
            p = cand[0]
        apb = ap_bullet_from_gun(p, nm)
        if not apb:
            print(f"  skip (no AP bullet): {p.name}")
            continue
        if apb["caliberMm"] < 100:
            print(f"  skip (sub-100mm AA round): {p.name}")
            continue
        # nominal caliber comes from the gun file name ("380mm_52_skc_34..."),
        # the measured bullet caliber (379.6) stays in caliberMm
        import re
        m = re.search(r"(\d+)mm", p.stem)
        cal = int(m.group(1)) if m else round(apb["caliberMm"])
        shells = [sh for sh in shells if sh["id"] != f"wt_{cal}mm_ap"]
        shells.append({
            "id": f"wt_{cal}mm_ap",
            "displayName": f"{cal}mm AP (wt extract)",
            "category": "AP",
            "caliberMm": apb["caliberMm"],
            "massKg": apb["massKg"],
            "muzzleVelocityMs": apb["muzzleVelMs"],
            "explosiveMassKg": round(apb["explosiveMassKg"], 2),
            "fuseDelayS": 0.03,
            "explodeThresholdMm": round(apb["caliberMm"] * 0.1, 1),
            "demarrePenetrationK": apb["demarrePenetrationK"],
            "source": {
                "origin": "wt_client_extract",
                "notes": "AP bullet measured from the client gun blk "
                         f"({Path(ref).name}); fuseDelay/threshold are engine "
                         "approximations; dragCoefficientScale left at 1.0 "
                         "(no official range table fitted yet).",
                "versionStamp": "extracted 2026-09-21",
            },
        })
        print(f"  wt_{cal}mm_ap: {apb['massKg']:.0f}kg {apb['muzzleVelMs']:.0f}m/s K={apb['demarrePenetrationK']}")

    doc = {"schemaVersion": 1, "kind": "shellSet", "shells": shells}
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
    print(f"{len(shells)} shells -> {args.out}")


if __name__ == "__main__":
    main()
