#!/usr/bin/env python3
"""Templated ship-fleet generator (MDR-0007/0016, Tier-2 data).

Expands a parameter table of historical ship classes into full NavyThunder shipSet
JSON: compartment layout follows the class template (bow/mid/stern sections, magazines,
boilers, engines, steering, fire control, pumps, turrets with first-stage racks), with
displacement/crew/armor taken from the historical parameter rows.

Layouts are TEMPLATES parameterised by real hull data - hand-refinement per ship is a
continuous data task (the schema already supports bespoke layouts).
"""
import json
from pathlib import Path

# name, class, displacement_t, length_m, beam_m, draft_m, crew, repair_pct, survive_pct,
# belt_mm, deck_mm, turret_groups, generation
FLEET = [
    ("uss_fletcher", "Destroyer", 2500, 114, 11, 3.9, 329, 0.55, 0.45, 13, 13, 1, 4),
    ("uss_sims", "Destroyer", 1570, 106, 11, 3.5, 251, 0.55, 0.45, 13, 13, 1, 3),
    ("uss_gearing", "Destroyer", 2616, 119, 12.5, 4.4, 336, 0.55, 0.45, 13, 13, 1, 4),
    ("uss_sumner", "Destroyer", 3200, 114.7, 12.4, 4.5, 336, 0.55, 0.45, 13, 13, 1, 4),
    ("ijn_kagero", "Destroyer", 2500, 118, 10.8, 3.8, 240, 0.5, 0.4, 12, 12, 1, 3),
    ("ijn_fubuki", "Destroyer", 2090, 115, 10.4, 3.2, 219, 0.5, 0.4, 12, 12, 1, 3),
    ("dkm_z23", "Destroyer", 2600, 127, 12, 4.2, 321, 0.55, 0.45, 16, 12, 1, 3),
    ("rn_tribal", "Destroyer", 2520, 115, 11.2, 3.5, 259, 0.5, 0.4, 13, 13, 1, 3),
    ("uss_atlanta", "Cruiser", 7400, 165, 16, 6.1, 673, 0.5, 0.4, 89, 30, 2, 3),
    ("uss_brooklyn", "Cruiser", 12207, 185, 19, 7, 868, 0.5, 0.4, 140, 51, 2, 3),
    ("uss_baltimore", "Cruiser", 13600, 205, 21.6, 7.2, 1142, 0.5, 0.4, 152, 65, 2, 4),
    ("uss_new_orleans", "Cruiser", 10360, 179, 18.8, 6.9, 849, 0.5, 0.4, 127, 57, 2, 3),
    ("ijn_myoko", "Cruiser", 15493, 204, 19.5, 6.4, 920, 0.5, 0.4, 102, 35, 2, 3),
    ("ijn_mogami", "Cruiser", 12400, 200, 18, 6.1, 850, 0.5, 0.4, 100, 35, 2, 3),
    ("dkm_prinz_eugen", "Cruiser", 16970, 212, 22, 7.2, 1900, 0.5, 0.4, 80, 30, 2, 3),
    ("rn_edinburgh", "Cruiser", 11550, 193, 19.3, 6.4, 850, 0.5, 0.4, 114, 32, 2, 3),
    ("uss_northampton", "Cruiser", 9200, 182, 18.8, 7.2, 617, 0.5, 0.4, 95, 38, 2, 3),
    ("uss_penelope", "Cruiser", 5700, 154, 15.5, 5.3, 550, 0.5, 0.4, 82, 30, 2, 3),
    ("uss_nevada", "Battleship", 29500, 178, 29, 8.7, 1500, 0.5, 0.42, 343, 76, 3, 2),
    ("uss_pennsylvania", "Battleship", 32400, 185, 32, 10, 1900, 0.5, 0.42, 343, 89, 3, 2),
    ("uss_new_mexico", "Battleship", 34000, 190, 32, 10, 1900, 0.5, 0.42, 343, 89, 3, 3),
    ("uss_colorado", "Battleship", 32600, 190, 30, 9.3, 1900, 0.5, 0.42, 343, 89, 3, 3),
    ("uss_north_carolina", "Battleship", 41500, 222, 33, 10.4, 2100, 0.5, 0.42, 305, 104, 3, 4),
    ("uss_south_dakota", "Battleship", 40500, 210, 33, 10.4, 2250, 0.5, 0.42, 310, 132, 3, 4),
    ("uss_iowa", "Battleship", 57500, 262, 33, 11.3, 2700, 0.5, 0.42, 307, 157, 3, 4),
    ("uss_california", "Battleship", 35500, 190, 32, 10, 2100, 0.5, 0.42, 343, 89, 3, 3),
    ("ijn_kongo", "Battlecruiser", 37000, 222, 29, 9.7, 1400, 0.45, 0.38, 203, 80, 3, 2),
    ("ijn_nagato", "Battleship", 42850, 215, 29, 9.4, 1700, 0.5, 0.4, 305, 76, 3, 3),
    ("rn_renown", "Battlecruiser", 32000, 242, 27.4, 9.7, 1200, 0.45, 0.38, 229, 76, 3, 2),
    ("rms_bismarck", "Battleship", 51000, 251, 36, 9.9, 2200, 0.5, 0.42, 320, 110, 3, 3),
]


def build_ship(row):
    (name, cls, disp, length, beam, draft, crew, repair, survive,
     belt_mm, deck_mm, turret_groups, generation) = row

    half_l = length / 2
    half_b = beam / 2
    keel = -draft
    deck_y = draft * 0.9 + 1.0

    capital = cls in ("Battleship", "Battlecruiser", "Cruiser", "Frigate")
    mid_sections = 3 if capital else 1
    section_width = (0.7 * length) / max(1, mid_sections)
    sections = [{
        "id": f"{name}_bow", "role": "Bow", "hp": round(disp * 0.9), "xMinM": 0.6 * half_l, "xMaxM": half_l,
    }]
    for i in range(mid_sections):
        x_max = 0.6 * half_l - i * section_width
        x_min = x_max - section_width
        sections.append({
            "id": f"{name}_mid{i + 1}", "role": "Mid", "hp": round(disp * 1.2),
            "xMinM": round(x_min, 1), "xMaxM": round(x_max, 1),
        })
    sections.append({
        "id": f"{name}_stern", "role": "Stern", "hp": round(disp * 0.9),
        "xMinM": -half_l, "xMaxM": round(-0.6 * half_l, 1),
    })

    mag_count = max(2, turret_groups)
    parts = []
    buoyancy = 0.0

    def add(pid, kind, section, hp, crew_n, x0, x1, y0, y1, z0, z1, share, open_=False, group=None):
        nonlocal buoyancy
        parts.append({
            "id": pid, "kind": kind, "sectionId": section, "hp": round(hp), "crew": crew_n,
            "xMinM": round(x0, 1), "xMaxM": round(x1, 1),
            "yMinM": round(y0, 1), "yMaxM": round(y1, 1),
            "zMinM": round(z0, 1), "zMaxM": round(z1, 1),
            "buoyancySharePct": share, **({"open": True} if open_ else {}),
            **({"turretGroup": group} if group else {}),
        })
        buoyancy += share

    mid_ids = [f"{name}_mid{i + 1}" for i in range(mid_sections)]
    crew_share = 10.0 / len(mid_ids)
    for i, mid in enumerate(mid_ids):
        x_max = sections[1 + i]["xMaxM"]
        x_min = sections[1 + i]["xMinM"]
        span = (x_max - x_min) / max(1, mag_count // 2 if mag_count > 2 else 1)
        add(f"{name}_crew_{i + 1}", "Compartment", mid, disp * 0.02,
            round(crew * 0.12 / len(mid_ids)), x_min + 1, x_max - 1,
            keel + 1, deck_y - 2, -half_b + 1, half_b - 1, crew_share)

    for i in range(mag_count):
        section = mid_ids[i % len(mid_ids)]
        x_center = -0.3 * half_l + i * (0.6 * half_l / max(1, mag_count - 1))
        add(f"{name}_mag_{chr(65 + i)}", "Magazine", section, disp * 0.012,
            round(crew * 0.02), x_center - 6, x_center + 6,
            keel + 0.5, keel + draft * 0.4, -half_b + 1.5, half_b - 1.5, 14.0 / mag_count)

    add(f"{name}_boiler_1", "Boiler", mid_ids[0], disp * 0.015, round(crew * 0.03),
        -half_b * 0.0 + 0.5, 0.5, keel + 0.5, 1, -half_b + 2, half_b - 2, 9)
    add(f"{name}_engine_1", "Engine", mid_ids[-1], disp * 0.015, round(crew * 0.03),
        0.5, 0.5 + 6, keel + 0.5, 1.5, -half_b + 2, half_b - 2, 9)
    add(f"{name}_steering", "Steering", f"{name}_stern", disp * 0.008, round(crew * 0.01),
        -half_l + 5, -half_l + 15, keel + 1, 0, -half_b + 2, half_b - 2, 6)
    add(f"{name}_fuel", "FuelTank", mid_ids[len(mid_ids) // 2], disp * 0.01, 0,
        -6, 6, keel + 0.5, keel + draft * 0.3, -half_b + 2, half_b - 2, 6)
    add(f"{name}_bridge", "Compartment", mid_ids[0], disp * 0.008, round(crew * 0.03),
        -2, 2, deck_y - 1, deck_y + 2, -half_b + 2, half_b - 2, 2)
    add(f"{name}_fcs", "FireControl", mid_ids[0], disp * 0.004, round(crew * 0.015),
        2.5, 5, deck_y + 1, deck_y + 3, -half_b + 3, half_b - 3, 1)
    add(f"{name}_radar", "Radar", mid_ids[0], disp * 0.002, 5,
        -5, -3, deck_y + 2, deck_y + 3.5, -half_b + 3, half_b - 3, 0)
    for p in range(max(1, len(mid_ids) // 2)):
        add(f"{name}_pump_{p + 1}", "Pump", mid_ids[p % len(mid_ids)], disp * 0.003, 4,
            -8 + p * 10, -6 + p * 10, keel + 0.5, keel + draft * 0.5, -half_b + 2, half_b - 2, 0)
    for g in range(turret_groups):
        x = half_l * (0.75 - 0.45 * g / max(1, turret_groups - 1) if turret_groups > 1 else 0.7)
        add(f"{name}_turret_{chr(65 + g)}", "Turret",
            f"{name}_bow" if x > 0.5 * half_l else mid_ids[0],
            disp * 0.006, round(crew * 0.04), x - 5, x + 5,
            deck_y - 2, deck_y + 2, -half_b + 3, half_b - 3,
            2.0 / turret_groups, open_=True, group=chr(65 + g))
    if abs(buoyancy - 100) > 1:
        parts[-1]["buoyancySharePct"] = round(parts[-1]["buoyancySharePct"] + (100 - buoyancy), 2)

    plates = [
        {"id": f"{name}_belt_port", "xMinM": -half_l, "xMaxM": half_l,
         "yMinM": keel, "yMaxM": deck_y, "zMinM": -half_b, "zMaxM": -half_b,
         "face": "ZMin", "thicknessMm": belt_mm},
        {"id": f"{name}_belt_starboard", "xMinM": -half_l, "xMaxM": half_l,
         "yMinM": keel, "yMaxM": deck_y, "zMinM": half_b, "zMaxM": half_b,
         "face": "ZMax", "thicknessMm": belt_mm},
        {"id": f"{name}_deck", "xMinM": -half_l, "xMaxM": half_l,
         "yMinM": deck_y, "yMaxM": deck_y, "zMinM": -half_b, "zMaxM": half_b,
         "face": "YMax", "thicknessMm": deck_mm},
    ]

    return {
        "id": name,
        "displayName": name.replace("_", " ").upper(),
        "class": cls,
        "displacementT": disp,
        "lengthM": length,
        "beamM": beam,
        "draftM": draft,
        "crewTotal": crew,
        "crewRepairThreshold": round(crew * repair),
        "crewSurviveThreshold": round(crew * survive),
        "firstStageRoundsPerTurret": 30 if not capital else 40,
        "resupplySeconds": 35,
        "dcGeneration": generation,
        "pumpCapacityPerSecond": 0.02,
        "capsizeAngleDeg": 40,
        "hullSections": sections,
        "parts": parts,
        "armorPlates": plates,
        "source": {
            "origin": "hand_authored",
            "notes": "Templated layout expanded from historical parameters "
                     "(displacement/crew/armor); compartment layout is a class template, "
                     "not a ship-specific survey - Tier-2 data.",
            "versionStamp": "generated 2026-09-19",
        },
    }


def main():
    ships = [build_ship(row) for row in FLEET]
    doc = {
        "schemaVersion": 1,
        "kind": "shipSet",
        "ships": ships,
    }
    out = Path("data/ships/generated_fleet.json")
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"generated {len(ships)} ships -> {out}")


if __name__ == "__main__":
    main()
