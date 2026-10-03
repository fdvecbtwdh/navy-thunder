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
            rigids.append({"node_id": node_id,
                           "name": names[node_id] if node_id < len(names) else f"node{node_id}",
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


def build_glb(node_prims: list[tuple[str, list[dict]]], materials: list[dict]) -> bytes:
    """node_prims: [(node_name, [{pos, uv, indices, mat, wtm?}])]; pos in WT local
    space; optional wtm (16 f32, Dagor row-major v*M) bakes the rigid's node
    transform before the NT axis conversion. UV flips V for glTF."""
    b = GlbBuilder()
    accessors, meshes, nodes = [], [], []

    def bake(p: tuple[float, float, float], wtm) -> tuple[float, float, float]:
        if wtm is None:
            return to_nt(p)
        x, y, z = p
        return to_nt((
            x * wtm[0] + y * wtm[4] + z * wtm[8] + wtm[12],
            x * wtm[1] + y * wtm[5] + z * wtm[9] + wtm[13],
            x * wtm[2] + y * wtm[6] + z * wtm[10] + wtm[14],
        ))

    for name, prims in node_prims:
        if not prims:
            continue
        gltf_prims = []
        for prim in prims:
            used = sorted(set(prim["indices"]))
            remap = {old: new for new, old in enumerate(used)}
            wtm = prim.get("wtm")
            npos = [bake(prim["pos"][i], wtm) for i in used]
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
            gltf_prims.append(gp)
        meshes.append({"primitives": gltf_prims, "name": name})
        nodes.append({"mesh": len(meshes) - 1, "name": name})
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


def gray_material(idx: int) -> dict:
    """Placeholder PBR material; texture pipeline (P03-2) replaces later."""
    shade = 0.35 + 0.5 * ((idx * 2654435761) % 1000) / 1000.0
    return {"name": f"mat{idx}", "pbrMetallicRoughness":
            {"baseColorFactor": [shade, shade, shade * 1.03, 1.0], "metallicFactor": 0.1,
             "roughnessFactor": 0.8}}


# --------------------------------------------------------------------------
# ship conversion
# --------------------------------------------------------------------------

def convert_grp(grp_path: Path, out_dir: Path, lods: list[int] | None = None) -> dict:
    data = grp_path.read_bytes()
    _names, entries = parse_grp(data)
    main = None
    for e in entries:
        if e["name"].endswith("_dmg") or e["name"].endswith("_xray"):
            continue
        payload = data[e["offset"]:e["offset"] + e["size"]]
        if len(payload) > 64 and payload[60:64] == b"BIM2":
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
        for r in lod["rigids"]:
            wtm = None
            if skeleton:
                # rigid.nodeId indexes the dynmodel name map; the node transform
                # lives in the GeomNodeTree, bridged by node NAME.
                sk_node = skeleton_by_name.get(r["name"].lstrip("@"))
                if sk_node is not None:
                    wtm = sk_node["wtm"]
            per_gvd: dict[int, dict] = {}
            for el in r["elems"]:
                gi = el["gvd"]
                if gi not in vcache:
                    g = parsed["gvd"][gi]
                    try:
                        vcache[gi] = decode_vertices(g, bbox)
                    except Exception as exc:  # noqa: BLE001 - per-ship robustness
                        vcache[gi] = {"pos": [], "uv": None}
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
                over = sum(1 for i in tri if el["base_vertex"] + i >= vcnt)
                if over > cnt * 0.02:  # broken elem (tiny fx meshes); drop whole elem
                    report["warnings"].append(
                        f"lod{li} rigid {r['name']} elem dropped: {over}/{cnt} indices out of range")
                    continue
                blk = per_gvd.setdefault(gi, {"items": []})
                blk["items"].append((el["base_vertex"],
                                     [min(i, vcnt - 1 - el["base_vertex"]) for i in tri],
                                     el["mat"]))
                max_mat = max(max_mat, el["mat"])
            prims = []
            for gi, blk in per_gvd.items():
                vdata = vcache[gi]
                if not vdata["pos"]:
                    continue
                for base_v, tri, mat_idx in blk["items"]:
                    sub = [base_v + i for i in tri]  # D3D baseVertex semantics
                    prims.append({"pos": vdata["pos"], "uv": vdata["uv"],
                                  "indices": sub, "mat": mat_idx, "wtm": wtm})
                    tris += len(tri) // 3
            if prims:
                node_prims.append((r["name"], prims))
        if node_prims:
            mats = [gray_material(i) for i in range(max_mat + 1)]
            glb = build_glb(node_prims, mats)
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
