#!/usr/bin/env python3
"""Post-fix validation: every packed-IB index must sit inside its elem's vertex
window [sv, sv+numv) (the engine locks exactly that window —
DynamicRenderableSceneLodsResource semantics). Any violation = decode noise.

Usage: python tools/validate_bim2_indices.py            # all ships in WT_ID
       python tools/validate_bim2_indices.py ship1 ...  # subset
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from convert_bim2_gltf import (  # noqa: E402
    _looks_like_bim2, parse_bim2, parse_grp, decode_indices,
)
from convert_fleet_models import WT_ID, grp_path_for  # noqa: E402

SHIPS_DIR = Path(r"D:\WarThunder\content\base\res\ships")


def validate(grp_path: Path) -> tuple[bool, str]:
    data = grp_path.read_bytes()
    _names, entries = parse_grp(data)
    payload = None
    for e in entries:
        if e["name"].endswith("_dmg") or e["name"].endswith("_xray"):
            continue
        p = data[e["offset"]:e["offset"] + e["size"]]
        if _looks_like_bim2(p):
            payload = p
            break
    if payload is None:
        return False, "no BIM2 entry"
    parsed = parse_bim2(payload)
    lines = []
    ok = True
    idone: dict[int, list[int]] = {}

    def idx(gi: int) -> list[int]:
        if gi not in idone:
            idone[gi] = decode_indices(parsed["gvd"][gi])
        return idone[gi]

    total_bad = 0
    total_idx = 0
    for li, lod in enumerate(parsed["lods"]):
        lod_bad = 0
        lod_tot = 0
        worst = None
        for r in lod["rigids"]:
            for el in r["elems"]:
                gi = el["gvd"]
                if gi >= len(parsed["gvd"]):
                    continue
                full = idx(gi)
                if not full:
                    continue
                si, cnt = el["si"], el["numf"] * 3
                if si + cnt > len(full):
                    ok = False
                    lines.append(f"  lod{li} {r['name']}: elem range {si}+{cnt} > {len(full)}")
                    continue
                tri = full[si:si + cnt]
                lo, hi = el["sv"], el["sv"] + el["numv"]
                bad = sum(1 for t in tri if not (lo <= t < hi))
                lod_bad += bad
                lod_tot += cnt
                if worst is None or bad > worst[0]:
                    worst = (bad, r["name"])
        total_bad += lod_bad
        total_idx += lod_tot
        pct = 100 * lod_bad / max(1, lod_tot)
        flag = ""
        if lod_bad > lod_tot * 0.0005 and lod_bad > 10:
            ok = False
            flag = f"  <-- FAIL (worst {worst})"
        lines.append(f"  lod{li}: window violations {lod_bad}/{lod_tot} ({pct:.4f}%){flag}")

    lines.insert(0, f"{grp_path.stem}: {'PASS' if ok else 'FAIL'} "
                    f"(total {total_bad}/{total_idx} = {100 * total_bad / max(1, total_idx):.4f}%)")
    return ok, "\n".join(lines)


def main() -> int:
    ids = sys.argv[1:] or sorted(WT_ID)
    failures = []
    for nt_id in ids:
        wt_unit = WT_ID.get(nt_id)
        if wt_unit is None:
            print(f"{nt_id}: no WT mapping, skipped")
            continue
        grp = grp_path_for(wt_unit, SHIPS_DIR)
        if grp is None:
            print(f"{nt_id}: no grp ({wt_unit})")
            continue
        ok, msg = validate(grp)
        print(msg)
        if not ok:
            failures.append(nt_id)
    print(f"\n{'ALL PASS' if not failures else 'FAILURES: ' + ', '.join(failures)}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
