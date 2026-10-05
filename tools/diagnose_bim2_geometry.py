#!/usr/bin/env python3
"""BIM2 geometry diagnostic: find the root cause of stretched/spiked triangles.

For every rigid the scene dump carries an engine-authoritative bounding sphere
(sph_c, sph_r). Any decoded triangle whose vertices sit far outside that sphere
is decode noise — a stretched triangle. This tool reports, per ship / LOD /
rigid, how many triangles escape their rigid's sphere and how far, then drills
into the worst offenders (element ranges, index stats, gvd classification) to
expose the mechanism (bad baseVertex, packed-IB decode drift, LOCAL buffers
rendered without their wtm, ...).

Usage: python tools/diagnose_bim2_geometry.py <ship_id> [more_ids...]
"""
from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from convert_bim2_gltf import (  # noqa: E402
    _looks_like_bim2, parse_bim2, parse_grp, decode_vertices, decode_indices,
)


def diag_ship(grp_path: Path, max_report: int = 6) -> None:
    data = grp_path.read_bytes()
    _names, entries = parse_grp(data)
    main = None
    for e in entries:
        if e["name"].endswith("_dmg") or e["name"].endswith("_xray"):
            continue
        payload = data[e["offset"]:e["offset"] + e["size"]]
        if _looks_like_bim2(payload):
            main = (e, payload)
            break
    if main is None:
        print(f"{grp_path.name}: no BIM2 entry")
        return
    parsed = parse_bim2(main[1])
    bbox = parsed["bbox"]
    hull_len = abs(bbox[1][0] - bbox[0][0])
    print(f"\n=== {grp_path.stem}  hull={hull_len:.0f}m  gvd={len(parsed['gvd'])} "
          f"nodes={len(parsed['names'])} ===")

    idx_cache: dict[int, list[int]] = {}
    vcache: dict[int, dict] = {}

    def get_vd(gi: int) -> dict:
        if gi not in vcache:
            g = parsed["gvd"][gi]
            vd = decode_vertices(g, bbox)
            xs = [q[0] for q in vd["pos"]]
            ys = [q[1] for q in vd["pos"]]
            zs = [q[2] for q in vd["pos"]]
            span = max(max(xs) - min(xs), max(zs) - min(zs)) if vd["pos"] else 0
            vd["span"] = span
            vd["model_space"] = span > hull_len * 0.6
            vcache[gi] = vd
        return vcache[gi]

    def get_idx(gi: int) -> list[int]:
        if gi not in idx_cache:
            idx_cache[gi] = decode_indices(parsed["gvd"][gi])
        return idx_cache[gi]

    for li, lod in enumerate(parsed["lods"]):
        worst = []
        total_tris = 0
        total_bad = 0
        local_no_wtm = 0
        for r in lod["rigids"]:
            cx, cy, cz = r["sph_c"]
            rad = r["sph_r"]
            lim = max(rad * 1.35, rad + 6.0)  # sphere + tolerance
            bad_tris = 0
            bad_far = 0.0
            tris = 0
            for el in r["elems"]:
                gi = el["gvd"]
                if gi >= len(parsed["gvd"]):
                    continue
                full = get_idx(gi)
                if not full:
                    continue
                vd = get_vd(gi)
                if not vd["pos"]:
                    continue
                start, cnt = el["si"], el["numf"] * 3
                if start + cnt > len(full):
                    continue
                base = el["base_vertex"]
                vcnt = parsed["gvd"][gi]["vcnt"]
                tri = full[start:start + cnt]
                for j in range(0, len(tri) - 2, 3):
                    idxs = [base + tri[j], base + tri[j + 1], base + tri[j + 2]]
                    if any(not (0 <= i < vcnt) for i in idxs):
                        continue  # already handled by the converter's culling
                    tris += 1
                    # worst corner distance from the rigid's sphere centre
                    d = max(math.dist(vd["pos"][i], (cx, cy, cz)) for i in idxs)
                    if d > lim:
                        bad_tris += 1
                        bad_far = max(bad_far, d)
                if not vd["model_space"]:
                    local_no_wtm += tris
            total_tris += tris
            total_bad += bad_tris
            if bad_tris:
                worst.append((bad_tris / max(1, tris), bad_tris, bad_far, r))
        worst.sort(key=lambda w: (-w[0], -w[2]))
        flag = " <-- BAD" if total_bad > total_tris * 0.005 and total_bad > 30 else ""
        print(f"  lod{li}: rigids={len(lod['rigids'])} tris={total_tris} "
              f"out-of-sphere={total_bad} ({100 * total_bad / max(1, total_tris):.2f}%)"
              f" local-vb-tris(no wtm applied)={local_no_wtm}{flag}")
        for frac, bad, far, r in worst[:max_report]:
            print(f"    {r['name']}: {bad} bad tris, worst {far:.0f}m "
                  f"(sphere r={r['sph_r']:.0f}m), elems={len(r['elems'])}")
            for el in r["elems"][:4]:
                gi = el["gvd"]
                g = parsed["gvd"][gi] if gi < len(parsed["gvd"]) else None
                if g is None:
                    continue
                vd = get_vd(gi)
                print(f"      elem gvd={gi} vcnt={g['vcnt']} basev={el['base_vertex']} "
                      f"si={el['si']} numf={el['numf']} sv={el['sv']} numv={el['numv']} "
                      f"vb_span={vd['span']:.0f}m model_space={vd['model_space']}")


if __name__ == "__main__":
    models = Path("assets/raw")
    ids = sys.argv[1:]
    if not ids:
        print("usage: diagnose_bim2_geometry.py <ship_id> [...]")
        sys.exit(1)
    for sid in ids:
        matches = list(models.glob(f"*{sid}*.grp"))
        if not matches:
            print(f"no grp for {sid} in {models}")
            continue
        diag_ship(matches[0])
