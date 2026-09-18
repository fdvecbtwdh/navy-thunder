#!/usr/bin/env python3
"""War Thunder datamine -> Navy Thunder shell JSON extraction pipeline (MDR-0001/0007).

Extracts per-shell mechanical parameters from the public gszabi99/War-Thunder-Datamine
naval weapon files (blkx text) and emits versioned, provenance-stamped NavyThunder
shellSet JSON. Values from here are Tier-1 reference data (facts about WT).

Usage:
  python tools/extract_datamine.py --dir <path/to/aero_guns+navalmodels_weapons parent> [--out data/shells/wt_extracted.json]
  python tools/extract_datamine.py --url <raw base URL>            # downloads on demand
  python tools/extract_datamine.py --selftest                      # runs on the committed sample

Offline use: point --dir at a local checkout of
https://github.com/gszabi99/War-Thunder-Datamine (aces.vromfs.bin_u/gamedata/weapons/).
"""
import argparse
import json
import re
import sys
from pathlib import Path

VERSION_STAMP = "WT datamine 2026-09"

# blk text tolerates both "key": v and "key"=v depending on dump tooling.
NUMBER = r'"?{key}"?\s*[:=]\s*(-?[0-9.eE+]+)'


def num(key: str, text: str, default=None):
    m = re.search(NUMBER.format(key=key), text)
    return float(m.group(1)) if m else default


def str_key(key: str, text: str, default=None):
    m = re.search(rf'"?{key}"?\s*[:=]\s*"([^"]+)"', text)
    return m.group(1) if m else default


def extract_shell(name: str, text: str, source_url: str) -> dict:
    explosive = num("explosiveMass", text)
    shell = {
        "id": name.replace(".", "_").lower(),
        "displayName": str_key("_name", text) or name,
        "category": map_category(str_key("bulletType", text) or ""),
        "caliberMm": round(num("caliber", text, 0.0) * 1000.0, 2),  # blk stores meters
        "massKg": num("mass", text, 0.0),
        "muzzleVelocityMs": num("speed", text, 0.0),
        "explosiveType": str_key("explosiveType", text),
        "explosiveMassKg": explosive if explosive is not None else 0.0,
        "fuseDelayS": num("time", text, 0.001),
        "explodeThresholdMm": num("explodeTreshold", text, 0.1),
        "demarrePenetrationK": num("demarrePenetrationK", text, 1.0),
        "dragCoefficient": num("Cx", text),
        "ballisticsModel": str_key("ballisticsModel", text),
        "source": {
            "origin": "wt_datamine",
            "url": source_url,
            "versionStamp": VERSION_STAMP,
        },
    }
    prox_radius = num("radius", text.split("proximityFuse", 1)[-1]) if "proximityFuse" in text else None
    if prox_radius:
        shell["proximityFuse"] = {
            "radiusM": prox_radius,
            "armDistanceM": num("armDistance", text, 0.0),
            "airTargetsOnly": True,
        }
        shell["category"] = "AAVT"
    return shell


def map_category(bullet_type: str) -> str:
    t = bullet_type.upper()
    if "APCBC" in t:
        return "APCBC"
    if "APC" in t:
        return "APC"
    if "APBC" in t:
        return "APBC"
    if t == "AP" or "AP " in t:
        return "AP"
    if "SAP" in t:
        return "SAP"
    if "COMMON" in t:
        return "Common"
    if "HE" in t or "HC" in t:
        return "HE"
    return "Unknown"


def run(sources, out_path: Path):
    shells = []
    for name, text, url in sources:
        try:
            shells.append(extract_shell(name, text, url))
        except Exception as exc:  # a malformed file must not sink the batch
            print(f"  ! skipped {name}: {exc}", file=sys.stderr)

    doc = {
        "schemaVersion": 1,
        "kind": "shellSet",
        "shells": shells,
    }
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    print(f"extracted {len(shells)} shells -> {out_path}")


def iter_blkx_files(root: Path):
    for pattern in ("navalmodels_weapons/*.blkx", "aircraft_weapons/*.blkx"):
        yield from sorted(root.glob(pattern))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", help="path to .../gamedata/weapons of a datamine checkout")
    parser.add_argument("--out", default="data/shells/wt_extracted.json")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()

    if args.selftest:
        sample = Path("data/reference/samples/sample_shell.blkx")
        if not sample.exists():
            sys.exit("selftest sample missing")
        text = sample.read_text(encoding="utf-8")
        run([("sample_shell", text, "https://github.com/gszabi99/War-Thunder-Datamine (sample)")],
            Path(args.out))
        extracted = json.loads(Path(args.out).read_text(encoding="utf-8"))["shells"][0]
        assert extracted["massKg"] == 1225.0, extracted
        assert extracted["demarrePenetrationK"] == 1.0
        assert extracted["caliberMm"] == 406.0
        print("selftest ok")
        return

    if not args.dir:
        parser.error("--dir or --selftest required")
    root = Path(args.dir)
    sources = []
    for f in iter_blkx_files(root):
        sources.append((f.stem, f.read_text(encoding="utf-8", errors="replace"),
                        f"https://github.com/gszabi99/War-Thunder-Datamine ({f.as_posix()})"))
    run(sources, Path(args.out))


if __name__ == "__main__":
    main()
