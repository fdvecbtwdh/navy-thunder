#!/usr/bin/env python3
"""War Thunder ship model package (.grp, Dagor GRP2) extraction tool.

Status (R3.1 spike, 2026-09-21): the GRP2 container is fully parsed and every
entry can be extracted and identified. The RENDER mesh is a compiled "BIM2"
dynmodel (see FORMAT NOTES below) that requires the dag2Tree/btag
deserializer to convert - implementing that is the next workstream. This tool
therefore gives provenance-complete raw extraction, not yet a mesh.

Empirically verified layout (ger_battleship_bismarck.grp, 10,632,026 bytes):

  header (16 B)
    u32 label           'GRP2'
    u32 descOnlySize    912   directory bytes (after this header)
    u32 fullDataSize    920   directory + 8-byte tail (data_base u32, u32 4)
    u32 restFileSize    rest of file = data region size

  directory [16, 16+descOnlySize)
    [0x10] u32 name table start (64)   - 13 z-strings, first at +64
    [0x1b0] u32 offsets[13]            - entry-name offsets (relative to +64)
    [0x1e4] records (12 B each, 12 entries):
        u32 hash | u32 abs_offset_in_file | u32 entry_index
    [0x280..] hash lookup table (hash, idx<<16|idx) slots
    [0x390] tail: u32 data_base (928), u32 4

  data region [16+fullDataSize .. EOF), entries referenced by abs offset.
  Entry formats observed (Bismarck):
    sections(.bat 7.5K), sections_remote, char('chr1' 32 B),
    skeleton(u32 count + float bone matrices), MAIN MODEL('BIM2' dynmodel,
    4.7 MB, compressed vertex streams), dm_skeleton, collision
    (u32 0xace50003 + zstd frame; quantized BVH, NOT a raw triangle soup),
    dmg_skeleton, dmg('BIM2'), fastphys('fph3' node params),
    xray_skeleton, xray('BIM2'), main_ship_animtree.

References: GaijinEntertainment/DagorEngine (BSD) - gameResSystem.cpp (GrpHeader),
dagFileFormat.h (classic DAG record format), ioSys/dag_btagCompr.h (NONE/ZSTD/OODLE
block compression). The BIM2 runtime serialization lives deeper in the render
dynmodel stack.
"""
import argparse
import json
import struct
import sys
import zlib
from pathlib import Path

try:
    import zstandard
except ImportError:
    zstandard = None

GRP_MAGIC = 0x32505247  # 'GRP2'


def parse_grp(data: bytes):
    """Returns (names, entries); entries = list of dicts with offset/size/hash."""
    if len(data) < 16 or struct.unpack_from("<I", data, 0)[0] != GRP_MAGIC:
        raise ValueError("not a GRP2 file")
    desc_only, full_data, rest = struct.unpack_from("<III", data, 4)
    table_off = struct.unpack_from("<I", data, 16)[0]  # 432: name-offset table
    count = struct.unpack_from("<I", data, 20)[0]      # 13 names/entries

    # names: count z-strings; the table holds ABSOLUTE string offsets (first = 64)
    names = []
    for i in range(count):
        off = struct.unpack_from("<I", data, table_off + i * 4)[0]
        end = data.index(b"\0", off)
        names.append(data[off:end].decode("ascii", "replace"))

    # entry records follow the offsets table: (hash, abs offset, index) x count
    rec = table_off + count * 4
    entries = []
    for i in range(count):
        h, off, idx = struct.unpack_from("<III", data, rec + i * 12)
        if off == 0:
            continue  # main_ship_animtree: listed in the name table, no data record
        entries.append({"hash": h, "offset": off, "index": idx})

    # sizes: next record's offset; last entry runs to EOF
    for i, e in enumerate(entries):
        e["name"] = names[e["index"]] if e["index"] < len(names) else f"entry{e['index']}"
        e["size"] = (entries[i + 1]["offset"] if i + 1 < len(entries) else len(data)) - e["offset"]
    # hash table sits between the records and the data region; trim the last
    # entry if it swallows the tail table (tail = data_base u32 + u32 4)
    if len(entries) >= 2:
        data_base = struct.unpack_from("<I", data, 16 + desc_only - 8)[0]
        tail = len(data) - (16 + desc_only + 8)  # approximate guard
        last = entries[-1]
        if last["offset"] > data_base + tail:
            last["size"] = len(data) - last["offset"]
    return names, entries


def classify(payload: bytes) -> str:
    if payload[:4] == b"chr1":
        return "char_def"
    if payload[4:8] == b"fph3":
        return "fastphys"
    if payload[60:64] == b"BIM2":
        return "dynmodel_bim2"
    if len(payload) > 12 and payload[8:12] == b"\x28\xb5\x2f\xfd":
        return "zstd_compressed"
    if len(payload) > 8 and payload[8] == 0x00 and payload[9] == 0x00 and payload[12:16] == b"\x00\x00\x80\x3f":
        return "skeleton"
    return "unknown"


def extract(payload: bytes) -> bytes:
    """Decompress zstd-wrapped payloads (collision etc.); pass everything else."""
    if len(payload) > 12 and payload[8:12] == b"\x28\xb5\x2f\xfd":
        if zstandard is None:
            raise RuntimeError("zstandard module required for compressed entries")
        return zstandard.ZstdDecompressor().decompress(payload[8:], max_output_size=1 << 30)
    return payload


EXTENSIONS = {
    "dynmodel_bim2": ".bim2",
    "zstd_compressed": ".zstd.bin",
    "skeleton": ".skeleton",
    "fastphys": ".fph3",
    "char_def": ".char",
    "unknown": ".bin",
}


def extract_ship(grp_path: Path, out_dir: Path) -> dict:
    data = grp_path.read_bytes()
    names, entries = parse_grp(data)
    out_dir.mkdir(parents=True, exist_ok=True)
    manifest = {"source": str(grp_path), "size": len(data), "entries": []}
    for e in entries:
        payload = data[e["offset"]: e["offset"] + e["size"]]
        kind = classify(payload)
        try:
            raw = extract(payload)
            dec = "zstd" if raw is not payload else "none"
        except Exception as exc:  # noqa: BLE001 - spike tool: report and continue
            raw, dec = payload, f"FAILED: {exc}"
        fname = f"{e['name']}{EXTENSIONS.get(kind, '.bin')}"
        (out_dir / fname).write_bytes(raw)
        manifest["entries"].append(
            {"name": e["name"], "format": kind, "offset": e["offset"],
             "packedSize": e["size"], "size": len(raw), "compression": dec, "file": fname})
        print(f"  {e['name']:42s} {kind:16s} {e['size']:9d} -> {len(raw):9d}")
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    return manifest


def inventory(grp_dir: Path, out_json: Path) -> None:
    inv = []
    for p in sorted(grp_dir.glob("*.grp")):
        data = p.read_bytes()
        try:
            names, entries = parse_grp(data)
        except ValueError:
            continue
        inv.append({"ship": p.stem, "file": p.name, "size": len(data),
                    "entries": [{"name": e["name"], "offset": e["offset"], "size": e["size"],
                                 "hash": e["hash"]} for e in entries]})
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps({"count": len(inv), "ships": inv}, indent=1), encoding="utf-8")
    print(f"inventory: {len(inv)} ships -> {out_json}")


def main():
    ap = argparse.ArgumentParser(description="extract WT ship model .grp (GRP2) packages")
    ap.add_argument("grp", type=Path, help=".grp file, or a directory with --inventory")
    ap.add_argument("--out", type=Path, default=Path("assets/raw/models"))
    ap.add_argument("--inventory", action="store_true", help="batch-scan a directory to JSON")
    args = ap.parse_args()

    if args.inventory:
        inventory(args.grp, args.out / "model_inventory.json")
        return
    manifest = extract_ship(args.grp, args.out / args.grp.stem)
    print(f"extracted {len(manifest['entries'])} entries -> {args.out / args.grp.stem}")


if __name__ == "__main__":
    sys.exit(main())
