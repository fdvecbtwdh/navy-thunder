"""Phase 01 geometry audit: add fore/aft transverse bulkheads to the 8 primary ships.

All 8 ships previously carried only side belts (+/-X faces) and a deck (YMax) — a
bow-on or stern-on shell met no armor at all. This adds the citadel end bulkheads
(ZMin/ZMax faces) at the main belt's keel extent, spanning the belt's beam and
vertical band. Thicknesses are Tier-3 hand values from published armor schemes
(approximations), flagged in the ship's source notes. Idempotent by plate id.
"""
import json

# ship id -> (bulkhead thickness mm, source note)
BULKHEAD_MM = {
    "test_battleship": 100,
    "test_destroyer": 20,
    "uss_iowa": 150,
    "uss_north_carolina": 100,
    "uss_fletcher": 13,
    "ijn_nagato": 100,
    "ijn_kongo": 100,
    "uss_baltimore": 75,
}

NOTE = ("Phase 01 geometry audit: fore/aft citadel bulkheads added "
        "(Tier-3 hand values from published armor schemes, approximation).")


def add_bulkheads(ship):
    sid = ship["id"]
    if sid not in BULKHEAD_MM:
        return False
    existing = {p["id"] for p in ship["armorPlates"]}
    # Main belt = the +/-X-faced plate with the widest keel (z) extent.
    belts = [p for p in ship["armorPlates"] if p["face"] in ("XMin", "XMax")]
    if not belts:
        return False
    main = max(belts, key=lambda p: p["zMaxM"] - p["zMinM"])
    z_aft, z_fwd = main["zMinM"], main["zMaxM"]
    t = BULKHEAD_MM[sid]
    added = False
    for name, z, face in (
        (f"{sid}_bulkhead_fwd", z_fwd, "ZMax"),
        (f"{sid}_bulkhead_aft", z_aft, "ZMin"),
    ):
        if name in existing:
            continue
        ship["armorPlates"].append({
            "id": name,
            "xMinM": main["xMinM"], "xMaxM": main["xMaxM"],
            "yMinM": main["yMinM"], "yMaxM": main["yMaxM"],
            "zMinM": z, "zMaxM": z,
            "face": face,
            "thicknessMm": t,
        })
        added = True
    if added:
        src = ship.setdefault("source", {})
        notes = src.get("notes", "")
        if NOTE not in notes:
            src["notes"] = (notes + "; " if notes else "") + NOTE
    return added


def process(path, key="ships"):
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    changed = 0
    for ship in doc[key]:
        if add_bulkheads(ship):
            changed += 1
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"{path}: {changed} ships updated")


process("data/ships/test_vessels.json")
process("data/ships/generated_fleet.json")
