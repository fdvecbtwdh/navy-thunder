#!/usr/bin/env python3
"""War Thunder binary blk (Dagor BBF3 "bin dump with shared namemap") decoder.

Format ground truth: GaijinEntertainment/DagorEngine (public repo)
  prog/engine/ioSys/dataBlock/blk_serialize.cpp  (loadFromBinDump / read_names_base)
  prog/dagorInclude/ioSys/dag_dataBlock.h        (Param bitfield, get_type_size)

After vromfs unpacking (packed_type byte stripped + zstd with archive dict),
the decompressed stream layout is:

  varint localNameCount            names NOT present in the shared vromfs namemap
  if >0: varint localNamesSize, then blob of NUL-terminated strings
  varint blockCount                total blocks, BFS order, root first
  varint paramCount                total params across all blocks
  varint complexSize               bytes of out-of-line value storage
  complexSize bytes                (point2/3/4, ipoint2/3/4, matrix, int64, string payloads)
  paramCount * 8 bytes             Param { u32 nameId:24 | type:8 ; u32 v }
  per block (blockCount entries):
    varint nameId (1-based into global name list, 0 = no name)
    varint paramCount (params consumed sequentially from the global param array)
    varint blockCount
    if >0: varint firstBlockId

Global name id space: shared vromfs 'nm' names first (ordinals 0..N-1),
then local names appended (N..). String param v with bit31 set is interned in
that name list; otherwise v is an offset into complex storage (NUL-terminated).

namemap file ('nm' in the vromfs): 40-byte header (u64 hash + 32-byte zstd dict
hash, already stripped by the unpacker), then varint nameCount, varint
nameSize, then the same blob layout.

Duplicate param/block names are preserved: each name maps to a list.
"""
from __future__ import annotations

import struct
from typing import Any

# dag_dataBlock.h ParamType
TYPE_NONE, TYPE_STRING, TYPE_INT, TYPE_REAL, TYPE_POINT2, TYPE_POINT3, \
    TYPE_POINT4, TYPE_IPOINT2, TYPE_IPOINT3, TYPE_BOOL, TYPE_E3DCOLOR, \
    TYPE_MATRIX, TYPE_INT64, TYPE_IPOINT4 = range(14)

# get_type_size(): bytes in the complex storage (0 => inline in Param.v)
_TYPE_SIZE = {0: 0, 1: 8, 2: 4, 3: 4, 4: 8, 5: 12, 6: 16, 7: 8, 8: 12,
              9: 1, 10: 4, 11: 48, 12: 8, 13: 16}

_IS_NAMEMAP_ID = 0x80000000

MAX_NAMES = 8 << 20          # sanity caps mirroring BLK_MAX_MEMORY_FOR_NAMES
MAX_COMPLEX = 256 << 20


class BlkError(ValueError):
    pass


def read_varint(buf: bytes, p: int) -> tuple[int, int]:
    """Dagor writeCompressedUnsignedGeneric: 7 bits/byte, LSB first, MSB = continue."""
    v = 0
    sh = 0
    while True:
        if p >= len(buf):
            raise BlkError("varint runs past end of buffer")
        b = buf[p]
        p += 1
        v |= (b & 0x7F) << sh
        if not (b & 0x80):
            return v, p
        sh += 7
        if sh > 35:
            raise BlkError("varint too long")


class BlkNode:
    """One block: ordered params and sub-blocks; duplicate names allowed."""

    __slots__ = ("name", "params", "blocks", "order")

    def __init__(self, name: str):
        self.name = name
        self.params: dict[str, list[Any]] = {}
        self.blocks: dict[str, list["BlkNode"]] = {}
        self.order: list[tuple[str, str, Any]] = []  # ('p'|'b', name, value)

    # -- convenience accessors -------------------------------------------
    def get(self, name: str, default=None, index: int = 0):
        vals = self.params.get(name)
        if not vals or index >= len(vals):
            return default
        return vals[index]

    def get_all(self, name: str) -> list[Any]:
        return self.params.get(name, [])

    def get_block(self, name: str, index: int = 0):
        lst = self.blocks.get(name)
        if not lst or index >= len(lst):
            return None
        return lst[index]

    def get_blocks(self, name: str) -> list["BlkNode"]:
        return self.blocks.get(name, [])

    def to_dict(self) -> dict:
        """Collapse single-element lists; blocks merged with params (blk text semantics)."""
        out: dict[str, Any] = {}
        for name, vals in self.params.items():
            out[name] = vals[0] if len(vals) == 1 else vals
        for name, lst in self.blocks.items():
            val = [b.to_dict() for b in lst]
            if name in out:
                prev = out[name]
                out[name] = ([prev] if not isinstance(prev, list) or
                             (prev and isinstance(prev[0], (int, float, str, bool))) else prev)
                out[name] = out[name] + val if isinstance(out[name], list) else val
            else:
                out[name] = val[0] if len(val) == 1 else val
        return out

    def __repr__(self):
        return f"BlkNode({self.name!r}, params={len(self.params)}, blocks={len(self.blocks)})"


def load_namemap(nm: bytes, blob_size_known: int | None = None) -> list[str]:
    """Parse the shared vromfs namemap ('nm' payload, header already stripped).

    Returns ordinal-indexed names. Tolerates a truncated tail (zstd salvage):
    only complete strings become usable ids.
    """
    count, p = read_varint(nm, 0)
    size, p = read_varint(nm, p)
    if count > MAX_NAMES or size > MAX_COMPLEX:
        raise BlkError(f"implausible namemap header count={count} size={size}")
    names: list[str] = []
    pos = p
    end = min(len(nm), p + size)
    for _ in range(count):
        nul = nm.find(b"\x00", pos, end)
        if nul < 0:
            break  # truncated tail
        names.append(nm[pos:nul].decode("utf-8", "replace"))
        pos = nul + 1
    return names


def _read_string(cdata: bytes, off: int) -> str:
    if off >= len(cdata):
        raise BlkError(f"string offset {off} out of complex storage {len(cdata)}")
    nul = cdata.find(b"\x00", off)
    if nul < 0:
        nul = len(cdata)
    return cdata[off:nul].decode("utf-8", "replace")


def decode_blk(data: bytes, names: list[str], name: str = "") -> BlkNode:
    """Decode a decompressed BBF3 bin dump (post zstd) into a BlkNode tree.

    `names` is the global name list: shared vromfs namemap ordinals, with any
    file-local names appended by this function's caller-facing local section
    (parsed here and returned via node-local resolution transparently).
    """
    p = 0
    local_cnt, p = read_varint(data, p)
    base = len(names)
    if local_cnt:
        local_sz, p = read_varint(data, p)
        if local_cnt > MAX_NAMES or local_sz > MAX_COMPLEX:
            raise BlkError("implausible local namemap header")
        blob_end = min(len(data), p + local_sz)
        pos = p
        added = 0
        while added < local_cnt:
            nul = data.find(b"\x00", pos, blob_end)
            if nul < 0:
                break
            names.append(data[pos:nul].decode("utf-8", "replace"))
            added += 1
            pos = nul + 1
        p = blob_end

    n_blocks, p = read_varint(data, p)
    n_params, p = read_varint(data, p)
    csize, p = read_varint(data, p)
    if n_blocks > (2 << 20) or n_params > (4 << 20) or csize > MAX_COMPLEX:
        raise BlkError(f"implausible blk header blocks={n_blocks} params={n_params} complex={csize}")
    if p + csize + n_params * 8 > len(data):
        raise BlkError(f"truncated blk: need {p + csize + n_params * 8} bytes, have {len(data)}")

    cdata = data[p:p + csize]
    parr = p + csize

    # -- params ----------------------------------------------------------
    plist: list[tuple[int, int, Any]] = []
    for i in range(n_params):
        hdr, v = struct.unpack_from("<II", data, parr + i * 8)
        name_id = hdr & 0xFFFFFF
        ptype = (hdr >> 24) & 0xFF
        if ptype >= TYPE_IPOINT4 + 1 or name_id >= len(names):
            raise BlkError(f"param {i}: bad type {ptype} or nameId {name_id}")
        val: Any
        if ptype == TYPE_STRING:
            if v & _IS_NAMEMAP_ID:
                sid = v & ~_IS_NAMEMAP_ID
                val = names[sid] if sid < len(names) else None
            else:
                val = _read_string(cdata, v)
        elif ptype == TYPE_BOOL:
            val = bool(v)
        elif ptype == TYPE_INT:
            val = struct.unpack("<i", struct.pack("<I", v))[0]
        elif ptype == TYPE_REAL:
            val = struct.unpack("<f", struct.pack("<I", v))[0]
        elif ptype == TYPE_E3DCOLOR:
            val = v  # raw ARGB dword
        else:
            sz = _TYPE_SIZE[ptype]
            if v + sz > csize:
                raise BlkError(f"param {i}: complex value {v}+{sz} out of storage {csize}")
            raw = cdata[v:v + sz]
            if ptype in (TYPE_POINT2, TYPE_POINT3, TYPE_POINT4, TYPE_MATRIX):
                val = list(struct.unpack("<%df" % (sz // 4), raw))
            elif ptype in (TYPE_IPOINT2, TYPE_IPOINT3, TYPE_IPOINT4):
                val = list(struct.unpack("<%di" % (sz // 4), raw))
            elif ptype == TYPE_INT64:
                val = struct.unpack("<q", raw)[0]
            else:
                raise BlkError(f"unhandled type {ptype}")
        plist.append((name_id, ptype, val))

    # -- blocks (BFS order, params consumed sequentially) ----------------
    nodes: list[BlkNode] = []
    descs = []
    q = parr + n_params * 8
    for _ in range(n_blocks):
        nid, q = read_varint(data, q)
        pc, q = read_varint(data, q)
        bc, q = read_varint(data, q)
        fb = 0
        if bc:
            fb, q = read_varint(data, q)
        if pc > n_params or (bc and fb + bc > n_blocks):
            raise BlkError("inconsistent block layout")
        descs.append((nid, pc, bc, fb))
    if q > len(data):
        raise BlkError("block descriptors run past end of data")

    cursor = 0
    for i, (nid, pc, bc, fb) in enumerate(descs):
        nm = names[nid - 1] if 0 < nid <= len(names) else ""
        node = BlkNode(nm)
        for j in range(pc):
            pname, ptype, val = plist[cursor + j]
            node.params.setdefault(names[pname], []).append(val)
            node.order.append(("p", names[pname], val))
        cursor += pc
        nodes.append(node)
    # link children after all nodes exist
    for i, (nid, pc, bc, fb) in enumerate(descs):
        if bc:
            node = nodes[i]
            for j in range(fb, fb + bc):
                child = nodes[j]
                node.blocks.setdefault(child.name, []).append(child)
                node.order.append(("b", child.name, child))
    if local_cnt:
        del names[base:]  # restore caller's list
    return nodes[0]


# ---------------------------------------------------------------------------
# CLI: decode files given a namemap dump
# ---------------------------------------------------------------------------
def _main():
    import argparse
    import json
    import sys

    sys.path.insert(0, __file__.rsplit("/", 1)[0] if "/" in __file__ else ".")
    ap = argparse.ArgumentParser(description="decode WT binary blk (BBF3 bin dump)")
    ap.add_argument("blk", help="decompressed blk file (or '-' for stdin)")
    ap.add_argument("namemap", help="namemap payload file (zstd already stripped, 40B header kept)")
    ap.add_argument("--text", action="store_true", help="print as JSON instead of summary")
    a = ap.parse_args()
    raw = sys.stdin.buffer.read() if a.blk == "-" else open(a.blk, "rb").read()
    names = load_namemap(open(a.namemap, "rb").read())
    node = decode_blk(raw, names)
    if a.text:
        json.dump(node.to_dict(), sys.stdout, ensure_ascii=False, indent=1)
    else:
        for kind, n, v in node.order:
            print(kind, n, "=", v if kind == "p" else "{...}")
        print(f"[{len(node.params)} param names, {sum(len(x) for x in node.blocks.values())} sub-blocks]")


if __name__ == "__main__":
    _main()
