# Generate Cube* opening as Unity local deltas from Blender frame 0 (FBX rest stays as imported).
import json
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DUMP = os.path.join(ROOT, "UnityBake", "Assets", "MissilePack", "Textures", "Crosswim", "crosswim_cube_frames.json")
DUMP_FALLBACK = os.path.join(ROOT, "UnityBake", "Assets", "MissilePack", "Textures", "Crosswim", "crosswim_dump.json")
OUT = os.path.join(ROOT, "MK65Crosswim", "Runtime", "CrosswimCubeKeys.cs")


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def wxyz_to_xyzw(q):
    w, x, y, z = q
    return (x, y, z, w)


def stabilize(rots):
    out = []
    prev = None
    for r in rots:
        if prev is not None and (prev[0] * r[0] + prev[1] * r[1] + prev[2] * r[2] + prev[3] * r[3]) < 0.0:
            r = (-r[0], -r[1], -r[2], -r[3])
        out.append(r)
        prev = r
    return out


def fmt(v, n):
    s = f"{v:.{n}f}"
    if s.startswith("-0.") and abs(float(s)) == 0.0:
        s = "0." + s.split(".", 1)[1]
    return s + "f"


def pack_floats(vals, cols, nd):
    lines = []
    row = []
    for v in vals:
        row.append(fmt(v, nd))
        if len(row) >= cols:
            lines.append("            " + ", ".join(row) + ",")
            row = []
    if row:
        lines.append("            " + ", ".join(row) + ",")
    return "\n".join(lines)


def load_cube(path):
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    cube = data.get("cubeFrames") if isinstance(data.get("cubeFrames"), dict) else None
    if cube is None and isinstance(data.get("frames"), list) and data.get("names"):
        cube = data
    return cube


def blender_delta_to_unity(q0_wxyz, qt_wxyz):
    q0 = wxyz_to_xyzw(q0_wxyz)
    qt = wxyz_to_xyzw(qt_wxyz)
    inv0 = (-q0[0], -q0[1], -q0[2], q0[3])
    n = math.sqrt(inv0[0] ** 2 + inv0[1] ** 2 + inv0[2] ** 2 + inv0[3] ** 2) or 1.0
    inv0 = (inv0[0] / n, inv0[1] / n, inv0[2] / n, inv0[3] / n)
    d = qmul(inv0, qt)
    # Swizzle only (no extra −90X): FBX rest already has importer conversion.
    ux, uy, uz, uw = -d[0], -d[2], -d[1], d[3]
    ln = math.sqrt(ux * ux + uy * uy + uz * uz + uw * uw) or 1.0
    return (ux / ln, uy / ln, uz / ln, uw / ln)


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else (DUMP if os.path.isfile(DUMP) else DUMP_FALLBACK)
    dst = sys.argv[2] if len(sys.argv) > 2 else OUT
    cube = load_cube(src)
    if not cube:
        raise SystemExit("cubeFrames missing in " + src)
    names = cube["names"]
    frames = cube["frames"]
    fps = int(cube.get("fps") or 24)
    n_frames = len(frames)
    rot = []
    for name in names:
        q0 = frames[0][name]["quat"]
        fin = []
        for rec in frames:
            entry = rec.get(name)
            if entry is None:
                raise SystemExit(f"missing {name} at frame {rec.get('f')}")
            fin.append(blender_delta_to_unity(q0, entry["quat"]))
        for r in stabilize(fin):
            rot.extend(r)

    cs = f"""using UnityEngine;

namespace Crosswim.Runtime
{{
    /// <summary>
    /// Cube* opening deltas: inv(frame0)*frame t in Blender, swizzled to Unity local.
    /// Applied as fbxRestRot * delta. Frame 0 is identity — hangar is FBX bind.
    /// </summary>
    internal static class CrosswimCubeKeys
    {{
        internal const int FinCount = {len(names)};
        internal const int FrameCount = {n_frames};
        internal const float Fps = {fps}f;
        internal static readonly string[] Names = {{ {", ".join('"' + n + '"' for n in names)} }};

        private static readonly float[] Rx =
        {{
{pack_floats([rot[i] for i in range(0, len(rot), 4)], 8, 7)}
        }};

        private static readonly float[] Ry =
        {{
{pack_floats([rot[i] for i in range(1, len(rot), 4)], 8, 7)}
        }};

        private static readonly float[] Rz =
        {{
{pack_floats([rot[i] for i in range(2, len(rot), 4)], 8, 7)}
        }};

        private static readonly float[] Rw =
        {{
{pack_floats([rot[i] for i in range(3, len(rot), 4)], 8, 7)}
        }};

        internal static int Index(int fin, int frame) => fin * FrameCount + frame;

        internal static Quaternion Delta(int fin, float frame)
        {{
            if (fin < 0 || fin >= FinCount)
                return Quaternion.identity;

            float f = frame;
            if (f < 0f)
                f = 0f;
            float last = FrameCount - 1;
            if (f > last)
                f = last;

            int i0 = (int)f;
            int i1 = i0 + 1;
            if (i1 >= FrameCount)
                i1 = FrameCount - 1;
            float a = f - i0;
            return Quaternion.Slerp(At(Index(fin, i0)), At(Index(fin, i1)), a);
        }}

        private static Quaternion At(int i) => new Quaternion(Rx[i], Ry[i], Rz[i], Rw[i]);
    }}
}}
"""
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        f.write(cs)
    print("WROTE", dst, "fins", len(names), "frames", n_frames)


if __name__ == "__main__":
    main()
