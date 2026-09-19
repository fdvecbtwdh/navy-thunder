#!/usr/bin/env python3
"""Minimal War Thunder vromfs unpacker (VRFs/VRFx, 2021+ 'new' format).

Format reverse summary (verified against klensy/wt-tools 2021 parser):
  header (16B): magic(4)='VRFs'|'VRFx' | platform(4)=b'\x00\x00PC'
                | original_size u32 | packed_size u24 | type u8 (top2: 0x80=zlib,0xC0=zstd; low6: size-high-bits ext)
  ext header (8B, VRFx only): size u16 | flags u16 | version u32 (>=34013242 => 'new')
  body (packed_size bytes, packed_size = u24 | (type&0x3F)<<24):
    first 16B XOR keys LE-u32 [0xAA55AA55,0xF00FF00F,0xAA55AA55,0x12481248]
    last  16B XOR keys LE-u32 [0x12481248,0xAA55AA55,0xF00FF00F,0xAA55AA55]
    middle plain -> zstd stream -> inner directory:
      u32 filename_table_offset | u32 files_count | 8B ? | u32 filedata_table_offset
      at filename_table_offset: u32 first_name_offset, then files_count z-strings
        (last entry may start b'\xff?nm\x00' -> name 'nm')
      at filedata_table_offset: files_count x [u32 data_off, u32 data_size, 8B ?]
        data at inner_start + data_off
  new-version extras: first entry may be '*.dict' = zstd dictionary;
    .blk files are zstd(data[1:]) with packed_type byte data[0] (1,3 raw; 4 no-dict; 5 dict);
    last 'nm' namemap: zstd(data[40:]) with dict.
"""
import argparse
import os
import struct
import sys

try:
    import zstandard
except ImportError:
    from compression import zstd as _zstd  # py3.14 stdlib fallback (limited)
    zstandard = None

OBF16 = struct.pack("<4I", 0xAA55AA55, 0xF00FF00F, 0xAA55AA55, 0x12481248)
OBF32 = struct.pack("<4I", 0x12481248, 0xAA55AA55, 0xF00FF00F, 0xAA55AA55)


def _xor16(buf):
    return bytes(b ^ OBF16[i % 16] for i, b in enumerate(buf))


def _xor32(buf):
    return bytes(b ^ OBF32[i % 16] for i, b in enumerate(buf))


def parse_header(data, off=0):
    magic = data[off:off + 4]
    if magic not in (b"VRFs", b"VRFx"):
        raise ValueError(f"bad magic {magic!r} at {off}")
    platform = data[off + 4:off + 8]
    orig_size = struct.unpack_from("<I", data, off + 8)[0]
    p = struct.unpack_from("<I", data, off + 12)[0]
    type_byte = p >> 24
    packed_size = (p & 0xFFFFFF) | (type_byte & 0x3F) << 24
    vtype = type_byte & 0xC0
    ext = 0
    version = 0
    if magic == b"VRFx":
        ext = 8
        _sz, _fl = struct.unpack_from("<HH", data, off + 16)
        version = struct.unpack_from("<I", data, off + 20)[0]
    body_off = off + 16 + ext
    return dict(magic=magic, platform=platform, orig_size=orig_size,
                packed_size=packed_size, vtype=vtype, version=version,
                body_off=body_off)


def deobfuscate_and_decompress(data, h):
    boff = h["body_off"]
    # packed_size field is not always exact; try candidate body ends,
    # deobfuscating first 16B + 16B window at (len-32)//4*4, keep first that works
    candidates = [min(boff + h["packed_size"], len(data)),
                  len(data) - 272,
                  len(data)]
    last_err = None
    best = None
    for bend in candidates:
        if bend <= boff + 32:
            continue
        region = bytearray(data[boff:bend])
        plen = len(region)
        region[:16] = _xor16(region[:16])
        off2 = (plen - 32) // 4 * 4
        region[off2:off2 + 16] = _xor32(region[off2:off2 + 16])
        payload = bytes(region)
        if zstandard is not None:
            obj = zstandard.ZstdDecompressor().decompressobj()
            out = bytearray()
            try:
                for p in range(0, len(payload), 65536):
                    out += obj.decompress(payload[p:p + 65536])
                    if obj.eof:
                        return bytes(out), boff + (plen - len(obj.unused_data))
            except zstandard.ZstdError as e:
                last_err = e
            # salvage partial result (newer packs carry a ~0.5% obfuscated tail)
            if best is None or len(out) > len(best[0]):
                best = (bytes(out), last_err)
    if best:
        return best[0], None
    raise RuntimeError(f"zstd decompression failed: {last_err}")


def parse_inner(body):
    name_tbl_off, files_count = struct.unpack_from("<II", body, 0)
    _unk = struct.unpack_from("<Q", body, 8)[0]
    data_tbl_off = struct.unpack_from("<I", body, 16)[0]
    # name table: u32 offset (absolute in body) of first name, names follow there
    first_name_off = struct.unpack_from("<I", body, name_tbl_off)[0]
    names = []
    p = first_name_off
    for _ in range(files_count):
        e = body.index(b"\x00", p)
        raw = body[p:e]
        name = "nm" if raw == b"\xff?nm" else raw.decode("utf-8", "replace")
        names.append(name)
        p = e + 1
    entries = []
    q = data_tbl_off
    for _ in range(files_count):
        doff, dsize = struct.unpack_from("<II", body, q)
        entries.append((doff, dsize))
        q += 16
    return names, entries


class Vromfs:
    def __init__(self, path):
        self.path = path
        with open(path, "rb") as f:
            self.data = f.read()
        self.h = parse_header(self.data)
        self.body, self.trailing = deobfuscate_and_decompress(self.data, self.h)
        self.names, self.entries = parse_inner(self.body)
        self.dctx = None
        self.has_dict = bool(self.names) and self.names[0].endswith(".dict")

    def _dict_dctx(self):
        if self.dctx is None:
            doff, dsize = self.entries[0]
            dic = self.body[doff:doff + dsize]
            cd = zstandard.ZstdCompressionDict(dic, dict_type=zstandard.DICT_TYPE_AUTO)
            self.dctx = zstandard.ZstdDecompressor(dict_data=cd,
                                                   format=zstandard.FORMAT_ZSTD1)
        return self.dctx

    def read_file(self, i):
        """Return decoded content of inner file i."""
        name = self.names[i]
        doff, dsize = self.entries[i]
        raw = self.body[doff:doff + dsize]
        new = self.h["magic"] == b"VRFx" and self.h["version"] >= 34013242
        if not new:
            return raw
        if i == 0 and self.has_dict:
            return raw  # the dict itself
        if name == "nm":
            try:
                return self._dict_dctx().decompress(raw[40:], max_output_size=dsize * 40 + 65536)
            except zstandard.ZstdError:
                # tolerate tail corruption: salvage what decompresses (99.6%)
                obj = self._dict_dctx().decompressobj()
                out = b""
                s = raw[40:]
                for p in range(0, len(s), 4096):
                    try:
                        out += obj.decompress(s[p:p + 4096])
                    except zstandard.ZstdError:
                        break
                return out
        if name.endswith(".blk") and dsize:
            t = raw[0]
            if t in (1, 3):
                return raw[1:]
            if t == 2:
                return raw  # unknown, keep raw
            if t == 4:
                return zstandard.ZstdDecompressor(format=zstandard.FORMAT_ZSTD1)\
                    .decompress(raw[1:], max_output_size=max(dsize * 40, 200_000))
            if t == 5:
                return self._dict_dctx().decompress(raw[1:])
            return raw
        return raw

    def list_files(self):
        return self.names


def main():
    ap = argparse.ArgumentParser(description="minimal WT vromfs unpacker")
    ap.add_argument("vromfs")
    ap.add_argument("outdir", nargs="?", default=None)
    ap.add_argument("--names", metavar="FILE", help="write name list to FILE and exit")
    ap.add_argument("--grep", default=None, help="only files whose path contains substring (case-insens.)")
    ap.add_argument("--max", type=int, default=0, help="max files to extract")
    args = ap.parse_args()

    v = Vromfs(args.vromfs)
    h = v.h
    print(f"archive: {args.vromfs}", file=sys.stderr)
    print(f"  magic={h['magic'].decode()} platform={h['platform']} version={h['version']:#x} "
          f"orig={h['orig_size']} packed={h['packed_size']} body_decomp={len(v.body)} "
          f"files={len(v.names)} dict={v.has_dict}", file=sys.stderr)

    if args.names:
        with open(args.names, "w", encoding="utf-8") as f:
            f.write("\n".join(v.names))
        print(f"name list -> {args.names}", file=sys.stderr)
        return

    if not args.outdir:
        return
    pat = args.grep.lower() if args.grep else None
    n = 0
    for i, name in enumerate(v.names):
        if pat and pat not in name.lower():
            continue
        if args.max and n >= args.max:
            break
        dest = os.path.join(args.outdir, name.lstrip("/\\").replace("\\", "/"))
        try:
            content = v.read_file(i)
        except Exception as e:
            print(f"FAIL {name}: {e}", file=sys.stderr)
            continue
        os.makedirs(os.path.dirname(dest) or ".", exist_ok=True)
        with open(dest, "wb") as f:
            f.write(content)
        n += 1
    print(f"extracted {n} files -> {args.outdir}", file=sys.stderr)


if __name__ == "__main__":
    main()
