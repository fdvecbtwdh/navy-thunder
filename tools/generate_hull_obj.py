#!/usr/bin/env python3
"""Procedural battleship hull mesh generator -> OBJ (R3.1 content pipeline).

Builds a 3D hull surface from REAL ship parameters (length/beam/draft/displacement
from the fleet data) using standard destroyer/battleship hull-form proportions:
parallel midbody, tapered bow with fine entrance, cruiser stern, hard chine at the
keel line. Used for Godot visualization (assets/models/<ship>/hull.obj) while the
WT BIM2 render-mesh deserializer remains future work (see docs/asset_pipeline.md).

Units: metres. +X = stern->bow? No: +Z forward (matches the engine's heading
convention), +X starboard, +Y up. Origin at waterline centre.
"""
import json
import math
import sys
from pathlib import Path


def half_beam(x_norm: float, cs: float, cb: float) -> float:
    """Half-beam factor at normalized station x in [-1..1] (0=mid, 1=bow)."""
    if x_norm >= 0:  # forward body: fine entrance
        t = x_norm ** (1.6 + 1.2 * (1 - cb))
        return max(0.02, (1 - t) ** 0.72)
    t = (-x_norm) ** (1.9 + 1.4 * (1 - cs))  # aft body: cruiser stern
    return max(0.04, (1 - t) ** 0.55)


def draft_ratio(x_norm: float) -> float:
    """Keel rise towards the ends (deadrise at bow/stern), 0 = full draft."""
    a = abs(x_norm)
    return min(1.0, a ** 3.2)


def build_hull(length_m: float, beam_m: float, draft_m: float, stations: int = 41,
               sides: int = 9):
    """Returns (verts, faces). Hull mirrored port/starboard, deck + bottom capped."""
    verts = []
    faces = []
    half_len = length_m / 2.0

    # stations from stern (-1) to bow (+1)
    station_points = []  # per station: list of (x, y) from deck to keel, starboard side
    for s in range(stations):
        x_norm = -1.0 + 2.0 * s / (stations - 1)
        z = x_norm * half_len
        hb = half_beam(x_norm, 0.62, 0.52) * (beam_m / 2.0)
        keel_rise = draft_ratio(x_norm) * draft_m * 0.85
        y_deck = draft_m * 0.75 + (1 - abs(x_norm) ** 1.4) * length_m * 0.012  # sheer
        cols = []
        for k in range(sides):
            t = k / (sides - 1)  # 0=deck edge .. 1=keel
            y = y_deck + (keel_rise - y_deck) * (t ** 1.25)
            x = hb * math.cos(t * math.pi / 2) ** 0.8  # round bilge
            cols.append((x, y, z))
        station_points.append(cols)

    # starboard surface grid
    grid = []
    for cols in station_points:
        grid.append([(len(verts) + k) for k in range(len(cols))])
        verts.extend(cols)

    for s in range(stations - 1):
        for k in range(sides - 1):
            a = grid[s][k]
            b = grid[s][k + 1]
            c = grid[s + 1][k + 1]
            d = grid[s + 1][k]
            faces.append((a, b, c))
            faces.append((a, c, d))

    # mirror to port (reverse winding)
    base = len(verts)
    verts.extend([(-v[0], v[1], v[2]) for v in verts])
    for s in range(stations - 1):
        for k in range(sides - 1):
            a = grid[s][k] + base
            b = grid[s][k + 1] + base
            c = grid[s + 1][k + 1] + base
            d = grid[s + 1][k] + base
            faces.append((a, d, c))
            faces.append((a, c, b))

    # deck cap (both sides, bow-to-stern strip)
    deck_r = [grid[s][0] for s in range(stations)]
    deck_l = [grid[s][0] + base for s in range(stations)]
    for s in range(stations - 1):
        faces.append((deck_r[s], deck_r[s + 1], deck_l[s]))
        faces.append((deck_r[s + 1], deck_l[s + 1], deck_l[s]))

    # top-down silhouette: starboard deck edge bow->stern, port side back
    outline = deck_r + deck_l[::-1]

    return verts, faces, outline


def write_obj(path: Path, verts, faces, name: str, outline: list):
    lines = [f"# procedural hull: {name}", "o hull"]
    lines += [f"v {x:.2f} {y:.2f} {z:.2f}" for x, y, z in verts]
    lines += [f"f {a+1} {b+1} {c+1}" for a, b, c in faces]
    # deck outline loop (top-down silhouette for the 2D slice renderer)
    lines.append("o deck_outline")
    lines.append("l " + " ".join(str(i + 1) for i in outline))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="ascii")
    print(f"{name}: {len(verts)} verts, {len(faces)} faces -> {path}")


def main():
    fleet = Path("data/ships/generated_fleet.json")
    doc = json.loads(fleet.read_text(encoding="utf-8"))
    test = Path("data/ships/test_vessels.json")
    ships = list(doc["ships"]) + (json.loads(test.read_text(encoding="utf-8"))["ships"] if test.exists() else [])
    by_id = {s["id"]: s for s in ships}
    targets = sys.argv[1:] or sorted(by_id)  # default: the whole fleet
    for ship_id in targets:
        s = by_id[ship_id]
        verts, faces, outline = build_hull(s["lengthM"], s["beamM"], s["draftM"])
        out = Path("assets/models") / ship_id / "hull.obj"
        write_obj(out, verts, faces, ship_id, outline)


if __name__ == "__main__":
    main()
