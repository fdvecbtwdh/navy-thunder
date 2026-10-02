"""Phase 01 one-shot data migration: ship-local frame X-bow -> Z-bow (PROJECT_DESIGN 5.2).

- hullSections: keel slab keys xMinM/xMaxM -> zMinM/zMaxM (values unchanged).
- parts: full boxes, swap x<->z values (xMinM<->zMinM, xMaxM<->zMaxM).
- armorPlates: swap x<->z values AND remap face XMin<->ZMin, XMax<->ZMax (Y faces unchanged).
- aircraft parts: same box swap (nose moves from +X to +Z).
Idempotent per invocation pair? No - running twice would swap back. Run once.
"""
import json

FACE_SWAP = {"XMin": "ZMin", "ZMin": "XMin", "XMax": "ZMax", "ZMax": "XMax"}


def swap_box(d):
    d["xMinM"], d["zMinM"] = d["zMinM"], d["xMinM"]
    d["xMaxM"], d["zMaxM"] = d["zMaxM"], d["xMaxM"]


def migrate_ship(ship):
    for sec in ship.get("hullSections", []):
        sec["zMinM"] = sec.pop("xMinM")
        sec["zMaxM"] = sec.pop("xMaxM")
    for part in ship.get("parts", []):
        swap_box(part)
    for plate in ship.get("armorPlates", []):
        swap_box(plate)
        plate["face"] = FACE_SWAP.get(plate["face"], plate["face"])


def migrate_aircraft(ac):
    for part in ac.get("parts", []):
        swap_box(part)


def migrate(path, kind):
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    if kind == "shipSet":
        for ship in doc["ships"]:
            migrate_ship(ship)
    else:
        for ac in doc["aircraft"]:
            migrate_aircraft(ac)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("migrated", path)


migrate("data/ships/test_vessels.json", "shipSet")
migrate("data/ships/generated_fleet.json", "shipSet")
migrate("data/aircraft/test_fighter.json", "aircraftSet")
