#!/usr/bin/env python3
"""Generate a shellSet (AP or HE) from the bullets measured in the fleet's
main-gun blks (refs recorded by extract_ship_weapons.py).

--mode ap (default): data/shells/wt_naval_shells.json — one AP shell per
distinct caliber, carrying the datamine de Marre K.
--mode he: data/shells/wt_naval_he_shells.json — the biggest-filler HE/Common
bullet per caliber (category HE: x1 ignition multiplier in the damage bridge).

Fuse rules (fuseDelayS/explodeThresholdMm) are engine-side approximations
flagged in the source notes.
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


def bullets_of(path: Path, nm):
    blk = decode_blk(path.read_bytes(), nm)
    bullets = []
    if blk.blocks.get("bullet"):
        bullets.append(blk.blocks["bullet"][0])
    for lst in blk.blocks.values():
        for b in lst:
            bullets.extend(b.blocks.get("bullet", []))
            for sub in b.blocks.values():
                for s2 in sub:
                    bullets.extend(s2.blocks.get("bullet", []))
    recs = []
    for bu in bullets:
        dmg = bu.blocks.get("damage", [None])[0]
        kinetic = dmg.blocks.get("kinetic", [None])[0] if dmg is not None else None
        dm_k = kinetic.get("demarrePenetrationK") if kinetic is not None else None
        recs.append((dm_k, {
            "massKg": bu.get("mass"),
            "caliberMm": (bu.get("caliber") or 0) * 1000.0,
            "muzzleVelMs": bu.get("speed"),
            "explosiveMassKg": bu.get("explosiveMass") or 0.0,
            "demarrePenetrationK": dm_k,
        }))
    return recs


def pick_bullet(path: Path, nm, mode: str):
    best = None
    seen = set()
    for dm_k, rec in bullets_of(path, nm):
        if rec["massKg"] is None or rec["muzzleVelMs"] is None:
            continue
        is_ap = dm_k is not None and dm_k >= 0.5
        if mode == "ap" and not is_ap:
            continue
        if mode == "he" and is_ap:
            continue
        sig = (round(rec["massKg"], 1), dm_k)
        if sig in seen:
            continue
        seen.add(sig)
        if mode == "ap":
            if best is None or rec["massKg"] > best[0]["massKg"]:
                best = (rec, dm_k)
        else:
            if best is None or rec["explosiveMassKg"] > best[0]["explosiveMassKg"]:
                best = (rec, dm_k or 0.0)
    return best  # (rec, dm_k) or None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", choices=["ap", "he"], default="ap")
    ap.add_argument("--weapons-dir", default="_wt_audit/weapons/gamedata/weapons")
    ap.add_argument("--namemap", default="_wt_audit/nm_full.bin")
    ap.add_argument("--weapons-ref", default="data/reference/wt_ship_weapons.json")
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    out = args.out or (
        "data/shells/wt_naval_shells.json" if args.mode == "ap"
        else "data/shells/wt_naval_he_shells.json")

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
        picked = pick_bullet(p, nm, args.mode)
        if picked is None:
            print(f"  skip (no {args.mode.upper()} bullet): {p.name}")
            continue
        rec, dm_k = picked
        if rec["caliberMm"] < 100:
            print(f"  skip (sub-100mm): {p.name}")
            continue
        # nominal caliber comes from the gun file name ("380mm_52_skc_34..."),
        # the measured bullet caliber (379.6) stays in caliberMm
        m = re.search(r"(\d+)mm", p.stem)
        cal = int(m.group(1)) if m else round(rec["caliberMm"])
        shells = [sh for sh in shells if sh["id"] != f"wt_{cal}mm_{args.mode}"]
        fuse_delay = 0.03 if args.mode == "ap" else 0.0
        threshold = round(rec["caliberMm"] * 0.1, 1) if args.mode == "ap" else 5.0
        shells.append({
            "id": f"wt_{cal}mm_{args.mode}",
            "displayName": f"{cal}mm {args.mode.upper()} (wt extract)",
            "category": "AP" if args.mode == "ap" else "HE",
            "caliberMm": rec["caliberMm"],
            "massKg": rec["massKg"],
            "muzzleVelocityMs": rec["muzzleVelMs"],
            "explosiveMassKg": round(rec["explosiveMassKg"], 2),
            "fuseDelayS": fuse_delay,
            "explodeThresholdMm": threshold,
            "demarrePenetrationK": dm_k,
            "source": {
                "origin": "wt_client_extract",
                "notes": f"{args.mode.upper()} bullet measured from the client gun blk "
                         f"({Path(ref).name}); fuseDelay/threshold are engine "
                         "approximations; dragCoefficientScale left at 1.0 "
                         "(no official range table fitted yet).",
                "versionStamp": "extracted 2026-09-21",
            },
        })
        print(f"  wt_{cal}mm_{args.mode}: {rec['massKg']:.0f}kg {rec['muzzleVelMs']:.0f}m/s "
              f"fill={rec['explosiveMassKg']:.1f}kg K={dm_k}")

    doc = {"schemaVersion": 1, "kind": "shellSet", "shells": shells}
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    Path(out).write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
    print(f"{len(shells)} shells -> {out}")


if __name__ == "__main__":
    main()
