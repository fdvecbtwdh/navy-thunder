#!/usr/bin/env python3
"""Batch-convert the Navy Thunder fleet's WT ship models to glTF (Phase 03).

Maps NT ship ids to WT client .grp packages via the same WT_ID table used by
generate_ships.py, converts every LOD, and writes metadata.json per ship for
the Godot AssetRegistry. Ships without a WT mapping are reported and skipped
(they keep the procedural fallback).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from convert_bim2_gltf import convert_grp

WT_ID = {
    "uss_fletcher": "us_destroyer_fletcher",
    "uss_sims": "uss_dd_sims_guns",
    "uss_gearing": "us_destroyer_gearing",
    "uss_sumner": "us_destroyer_sumner",
    "ijn_kagero": "jp_destroyer_kagero",
    "ijn_fubuki": "jp_destroyer_fubuki",
    "dkm_z23": None,
    "rn_tribal": "uk_destroyer_tribal",
    "uss_atlanta": "us_cruiser_atlanta_class_atlanta",
    "uss_brooklyn": "us_cruiser_brooklyn_class_brooklyn",
    "uss_baltimore": "us_cruiser_baltimore_class",
    "uss_new_orleans": "us_cruiser_new_orleans_class",
    "ijn_myoko": "jp_cruiser_myoko",
    "ijn_mogami": "jp_cruiser_mogami",
    "dkm_prinz_eugen": "germ_cruiser_prinz_eugen",
    "rn_edinburgh": None,
    "uss_northampton": "us_cruiser_northampton_class",
    "uss_penelope": None,
    "uss_nevada": "us_battleship_nevada",
    "uss_pennsylvania": None,
    "uss_new_mexico": None,
    "uss_colorado": "us_battleship_colorado_class_colorado",
    "uss_north_carolina": "us_battleship_north_carolina_class",
    "uss_south_dakota": "us_battleship_south_dakota",
    "uss_iowa": "us_battleship_iowa_class_iowa",
    "uss_california": None,
    "ijn_kongo": "jp_battlecruiser_kongo",
    "ijn_nagato": "jp_battleship_nagato",
    "rn_renown": "uk_battlecruiser_renown",
    "rms_bismarck": "germ_battleship_bismarck",
}

# WT unit-id nation prefix -> client grp filename prefix (measured 2026-10-03)
GRP_PREFIX = {"us_": "usa_", "jp_": "jap_", "germ_": "ger_", "uk_": "uk_"}


def grp_path_for(wt_unit: str, ships_dir: Path) -> Path | None:
    for pre, rep in GRP_PREFIX.items():
        if wt_unit.startswith(pre):
            p = ships_dir / f"{rep}{wt_unit[len(pre):]}.grp"
            return p if p.exists() else None
    p = ships_dir / f"{wt_unit}.grp"
    return p if p.exists() else None


def main() -> int:
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--wt", type=Path, default=Path(r"D:\WarThunder\content\base\res\ships"))
    ap.add_argument("--out", type=Path, default=Path("assets/models"))
    ap.add_argument("--ids", nargs="*", help="only these NT ship ids")
    args = ap.parse_args()

    summary = {"converted": [], "no_mapping": [], "no_grp": [], "failed": []}
    ids = args.ids or sorted(WT_ID)
    for nt_id in ids:
        wt_unit = WT_ID.get(nt_id)
        if not wt_unit:
            summary["no_mapping"].append(nt_id)
            continue
        grp = grp_path_for(wt_unit, args.wt)
        if grp is None:
            summary["no_grp"].append(f"{nt_id} ({wt_unit})")
            continue
        out_dir = args.out / nt_id
        try:
            report = convert_grp(grp, out_dir)
            lods = [l for l in report["lods"] if l.get("file")]
            meta = {
                "ship_id": nt_id,
                "wt_unit_id": wt_unit,
                "source_grp": grp.name,
                "coordinate": "NT_STANDARD",
                "model": lods[0]["file"] if lods else None,
                "lods": {str(l["lod"]): {"file": l["file"], "range_m": l["range_m"],
                                          "tris": l["tris"]} for l in lods},
                "node_map": "nodeMap.json" if report.get("node_map") else None,
                "bbox_nt_min": report["bbox_nt"][0],
                "bbox_nt_max": report["bbox_nt"][1],
                "converted": bool(lods),
            }
            (out_dir / "metadata.json").write_text(json.dumps(meta, indent=1), encoding="utf-8")
            tris = sum(l["tris"] for l in lods)
            summary["converted"].append({"id": nt_id, "lods": len(lods), "tris": tris})
            print(f"  {nt_id:22s} OK  lods={len(lods)} tris={tris:,}")
        except Exception as exc:  # noqa: BLE001 - batch continues
            summary["failed"].append({"id": nt_id, "error": str(exc)})
            print(f"  {nt_id:22s} FAIL {exc}")

    print(json.dumps({k: (v if k != "converted" else len(v)) for k, v in summary.items()},
                     indent=1, ensure_ascii=False))
    (args.out / "fleet_convert_summary.json").write_text(
        json.dumps(summary, indent=1), encoding="utf-8")
    return 0 if not summary["failed"] else 1


if __name__ == "__main__":
    sys.exit(main())
