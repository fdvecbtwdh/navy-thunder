#!/usr/bin/env python3
"""Software-rasterized preview of a GLB mesh (3 orthographic views).

Phase 03 geometry sanity check: renders side/top/bow views with a flat z-buffer
so hull silhouette, superstructure and turret placement can be verified without
opening a 3D editor. Same approach as tools/generate_hull_obj.py preview.png.
"""
import json
import struct
import sys
from pathlib import Path


def read_glb(path: Path):
    data = path.read_bytes()
    assert data[:4] == b"glTF", "not a GLB"
    js_len, js_type = struct.unpack_from("<I4s", data, 12)
    gltf = json.loads(data[20:20 + js_len])
    bin_off = 20 + js_len
    bin_len, bin_type = struct.unpack_from("<I4s", data, bin_off)
    blob = data[bin_off + 8:bin_off + 8 + bin_len]
    meshes = []
    for mesh in gltf["meshes"]:
        tris = []
        for prim in mesh["primitives"]:
            pa = gltf["accessors"][prim["attributes"]["POSITION"]]
            view = gltf["bufferViews"][pa["bufferView"]]
            off = view.get("byteOffset", 0)
            pos = struct.unpack_from(f"<{pa['count']*3}f", blob, off)
            ia = gltf["accessors"][prim["indices"]]
            iv = gltf["bufferViews"][ia["bufferView"]]
            ioff = iv.get("byteOffset", 0)
            idx = struct.unpack_from(f"<{ia['count']}I", blob, ioff)
            tris.append((pos, idx))
        meshes.append((mesh.get("name", "?"), tris))
    return meshes


def render(tris, view, size=900):
    import math
    # view: 'side' (x right=z_nt? we render NT axes: z=bow, y=up, x=starboard)
    def proj(p):
        if view == "side":   # looking from starboard: bow to the right
            return (p[2], p[1]), p[0]
        if view == "top":    # looking down: bow up
            return (p[0], p[2]), p[1]
        return (p[0], p[1]), p[2]  # bow view
    pts2, depths = [], []
    for pos, idx in tris:
        for i in idx:
            xy, d = proj(pos[i * 3:i * 3 + 3])
            pts2.append(xy)
            depths.append(d)
    if not pts2:
        return None
    xs = [p[0] for p in pts2]
    ys = [p[1] for p in pts2]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    span = max(x1 - x0, y1 - y0) * 1.05
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    sc = size / span

    # z-buffer raster with face normal shading
    zbuf = [1e30] * (size * size)
    img = bytearray(b"\xff" * (size * size * 3))
    light = {"side": (0.4, 0.3, 0.9), "top": (0.2, 0.9, 0.3), "bow": (0.9, 0.2, 0.3)}[view]
    for pos, idx in tris:
        for f in range(0, len(idx) - 2, 3):
            ia, ib_, ic = idx[f], idx[f + 1], idx[f + 2]
            tri = []
            for ii in (ia, ib_, ic):
                xy, d = proj(pos[ii * 3:ii * 3 + 3])
                tri.append(((xy[0] - cx) * sc + size / 2, (y1 - xy[1]) * sc + size / 2 - (y1 - y0) * sc / 2 * 0, d))
            # actual y flip
            tri = []
            for ii in (ia, ib_, ic):
                xy, d = proj(pos[ii * 3:ii * 3 + 3])
                tri.append(((xy[0] - cx) * sc + size / 2, size / 2 - (xy[1] - cy) * sc, d))
            (ax, ay, ad), (bx, by, bd), (cx2, cy2, cd) = tri
            n = ((by - ay) * (cx2 - ax) - (bx - ax) * (cy2 - ay))
            if abs(n) < 1e-9:
                continue
            shade = 0.55 + 0.45 * abs(n) / (abs(n) + ((bx-ax)**2 + (by-ay)**2 + (cx2-ax)**2 + (cy2-ay)**2) ** 0.5)
            minx = max(0, int(min(ax, bx, cx2)))
            maxx = min(size - 1, int(max(ax, bx, cx2)))
            miny = max(0, int(min(ay, by, cy2)))
            maxy = min(size - 1, int(max(ay, by, cy2)))
            den = (by - cy2) * (ax - cx2) + (cx2 - bx) * (ay - cy2)
            if abs(den) < 1e-9:
                continue
            for py in range(miny, maxy + 1):
                for px in range(minx, maxx + 1):
                    w0 = ((by - cy2) * (px - cx2) + (cx2 - bx) * (py - cy2)) / den
                    w1 = ((cy2 - ay) * (px - cx2) + (ax - cx2) * (py - cy2)) / den
                    w2 = 1 - w0 - w1
                    if w0 < -1e-6 or w1 < -1e-6 or w2 < -1e-6:
                        continue
                    d = w0 * ad + w1 * bd + w2 * cd
                    pi = py * size + px
                    if d < zbuf[pi]:
                        zbuf[pi] = d
                        c = int(255 * shade)
                        img[3 * pi] = int(c * 0.75)
                        img[3 * pi + 1] = c
                        img[3 * pi + 2] = int(c * 0.9)
    return bytes(img), size


def write_ppm(path: Path, img: bytes, size: int):
    with open(path, "wb") as f:
        f.write(b"P6\n%d %d\n255\n" % (size, size))
        f.write(img)


def main():
    glb = Path(sys.argv[1])
    out_dir = glb.parent
    meshes = read_glb(glb)
    all_tris = [t for _, ts in meshes for t in ts]
    total = sum(len(idx) // 3 for _, idx in all_tris)
    print(f"{glb.name}: {len(meshes)} meshes, {total} triangles")
    for view in ("side", "top", "bow"):
        r = render(all_tris, view)
        if r:
            img, size = r
            out = out_dir / f"preview_{view}.ppm"
            write_ppm(out, img, size)
            print(f"  {view}: {out}")


if __name__ == "__main__":
    main()
