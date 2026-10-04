#!/usr/bin/env python3
"""BIM2 (Dagor dynmodel v7) -> glTF 2.0 converter for Navy Thunder Phase 03.

Converts the compiled ship model inside a War Thunder .grp package into per-LOD
.glb files usable by Godot 4, plus a provenance report. Input = the user's own
game client (LICENSE_AUDIT assumption 3); no WT data is redistributed.

Format knowledge (fully verified against ger_battleship_bismarck 2026-10-03,
cross-checked with open-source DagorEngine headers - see
docs/research/DAGOR_ASSET_RESEARCH.md):

  BIM2 file
    u32 resSz                       DynamicRenderableSceneLodsResource dump size
    i32 -1, i32 -1                  v7: materials separated (dynModelDesc.bin)
    u32 vdataFullCount
    u32 mvhdrSz | 0xC0000000        MatVdataHdr size + compressed marker
    u32 tag                         (OODLE=2)<<30 | compSz
    u32 unpackedSz
    payload [28, 24+compSz)         Oodle Kraken -> MatVdataHdr + VB/IB blobs

  decompressed stream
    [0x00] mat tab 16B              PatchableTab{u64 dptr hi=cnt lo=ofs}, u32, u32
    [0x10] vdata tab 16B            -> VdataHdr array at dptr.lo
    [0x20..vdOfs) vDecl pool        one u32 per channel: (VSDT type << 16) | param
    [vdOfs..] VdataHdr x n (32B)    u32 vcnt | u32 stride:8|ipackedLo:24 |
                                    u32 idxSz:28|ipackedHi:4 | u32 flags |
                                    vDecl PatchableTab 16B
    after header: per gvd            VB (vcnt*stride) then IB (packed? iPacked : idxSz)
                                    (+2B pad when unpacked IB has odd u16 count)

  vertex position (v7): three FLOAT1 channels hold X/Y/Z as 32-bit normalized
  ints (0..2^32-1) lerped across the model bbox stored in the Lods dump
  (CMOD_BOUNDING_PACK). Channel order = X, Y, Z (file order interleaved).

  packed index buffer (VDATA_PACKED_IB): Gaijin meshopt index-sequence variant -
  LEB128 value; bit0 selects one of two delta buffers, bit1 = negative;
  value = decoded >> 2 (inverted when negative); triangles emit (a, c, b).

  element vertex index = ib[i] + baseVertex (D3D drawIndexedBaseVertex semantics,
  cross-checked with DynamicRenderableSceneResource::traceRayRigids).

  after compressed block
    Lods dump (resSz)               lods tab 16B | bbox min/max 24B (whole-struct
                                    swap when min.x > max.x) | bpC254 16B | bpC255 16B |
                                    lod entries 16B {u64 scene lo=dumpSz, f32 range, f32 texScale}
    name map dump                   u32 sz; {names tab 16B, skinNodes tab 16B} +
                                    z-strings + (u32 ofs, u32 id) x n + u16 skinNodes
    per-LOD scene dump              rigids tab 16B | skins tab 16B | rigid x 32B
                                    {u64 mesh lo=dumpSz, f32 sph_c[3], f32 sph_r, i32 nodeId, i32 resv}
    per-rigid mesh dump             elems tab 16B | u16 stageEnd[8] | u32 depr (hi bit=modern) |
                                    RElem x 48B {u64 e, u64 mat lo=matIdx, u64 vdata lo=gvdIdx,
                                    i32 vdOrder, sv, numv, si, numf, baseVertex}

  WT model axes (measured): +X = bow direction, +Y = up, +Z = width axis.
  Navy Thunder standard (PROJECT_DESIGN 8.2): +Z bow, +Y up, +X starboard.
  Transform (right-handed, keeps chirality): NT.x = -WT.z, NT.y = WT.y, NT.z = WT.x.

Usage:
  python tools/convert_bim2_gltf.py <in.grp> [--out assets/models/<ship_id>] [--lod N]...
"""
from __future__ import annotations

import argparse
import ctypes
import json
import os
import struct
import sys
from pathlib import Path

try:
    import zstandard
except ImportError:
    zstandard = None

sys.path.insert(0, str(Path(__file__).parent))
from extract_ship_model import parse_grp  # GRP2 container (shared provenance parser)


# --------------------------------------------------------------------------
# decompression (three-level fallback per PHASE_03 risk table)
# --------------------------------------------------------------------------

_OODLE_ORD = 574  # OodleLZ_Decompress inside Gaijin's daKernel-dev.dll


def _oodle_dll_candidates() -> list[Path]:
    env = os.environ.get("NT_OODLE_DLL")
    cands = [Path(env)] if env else []
    here = Path(__file__).resolve().parent.parent
    cands += [
        here / "_wt_audit" / "dakernel" / "daKernel-dev.dll",
        Path(os.path.expandvars(r"%TEMP%\dk_try1.dll")),
        Path(r"D:\WarThunder\win64\daKernel-dev.dll"),
    ]
    return cands


def _load_oodle():
    for p in _oodle_dll_candidates():
        if not p.exists():
            continue
        try:
            lib = ctypes.CDLL(str(p))
            fn = lib[_OODLE_ORD]
            fn.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t]
            fn.restype = ctypes.c_int64
            return fn
        except OSError:
            continue
    return None


_OODLE = None


def decompress_matvdata(tag: int, payload: bytes) -> bytes:
    global _OODLE
    compr, size = tag >> 30, tag & 0x3FFFFFFF
    if compr == 2:  # OODLE
        if _OODLE is None:
            _OODLE = _load_oodle()
        if _OODLE is None:
            raise RuntimeError(
                "Oodle decompressor not available; put a working daKernel-dev.dll in "
                "_wt_audit/dakernel/ or set NT_OODLE_DLL (docs/asset_pipeline.md)")
        unpacked = struct.unpack_from("<I", payload, 0)[0]
        dst = ctypes.create_string_buffer(unpacked)
        ret = _OODLE(dst, unpacked, payload[4:], len(payload) - 4)
        if ret != unpacked:
            raise RuntimeError(f"oodle returned {ret}, expected {unpacked}")
        return dst.raw[:unpacked]
    if compr == 1:  # ZSTD
        if zstandard is None:
            raise RuntimeError("zstandard module required for zstd blocks")
        return zstandard.ZstdDecompressor().decompress(payload, max_output_size=1 << 31)
    raise RuntimeError(f"unknown matVdata compression {compr}")


# --------------------------------------------------------------------------
# BIM2 parsing
# --------------------------------------------------------------------------

T_FLOAT1, T_FLOAT2, T_FLOAT3, T_FLOAT4 = 0, 1, 2, 3
T_E3DCOLOR, T_UBYTE4, T_SHORT2, T_SHORT4 = 4, 5, 6, 7
T_SHORT2N, T_SHORT4N, T_USHORT2N, T_USHORT4N = 9, 10, 11, 12
T_UDEC3, T_DEC3N, T_HALF2, T_HALF4 = 13, 14, 15, 16
CHSIZE = {0: 4, 1: 8, 2: 12, 3: 16, 4: 4, 5: 4, 6: 4, 7: 8, 9: 4, 10: 8,
          11: 4, 12: 8, 13: 4, 14: 4, 15: 4, 16: 8}

VDATA_PACKED_IB = 0x200
VDATA_LOD_SHIFT = 12


def parse_tab(buf: bytes, o: int) -> tuple[int, int]:
    """PatchableTab on x64: {u64 dptr: hi32=cnt lo32=ofs}, u32 dcnt, u32 pad."""
    dptr, dcnt, _pad = struct.unpack_from("<QII", buf, o)
    cnt = dptr >> 32
    return dptr & 0xFFFFFFFF, (cnt if cnt else dcnt)


def parse_bim2(data: bytes) -> dict:
    """Parse a BIM2 dynmodel entry into raw geometry description."""
    res_sz, neg1a, neg1b, vfullcnt, mvhdr_flags = struct.unpack_from("<IiiII", data, 0)
    if neg1a != -1 or neg1b != -1:
        raise ValueError(f"unsupported BIM2 variant (materials inline: {neg1a:#x},{neg1b:#x})")
    mvhdr_sz = mvhdr_flags & 0x3FFFFFFF
    if (mvhdr_flags >> 30) != 3:
        raise ValueError(f"unexpected matVdata compression marker {mvhdr_flags >> 30}")
    tag, = struct.unpack_from("<I", data, 20)
    stream = decompress_matvdata(tag, data[24:24 + (tag & 0x3FFFFFFF)])

    mat_ofs, mat_cnt = parse_tab(stream, 0)
    vd_ofs, vd_cnt = parse_tab(stream, 16)

    gvd = []
    for i in range(vd_cnt):
        o = vd_ofs + i * 32
        vcnt, bf1, bf2, flags = struct.unpack_from("<IIII", stream, o)
        vdecl_ofs, vdecl_cnt = parse_tab(stream, o + 16)
        ipacked = ((bf1 >> 8) | ((bf2 >> 28) << 24)) if (flags & VDATA_PACKED_IB) else 0
        channels = [struct.unpack_from("<I", stream, vdecl_ofs + c * 4)[0] for c in range(vdecl_cnt)]
        gvd.append({
            "idx": i, "vcnt": vcnt, "stride": bf1 & 0xFF, "idxsize": bf2 & 0x0FFFFFFF,
            "ipacked": ipacked, "flags": flags, "lod": (flags & 0xF000) >> VDATA_LOD_SHIFT,
            "channels": channels,
        })

    o = mvhdr_sz
    for g in gvd:
        vb_sz = g["vcnt"] * g["stride"]
        g["vb"] = stream[o:o + vb_sz]
        o += vb_sz
        ib_sz = g["ipacked"] if g["ipacked"] else g["idxsize"]
        g["ib"] = stream[o:o + ib_sz]
        o += ib_sz
        if not g["ipacked"] and g["idxsize"] % 4 == 2:
            o += 2  # u16 alignment pad after unpacked index buffer

    fo = 24 + (tag & 0x3FFFFFFF)
    ld = data[fo:fo + res_sz]
    lods_ofs, lods_cnt = parse_tab(ld, 0)
    bbox = [struct.unpack_from("<fff", ld, 16), struct.unpack_from("<fff", ld, 28)]
    if bbox[0][0] > bbox[1][0]:  # engine swaps the whole struct when min > max
        bbox = [bbox[1], bbox[0]]
    bpc254 = struct.unpack_from("<ffff", ld, 40)
    bpc255 = struct.unpack_from("<ffff", ld, 56)
    lods = []
    for i in range(lods_cnt):
        scene_val, rng, texscale = struct.unpack_from("<Qff", ld, lods_ofs + i * 16)
        lods.append({"scene_dump_sz": scene_val & 0xFFFFFFFF, "range": rng, "tex_scale": texscale})
    fo += res_sz

    nm_sz, = struct.unpack_from("<I", data, fo)
    nm = data[fo + 4:fo + 4 + nm_sz]
    names_ofs, names_cnt = parse_tab(nm, 0)
    skin_ofs, skin_cnt = parse_tab(nm, 16)
    names = []
    for i in range(names_cnt):
        nofs, _nid = struct.unpack_from("<II", nm, names_ofs + i * 8)
        end = nm.index(b"\0", nofs)
        names.append(nm[nofs:end].decode("utf-8", "replace"))
    skin_nodes = list(struct.unpack_from(f"<{skin_cnt}H", nm, skin_ofs)) if skin_cnt else []
    # rigid.nodeId indexes the node table THROUGH the skinNodes permutation
    # (Dagor-Asset-Explorer semantics: value->index lookup); the direct index
    # yields a scrambled name, which misplaces every transformed rigid.
    skin_lookup = {v: i for i, v in enumerate(skin_nodes)}
    fo += 4 + nm_sz

    for lod in lods:
        sd = data[fo:fo + lod["scene_dump_sz"]]
        fo += lod["scene_dump_sz"]
        rig_ofs, rig_cnt = parse_tab(sd, 0)
        _skin_ofs, skin_cnt_l = parse_tab(sd, 16)
        rigids = []
        for ri in range(rig_cnt):
            ro = rig_ofs + ri * 32
            mesh_val, = struct.unpack_from("<Q", sd, ro)
            sph_c = struct.unpack_from("<fff", sd, ro + 8)
            sph_r, node_id, _resv = struct.unpack_from("<fii", sd, ro + 20)
            mesh_sz = mesh_val & 0xFFFFFFFF
            md = data[fo:fo + mesh_sz]
            fo += mesh_sz
            elems: list[dict] = []
            modern = True
            if mesh_sz:
                el_ofs, el_cnt = parse_tab(md, 0)
                depr, = struct.unpack_from("<I", md, 32)  # after elems tab 16B + stageEnd[8] u16
                modern = bool(depr & 0x80000000)
                for ei in range(el_cnt):
                    eo = el_ofs + ei * 48
                    _e, matv, vdref = struct.unpack_from("<QQQ", md, eo)
                    vdorder, sv, numv, si, numf, basev = struct.unpack_from("<iiiiii", md, eo + 24)
                    elems.append({"mat": matv & 0xFFFFFFFF, "gvd": vdref & 0xFFFFFFFF,
                                  "vd_order": vdorder, "sv": sv, "numv": numv,
                                  "si": si, "numf": numf, "base_vertex": basev})
            node_idx = skin_lookup.get(node_id, node_id)
            rigids.append({"node_id": node_idx,
                           "name": names[node_idx] if node_idx < len(names) else f"node{node_idx}",
                           "sph_c": sph_c, "sph_r": sph_r, "mesh_dump_sz": mesh_sz,
                           "modern_fmt": modern, "elems": elems})
        lod["rigids"] = rigids
        lod["skin_count"] = skin_cnt_l

    return {
        "vdata_full_count": vfullcnt,
        "bbox": bbox, "bpc254": bpc254, "bpc255": bpc255,
        "names": names, "skin_nodes": skin_nodes, "gvd": gvd, "lods": lods,
        "mat_count": mat_cnt, "bytes_consumed": fo, "file_size": len(data),
    }


# --------------------------------------------------------------------------
# geometry decoding
# --------------------------------------------------------------------------

def decode_packed_ib(raw: bytes, count: int) -> list[int]:
    """Gaijin meshopt index-sequence: LEB128, buffer-select bit, zig-zag delta."""
    out: list[int] = []
    buf = [0, 0]
    i, n = 0, len(raw)
    append = out.append
    while len(out) < count and i < n:
        b = raw[i]
        i += 1
        decoded = b & 0x7F
        shift = 7
        while b & 0x80 and i < n:
            b = raw[i]
            i += 1
            decoded |= (b & 0x7F) << shift
            shift += 7
        sel = decoded & 1
        d = (decoded >> 2) ^ -((decoded & 2) != 0)
        v = buf[sel] + d
        buf[sel] = v
        append(v)
    if len(out) < count:
        raise RuntimeError(f"packed IB exhausted: {len(out)}/{count}")
    return out


def decode_vertices(g: dict, bbox: tuple) -> dict:
    """Decode a GlobalVertexData blob.

    Two v7 vertex layouts verified byte-exact on Bismarck (see PHASE_03 notes):

    Layout A - 5 channels [SHORT2, F1, F1, SHORT4N, F1], stride 24:
        [0..4)   SHORT2  narrow-range packed color/AO
        [4..8)   FLOAT1  X as u32 normalized across bbox
        [8..10)  int16   Y as signed normalized (CMOD_SIGNED_PACK) + [10..12) = +1.0 pad
        [12..16) 2x s16n UV0   [16..20) 2x s16n UV1 (lightmap)
        [20..24) FLOAT1  Z as u32 normalized across bbox
    Layout B - 3 channels [SHORT4N, F1, F1], stride 16:
        [0..8)   4x s16n  signed-normalized XYZ (w = +1.0) across bbox
        [8..12)  FLOAT1 u32 normalized UV.u  [12..16) FLOAT1 u32 normalized UV.v
    """
    chans = g["channels"]
    types = [c >> 16 for c in chans]
    stride, vb, vcnt = g["stride"], g["vb"], g["vcnt"]
    (mnx, mny, mnz), (mxx, mxy, mxz) = bbox
    size = (mxx - mnx, mxy - mny, mxz - mnz)
    mn = (mnx, mny, mnz)

    unpack_u32 = struct.Struct("<I").unpack_from
    unpack_u16 = struct.Struct("<H").unpack_from
    unpack_s16 = struct.Struct("<h").unpack_from
    layout = None
    if types == [T_SHORT2, T_FLOAT1, T_FLOAT1, T_SHORT4N, T_FLOAT1] and stride == 24:
        layout = "A"
    elif types == [T_SHORT4N, T_FLOAT1, T_FLOAT1] and stride == 16:
        layout = "B"
    else:
        raise RuntimeError(f"gvd[{g['idx']}]: unsupported vDecl {[hex(c) for c in chans]}")

    pos = []
    uv = []
    for v in range(vcnt):
        base = v * stride
        if layout == "A":
            px = unpack_u32(vb, base + 4)[0] / 4294967295.0
            py = (unpack_s16(vb, base + 8)[0] + 32767) / 65534.0
            pz = unpack_u32(vb, base + 20)[0] / 4294967295.0
            u0 = unpack_s16(vb, base + 12)[0] / 32767.0
            v0 = unpack_s16(vb, base + 14)[0] / 32767.0
        else:
            px = (unpack_s16(vb, base + 0)[0] + 32767) / 65534.0
            py = (unpack_s16(vb, base + 2)[0] + 32767) / 65534.0
            pz = (unpack_s16(vb, base + 4)[0] + 32767) / 65534.0
            u0 = unpack_u32(vb, base + 8)[0] / 4294967295.0
            v0 = unpack_u32(vb, base + 12)[0] / 4294967295.0
        pos.append((mn[0] + px * size[0], mn[1] + py * size[1], mn[2] + pz * size[2]))
        uv.append((u0, 1.0 - v0))
    return {"pos": pos, "uv": uv}


def decode_indices(g: dict) -> list[int]:
    count = g["idxsize"] // 2
    if g["ipacked"]:
        return decode_packed_ib(g["ib"], count)
    return list(struct.unpack_from(f"<{count}H", g["ib"]))


# --------------------------------------------------------------------------
# GeomNodeTree (skeleton) parsing - DClass 0x56F81B6D
# --------------------------------------------------------------------------

def parse_skeleton(sk: bytes) -> list[dict]:
    """Parse *_skeleton (GeomNodeTree): u32 szFlags, u32 nodeCnt, nodes x 160B,
    name blob. Node: f32x16 tm, f32x16 wtm (row-major, v*M convention,
    translation in [12..15]), i32 refOfs, i32 refCnt, 8B, i32 parent, 4B,
    i32 nameOfs (absolute = nameOfs+8 from file start)."""
    node_cnt, = struct.unpack_from("<I", sk, 4)
    nodes = []
    for i in range(node_cnt):
        o = 8 + i * 160
        tm = struct.unpack_from("<16f", sk, o)
        wtm = struct.unpack_from("<16f", sk, o + 64)
        parent, = struct.unpack_from("<i", sk, o + 140)
        name_ofs, = struct.unpack_from("<i", sk, o + 152)
        end = sk.index(b"\0", name_ofs + 8)
        name = sk[name_ofs + 8:end].decode("utf-8", "replace")
        nodes.append({"idx": i, "name": name, "tm": tm, "wtm": wtm, "parent": parent})
    return nodes


def load_skeleton_from_grp(grp_data: bytes, entries: list[dict]) -> list[dict] | None:
    """Pick the main (undamaged) *_skeleton entry of a grp package."""
    for e in entries:
        n = e["name"]
        if n.endswith("_skeleton") and not n.endswith(("_dm_skeleton", "_dmg_skeleton", "_xray_skeleton")):
            payload = grp_data[e["offset"]:e["offset"] + e["size"]]
            try:
                return parse_skeleton(payload)
            except Exception:  # noqa: BLE001 - skeleton optional
                return None
    return None


def _invert_rigid(rot, trans):
    """Invert a rigid transform (3x3 rotation, translation). Returns 3x4 rows."""
    inv = [[rot[0][0], rot[1][0], rot[2][0]],
           [rot[0][1], rot[1][1], rot[2][1]],
           [rot[0][2], rot[1][2], rot[2][2]]]  # transpose = inverse for rotations
    t = [-sum(inv[i][k] * trans[k] for k in range(3)) for i in range(3)]
    return [inv[0] + [t[0]], inv[1] + [t[1]], inv[2] + [t[2]]]


def to_nt(p: tuple[float, float, float]) -> tuple[float, float, float]:
    """WT (x=bow axis, y=up, z=width) -> NT (+Z bow, +Y up, +X starboard)."""
    return (-p[2], p[1], p[0])


# --------------------------------------------------------------------------
# glTF 2.0 writer (minimal GLB)
# --------------------------------------------------------------------------

def _pad4(n: int) -> int:
    return (4 - n % 4) % 4


class GlbBuilder:
    def __init__(self):
        self.bin = bytearray()
        self.views: list[dict] = []

    def add_view(self, data: bytes) -> int:
        off = len(self.bin)
        self.bin += data
        self.bin += b"\0" * _pad4(len(self.bin))
        self.views.append({"buffer": 0, "byteOffset": off, "byteLength": len(data)})
        return len(self.views) - 1


def _compute_normals(pos: list[tuple], indices: list[int]) -> bytes:
    acc = [0.0] * (3 * len(pos))
    for f in range(0, len(indices) - 2, 3):
        ia, ib_, ic = indices[f], indices[f + 1], indices[f + 2]
        ax, ay, az = pos[ia]
        bx, by, bz = pos[ib_]
        cx, cy, cz = pos[ic]
        ux, uy, uz = bx - ax, by - ay, bz - az
        vx, vy, vz = cx - ax, cy - ay, cz - az
        nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        for ii in (ia, ib_, ic):
            acc[3 * ii] += nx
            acc[3 * ii + 1] += ny
            acc[3 * ii + 2] += nz
    out = bytearray()
    for v in range(len(pos)):
        nx, ny, nz = acc[3 * v], acc[3 * v + 1], acc[3 * v + 2]
        l = math_sqrt(nx * nx + ny * ny + nz * nz)
        if l > 1e-12:
            nx, ny, nz = nx / l, ny / l, nz / l
        else:
            nx, ny, nz = 0.0, 1.0, 0.0
        out += struct.pack("<3f", nx, ny, nz)
    return bytes(out)


try:
    from math import sqrt as math_sqrt
except ImportError:  # pragma: no cover
    math_sqrt = lambda x: x ** 0.5  # noqa: E731


def build_glb(node_prims: list[tuple[str, list[dict]]],
              materials: list[dict],
              hierarchy: list[dict] | None = None) -> bytes:
    """node_prims: [(node_name, [{pos, uv, indices, mat, wtm?}])] for baked,
    scene-root nodes. hierarchy: extra structured nodes
    [{name, prims, translation(nt), rotation_quat(nt), children: [same]}] whose
    meshes keep local vertices and whose transforms place them in the scene
    (turret nodes with gun child nodes). pos arrays are WT local; all output
    geometry is converted to NT axes."""
    b = GlbBuilder()
    accessors, meshes, nodes = [], [], []

    def bake(p: tuple[float, float, float], wtm, pivot=None) -> tuple[float, float, float]:
        if pivot is not None:
            # Rotatable node: re-anchor around its barbette (XZ); Y stays model-space.
            p = (p[0] - pivot[0], p[1], p[2] - pivot[1])
        if wtm is None:
            return to_nt(p)
        x, y, z = p
        return to_nt((
            x * wtm[0] + y * wtm[4] + z * wtm[8] + wtm[12],
            x * wtm[1] + y * wtm[5] + z * wtm[9] + wtm[13],
            x * wtm[2] + y * wtm[6] + z * wtm[10] + wtm[14],
        ))

    def add_prims(prims: list[dict]) -> list[int]:
        out = []
        for prim in prims:
            used = sorted(set(prim["indices"]))
            remap = {old: new for new, old in enumerate(used)}
            wtm = prim.get("wtm")
            pv = prim.get("pivot_wt")
            npos = [bake(prim["pos"][i], wtm, pv) for i in used]
            nuv = [prim["uv"][i] for i in used] if prim.get("uv") else None
            nidx = [remap[i] for i in prim["indices"]]
            pv = b.add_view(b"".join(struct.pack("<3f", *p) for p in npos))
            pacc = len(accessors)
            accessors.append({"bufferView": pv, "componentType": 5126, "count": len(npos),
                              "type": "VEC3",
                              "min": [min(p[a] for p in npos) for a in range(3)],
                              "max": [max(p[a] for p in npos) for a in range(3)]})
            nv = b.add_view(_compute_normals(npos, nidx))
            nacc = len(accessors)
            accessors.append({"bufferView": nv, "componentType": 5126, "count": len(npos),
                              "type": "VEC3"})
            iv = b.add_view(b"".join(struct.pack("<I", i) for i in nidx))
            iacc = len(accessors)
            accessors.append({"bufferView": iv, "componentType": 5125,
                              "count": len(nidx), "type": "SCALAR"})
            gp = {"attributes": {"POSITION": pacc, "NORMAL": nacc},
                  "indices": iacc, "material": prim["mat"]}
            if nuv:
                uvv = b.add_view(b"".join(struct.pack("<2f", *uv) for uv in nuv))
                uacc = len(accessors)
                accessors.append({"bufferView": uvv, "componentType": 5126,
                                  "count": len(nuv), "type": "VEC2"})
                gp["attributes"]["TEXCOORD_0"] = uacc
            out.append(gp)
        return out

    def add_node(name: str, prims: list[dict], translation=None, rotation=None) -> int:
        gltf_prims = add_prims(prims)
        meshes.append({"primitives": gltf_prims, "name": name})
        ni = len(nodes)
        node: dict = {"mesh": len(meshes) - 1, "name": name}
        if translation:
            node["translation"] = [round(v, 5) for v in translation]
        if rotation:
            node["rotation"] = [round(v, 6) for v in rotation]
        nodes.append(node)
        return ni

    def add_hierarchy(hn: dict) -> int:
        ni = add_node(hn["name"], hn["prims"], hn.get("translation"), hn.get("rotation"))
        for ch in hn.get("children", []):
            ci = add_hierarchy(ch)
            nodes[ni].setdefault("children", []).append(ci)
        return ni

    for name, prims in node_prims:
        if prims:
            add_node(name, prims)
    for hn in hierarchy or []:
        add_hierarchy(hn)

    gltf = {
        "asset": {"version": "2.0", "generator": "NavyThunder convert_bim2_gltf"},
        "scene": 0, "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes, "meshes": meshes, "materials": materials,
        "buffers": [{"byteLength": len(b.bin)}],
        "bufferViews": b.views, "accessors": accessors,
    }
    js = json.dumps(gltf, separators=(",", ":")).encode()
    js += b" " * _pad4(len(js))
    blen = len(b.bin)
    total = 12 + 8 + len(js) + 8 + blen
    out = bytearray()
    out += struct.pack("<4sII", b"glTF", 2, total)
    out += struct.pack("<I4s", len(js), b"JSON")
    out += js
    out += struct.pack("<I4s", blen, b"BIN\0")
    out += b.bin
    return bytes(out)


import re

_TURRET_RE = re.compile(r"^(main_caliber_turret|turret)_\d+$")
_MC_TURRET_RE = re.compile(r"^main_caliber_turret_\d+$")
_MC_GUN_RE = re.compile(r"^main_caliber_gun_\d+$")


def _quat_from_mat(m: tuple[tuple, tuple, tuple]) -> tuple[float, float, float, float]:
    """Rotation matrix (3x3 tuples) -> quaternion (x, y, z, w)."""
    tr = m[0][0] + m[1][1] + m[2][2]
    if tr > 0:
        s = math_sqrt(tr + 1.0) * 2
        w = 0.25 * s
        x = (m[2][1] - m[1][2]) / s
        y = (m[0][2] - m[2][0]) / s
        z = (m[1][0] - m[0][1]) / s
    elif m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = math_sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2
        w = (m[2][1] - m[1][2]) / s
        x = 0.25 * s
        y = (m[0][1] + m[1][0]) / s
        z = (m[0][2] + m[2][0]) / s
    elif m[1][1] > m[2][2]:
        s = math_sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2
        w = (m[0][2] - m[2][0]) / s
        x = (m[0][1] + m[1][0]) / s
        y = 0.25 * s
        z = (m[1][2] + m[2][1]) / s
    else:
        s = math_sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2
        w = (m[1][0] - m[0][1]) / s
        x = (m[0][2] + m[2][0]) / s
        y = (m[1][2] + m[2][1]) / s
        z = 0.25 * s
    return (x, y, z, w)


def _mat_nt(r00, r01, r02, r10, r11, r12, r20, r21, r22):
    """WT->NT axis permutation applied to a rotation: R_NT = P * R * P^T with
    P the (x,y,z)->(-z,y,x) permutation (orthogonal, P^T = P)."""
    # P rows: nt_x = -wt_z, nt_y = wt_y, nt_z = wt_x
    nt = [[0.0] * 3 for _ in range(3)]
    R = [[r00, r01, r02], [r10, r11, r12], [r20, r21, r22]]
    P = [[0, 0, -1], [0, 1, 0], [1, 0, 0]]
    for i in range(3):
        for j in range(3):
            acc = 0.0
            for k in range(3):
                for m in range(3):
                    acc += P[i][k] * R[k][m] * P[j][m]
            nt[i][j] = acc
    return nt


def gray_material(idx: int) -> dict:
    """Placeholder PBR material; texture pipeline (P03-2) replaces later."""
    shade = 0.35 + 0.5 * ((idx * 2654435761) % 1000) / 1000.0
    return {"name": f"mat{idx}", "pbrMetallicRoughness":
            {"baseColorFactor": [shade, shade, shade * 1.03, 1.0], "metallicFactor": 0.1,
             "roughnessFactor": 0.8}}


# --------------------------------------------------------------------------
# ship conversion
# --------------------------------------------------------------------------

def _looks_like_bim2(payload: bytes) -> bool:
    """Header signature of the v7 dynmodel dump (measured on Bismarck AND
    Fletcher: the 'BIM2' string at offset 60 in Bismarck is coincidental
    compressed bytes, NOT a magic)."""
    if len(payload) < 28:
        return False
    res_sz, neg1a, neg1b, _vfc, mvhdr = struct.unpack_from("<IiiII", payload, 0)
    return (neg1a == -1 and neg1b == -1 and (mvhdr >> 30) == 3
            and 0 < (mvhdr & 0x3FFFFFFF) < 1 << 24 and 0 < res_sz < 1 << 20)


def convert_grp(grp_path: Path, out_dir: Path, lods: list[int] | None = None) -> dict:
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
        raise ValueError(f"no BIM2 dynmodel entry in {grp_path.name}")
    entry, payload = main

    parsed = parse_bim2(payload)
    skeleton = load_skeleton_from_grp(data, entries)
    skeleton_by_name = {nd["name"]: nd for nd in skeleton} if skeleton else {}
    bbox = parsed["bbox"]
    idx_cache: dict[int, list[int]] = {}
    vcache: dict[int, dict] = {}
    report = {
        "source": str(grp_path), "entry": entry["name"],
        "bbox_wt": [list(bbox[0]), list(bbox[1])],
        "bbox_nt": [[-bbox[1][2], bbox[0][1], bbox[0][0]], [-bbox[0][2], bbox[1][1], bbox[1][0]]],
        "nodes": len(parsed["names"]),
        "skeleton_nodes": len(skeleton) if skeleton else 0,
        "lods": [], "warnings": [],
    }
    out_dir.mkdir(parents=True, exist_ok=True)

    for li, lod in enumerate(parsed["lods"]):
        if lods is not None and li not in lods:
            continue
        node_prims: list[tuple[str, list[dict]]] = []
        tris = 0
        max_mat = 0
        # per-ship turret naming: ships with explicit main_caliber_* nodes use
        # them; otherwise bare turret_NN (Bismarck/Kongo era models). Iowa-type
        # hulls bake the turret body into the hull and expose main_caliber_gun_*
        # as the rotatable nodes instead.
        lod_names = [r["name"] for r in lod["rigids"]]
        if any(_MC_TURRET_RE.match(n) for n in lod_names):
            turret_re, gun_re = _MC_TURRET_RE, _MC_GUN_RE
        elif any(n.startswith("main_caliber_") for n in lod_names):
            turret_re, gun_re = None, _MC_GUN_RE  # turret body baked into hull
        else:
            turret_re, gun_re = _TURRET_RE, None
        turret_rigids: dict[str, dict] = {}
        gun_rigids: list[dict] = []
        for r in lod["rigids"]:
            if turret_re and turret_re.match(r["name"]):
                turret_rigids[r["name"]] = r
            elif gun_re and gun_re.match(r["name"]):
                gun_rigids.append(r)

        def collect(r: dict, pivot: tuple | None = None) -> list[dict]:
            """Decode + filter elems for one rigid.

            Shared-buffer vertices are MODEL-SPACE for every rigid (verified on
            Bismarck AND North Carolina: identity-wtm parts render correctly,
            transformed parts would fly if baked). pivot=(px, pz) re-anchors the
            emitted primitives for a rotatable node: the glb writer subtracts the
            pivot from the vertices and the node translation puts it back, so the
            node can yaw around its own barbette."""
            per_gvd: dict[int, dict] = {}
            for el in r["elems"]:
                gi = el["gvd"]
                if gi not in vcache:
                    g = parsed["gvd"][gi]
                    try:
                        vd = decode_vertices(g, bbox)
                        # Shared vertex buffers come in two semantics (verified on
                        # Bismarck + North Carolina): big buffers hold MODEL-SPACE
                        # vertices (bbox ~ hull), small ones hold per-part LOCAL
                        # vertices that need their rigid's wtm. Discriminate by span.
                        if vd["pos"]:
                            xs = [q[0] for q in vd["pos"]]
                            ys = [q[1] for q in vd["pos"]]
                            zs = [q[2] for q in vd["pos"]]
                            span = max(max(xs) - min(xs), max(zs) - min(zs))
                            hull_len = abs(bbox[1][0] - bbox[0][0])
                            vd["model_space"] = span > hull_len * 0.6
                            print(f"  gvd[{gi}]: {len(vd['pos'])} verts span={span:.1f}m "
                                  f"hull={hull_len:.1f}m -> {'MODEL' if vd['model_space'] else 'LOCAL'}")
                        else:
                            vd["model_space"] = True
                        vcache[gi] = vd
                    except Exception as exc:  # noqa: BLE001 - per-ship robustness
                        vcache[gi] = {"pos": [], "uv": None, "model_space": True}
                        report["warnings"].append(f"lod{li} gvd{gi} vertices: {exc}")
                if gi not in idx_cache:
                    try:
                        idx_cache[gi] = decode_indices(parsed["gvd"][gi])
                    except Exception as exc:  # noqa: BLE001
                        idx_cache[gi] = []
                        report["warnings"].append(f"lod{li} gvd{gi} indices: {exc}")
                full = idx_cache[gi]
                if not full:
                    continue
                start, cnt = el["si"], el["numf"] * 3
                if start + cnt > len(full):
                    report["warnings"].append(
                        f"lod{li} rigid {r['name']} elem range {start}+{cnt} > {len(full)}")
                    continue
                vcnt = parsed["gvd"][gi]["vcnt"]
                tri = full[start:start + cnt]
                base = el["base_vertex"]
                # Drop whole triangles whose final index escapes the vertex buffer
                # (strip-tail junk or a bad baseVertex); clamping would weld them to
                # unrelated hull vertices and stretch triangles across the ship.
                ok = []
                bad = 0
                for j in range(0, len(tri) - 2, 3):
                    a = base + tri[j]
                    b = base + tri[j + 1]
                    c = base + tri[j + 2]
                    if 0 <= a < vcnt and 0 <= b < vcnt and 0 <= c < vcnt:
                        ok.extend((a, b, c))
                    else:
                        bad += 3
                if bad > cnt * 0.02:
                    report["warnings"].append(
                        f"lod{li} rigid {r['name']} elem dropped: {bad}/{cnt} indices out of range")
                    continue
                blk = per_gvd.setdefault(gi, {"items": []})
                blk["items"].append((0, ok, el["mat"]))
            prims = []
            for gi, blk in per_gvd.items():
                vdata = vcache[gi]
                if not vdata["pos"]:
                    continue
                for _base_v, tri, mat_idx in blk["items"]:
                    # Final indices are already resolved (baseVertex applied + culled).
                    # Re-anchor: the glb writer shifts verts by -pivot before the NT
                    # conversion and the node carries +pivot as its translation, so
                    # yawing the node spins the part around its own barbette.
                    entry = {"pos": vdata["pos"], "uv": vdata["uv"],
                             "indices": tri, "mat": mat_idx, "wtm": None,
                             "pivot_wt": None}
                    if pivot is not None:
                        # Per-triangle gate: rigids are material batches whose slabs can
                        # span the whole beam; only triangles whose centroid sits within
                        # 15 m of the barbette pivot belong to the rotating turret.
                        rotating = []
                        static = []
                        for j in range(0, len(tri) - 2, 3):
                            a, b, c = tri[j], tri[j + 1], tri[j + 2]
                            # ALL three corners near the barbette: big deck triangles
                            # straddle the pivot with a near-centroid and would sweep
                            # the whole beam when yawed.
                            near = all(abs(vdata["pos"][v][0] - pivot[0]) <= 12
                                       and abs(vdata["pos"][v][2] - pivot[1]) <= 12
                                       for v in (a, b, c))
                            (rotating if near else static).extend((a, b, c))
                        if rotating:
                            entry["pivot_wt"] = pivot
                            entry["indices"] = rotating
                        if static:
                            static_entry = dict(entry)
                            static_entry["indices"] = static
                            static_entry["pivot_wt"] = None
                            prims.append(static_entry)
                        if not rotating:
                            entry = None
                    if entry is not None and entry.get("indices"):
                        prims.append(entry)
            return prims
            return prims

        def nt_world(nd: dict) -> tuple[list[float], tuple]:
            """Node world transform (from skeleton wtm) converted to NT axes:
            returns (translation, 3x3 rotation)."""
            w = nd["wtm"]
            t = to_nt((w[12], w[13], w[14]))
            rot = _mat_nt(w[0], w[1], w[2], w[4], w[5], w[6], w[8], w[9], w[10])
            return list(t), rot

        hierarchy: list[dict] = []
        emitted: set[str] = set()
        # PHASE_03 decision: rigids are material batches and may span the hull,
        # so pivot-local vertices would sweep unrelated geometry when a turret
        # yaws. Emit every rigid with model-space vertices and identity node
        # transforms (always renders correctly); turret anchor positions live in
        # nodeMap.json and Phase 04 owns real turret articulation.
        turret_nodes: dict[str, dict] = {}
        turret_pivots: dict[str, tuple[float, float]] = {}
        for tname, r in turret_rigids.items():
            sk_node = skeleton_by_name.get(tname.lstrip("@"))
            if sk_node is None:
                continue
            pivot = (sk_node["wtm"][12], sk_node["wtm"][14])  # XZ, WT axes
            prims = collect(r, pivot)
            trans = to_nt((pivot[0], 0.0, pivot[1]))
            turret_nodes[tname] = {"name": tname, "prims": prims,
                                   "translation": list(trans),
                                   "rotation": None,
                                   "children": [], "_rot": None,
                                   "_trans": pivot}
            hierarchy.append(turret_nodes[tname])
            emitted.add(tname)
        for r in gun_rigids:
            sk_node = skeleton_by_name.get(r["name"].lstrip("@"))
            if sk_node is None:
                continue  # baked with the rest
            emitted.add(r["name"])
            gun_sk = skeleton_by_name.get(r["name"].lstrip("@"))
            gun_pivot = (gun_sk["wtm"][12], gun_sk["wtm"][14]) if gun_sk else None
            prims = collect(r, gun_pivot)
            if not turret_nodes:
                # Iowa-type: gun nodes rotate themselves around their own barbette.
                trans = to_nt((gun_pivot[0], 0.0, gun_pivot[1])) if gun_pivot else None
                hierarchy.append({"name": r["name"], "prims": prims,
                                  "translation": list(trans) if trans else None,
                                  "rotation": None, "children": []})
                continue
            # Nearest turret by skeleton position (the gun feeds that barbette).
            best = min(turret_nodes.values(),
                       key=lambda t: sum((a - b) ** 2 for a, b in zip(t["_trans"], gun_pivot or (0.0, 0.0))))
            best["children"].append({"name": r["name"], "prims": prims,
                                     "translation": None, "rotation": None,
                                     "children": []})

        for r in lod["rigids"]:
            if r["name"] in emitted:
                continue  # emitted in the turret/gun hierarchy
            prims = collect(r)
            max_mat = max([max_mat] + [p["mat"] for p in prims])
            tris += sum(len(p["indices"]) // 3 for p in prims)
            if prims:
                node_prims.append((r["name"], prims))
        h_tris = 0

        def count_h(hn: dict) -> int:
            n = sum(len(p["indices"]) // 3 for p in hn["prims"])
            for ch in hn["children"]:
                n += count_h(ch)
            return n

        h_tris = sum(count_h(hn) for hn in hierarchy)
        tris += h_tris
        for tn in turret_nodes.values():
            for ch in tn["children"]:
                max_mat = max([max_mat] + [p["mat"] for p in ch["prims"]] + [p["mat"] for p in tn["prims"]])
            max_mat = max([max_mat] + [p["mat"] for p in tn["prims"]])
        if node_prims or hierarchy:
            mats = [gray_material(i) for i in range(max_mat + 1)]
            glb = build_glb(node_prims, mats, hierarchy)
            fname = f"model_lod{li}.glb"
            (out_dir / fname).write_bytes(glb)
            report["lods"].append({"lod": li, "range_m": lod["range"], "file": fname,
                                   "rigids": len(lod["rigids"]), "tris": tris, "bytes": len(glb)})
        else:
            report["lods"].append({"lod": li, "range_m": lod["range"], "file": None, "tris": 0})

    # nodeMap: skeleton nodes in model space -> NT attach points for the frontend
    # (turret mounts, fire/smoke anchors). Coordinates converted to NT axes.
    if skeleton:
        node_map = {}
        for nd in skeleton:
            w = nd["wtm"]
            tx, ty, tz = to_nt((w[12], w[13], w[14]))
            entry_nm = {"parent": skeleton[nd["parent"]]["name"] if 0 <= nd["parent"] < len(skeleton) else None,
                        "pos": [round(tx, 3), round(ty, 3), round(tz, 3)]}
            node_map[nd["name"] or f"node{nd['idx']}"] = entry_nm
        (out_dir / "nodeMap.json").write_text(json.dumps(node_map, indent=1), encoding="utf-8")
        report["node_map"] = "nodeMap.json"

    (out_dir / "convert_report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    return report


def main() -> int:
    ap = argparse.ArgumentParser(description="convert WT ship .grp (BIM2 dynmodel) to glTF/GLB")
    ap.add_argument("grp", type=Path, help=".grp file")
    ap.add_argument("--out", type=Path, default=None, help="output dir (default assets/models/<ship_id>)")
    ap.add_argument("--lod", type=int, action="append", help="only convert these LOD indices")
    args = ap.parse_args()
    stem = args.grp.stem
    for pre, rep in (("usa_", "uss_"), ("jap_", "ijn_"), ("germ_", "rms_"), ("uk_", "rn_")):
        if stem.startswith(pre):
            stem = rep + stem[len(pre):]
    out = args.out or Path("assets/models") / stem
    report = convert_grp(args.grp, out, args.lod)
    print(json.dumps(report, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
