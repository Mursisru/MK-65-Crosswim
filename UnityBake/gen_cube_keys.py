# Opening keys: UnityBake CubeKeyBaker samples FBX Legacy clips (Inverse(bind)*pose). Python gen_cube_keys is dump-only.
# Do not invent axis remaps.
import json
import math
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DUMP = os.path.join(ROOT, "UnityBake", "Assets", "MissilePack", "Textures", "Crosswim", "crosswim_cube_frames.json")
OUT = os.path.join(ROOT, "MK65Crosswim", "Runtime", "CrosswimCubeKeys.cs")

UNITY_REST_POS = {
    "Cube": (1.6919622, 0.6897914, -0.00027583167),
    "Cube.001": (1.6919622, 0.00027607224, 0.68979096),
    "Cube.002": (1.6919627, -0.6897914, -0.00027583167),
    "Cube.003": (1.6919627, 0.00027607224, -0.68979096),
}

UNITY_ORDER = ["Cube", "Cube.001", "Cube.002", "Cube.003"]

# Top/bottom: Unity rest ≈ RestPose Convert(Blender). Sides do not — keep raw.
USE_FBX_CONVERT = {"Cube", "Cube.002"}

Q_NEG90X_XYZW = (-0.7071067811865476, 0.0, 0.0, 0.7071067811865476)


def qmul_wxyz(a, b):
    aw, ax, ay, az = a
    bw, bx, by, bz = b
    return (
        aw * bw - ax * bx - ay * by - az * bz,
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
    )


def qnorm_wxyz(q):
    w, x, y, z = q
    n = math.sqrt(w * w + x * x + y * y + z * z) or 1.0
    return (w / n, x / n, y / n, z / n)


def qinv_wxyz(q):
    w, x, y, z = qnorm_wxyz(q)
    return (w, -x, -y, -z)


def xyzw_to_wxyz(q):
    x, y, z, w = q
    return (w, x, y, z)


def wxyz_to_xyzw(q):
    w, x, y, z = q
    return (x, y, z, w)


def qmul_xyzw(a, b):
    return wxyz_to_xyzw(qmul_wxyz(xyzw_to_wxyz(a), xyzw_to_wxyz(b)))


def qinv_xyzw(q):
    return wxyz_to_xyzw(qinv_wxyz(xyzw_to_wxyz(q)))


def qnorm_xyzw(q):
    return wxyz_to_xyzw(qnorm_wxyz(xyzw_to_wxyz(q)))


def blender_wxyz_to_unity_xyzw(w, x, y, z):
    """Same remap as hangar RestPose / FBX for Cube±Y."""
    remapped = (-x, -z, -y, w)  # xyzw
    return qnorm_xyzw(qmul_xyzw(Q_NEG90X_XYZW, remapped))


def axis_angle_wxyz(q):
    w, x, y, z = qnorm_wxyz(q)
    w = max(-1.0, min(1.0, w))
    ang = 2.0 * math.acos(abs(w))
    s = math.sqrt(max(0.0, 1.0 - w * w))
    if s < 1e-8:
        return (1.0, 0.0, 0.0), 0.0
    if w < 0.0:
        return (-x / s, -y / s, -z / s), ang
    return (x / s, y / s, z / s), ang


def stabilize_xyzw(rots):
    out = []
    prev = None
    for r in rots:
        if prev is not None and (prev[0] * r[0] + prev[1] * r[1] + prev[2] * r[2] + prev[3] * r[3]) < 0.0:
            r = (-r[0], -r[1], -r[2], -r[3])
        out.append(r)
        prev = r
    return out


def raw_local_delta_xyzw(q0_wxyz, qt_wxyz):
    rel = qnorm_wxyz(qmul_wxyz(qinv_wxyz(q0_wxyz), qt_wxyz))
    return wxyz_to_xyzw(rel)


def convert_local_delta_xyzw(q0_wxyz, qt_wxyz):
    c0 = blender_wxyz_to_unity_xyzw(*q0_wxyz)
    ct = blender_wxyz_to_unity_xyzw(*qt_wxyz)
    return qnorm_xyzw(qmul_xyzw(qinv_xyzw(c0), ct))


def map_pos(x, y, z):
    return (-x, z, y)


def dist3(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def match_blender_names(frames0, blender_names):
    mapping = {}
    used = set()
    for uname in UNITY_ORDER:
        up = UNITY_REST_POS[uname]
        best = None
        best_d = 1e9
        for bname in blender_names:
            if bname in used:
                continue
            loc = frames0[bname]["loc"]
            mp = map_pos(loc[0], loc[1], loc[2])
            d = dist3(mp, up)
            if d < best_d:
                best_d = d
                best = bname
        if best is None or best_d > 0.05:
            raise SystemExit(f"no Blender match for Unity '{uname}' (best={best} d={best_d})")
        mapping[uname] = best
        used.add(best)
        print(f"match Unity '{uname}' <- Blender '{best}' err={best_d:.2e}")
    return mapping


def main():
    with open(DUMP, encoding="utf-8") as f:
        data = json.load(f)
    frames = data["frames"]
    fps = data.get("fps", 24)
    blender_names = data.get("names") or UNITY_ORDER
    n = len(frames)
    if n < 2:
        raise SystemExit("need cube frames")

    mapping = match_blender_names(frames[0], blender_names)

    blocks = {c: [] for c in ("Rx", "Ry", "Rz", "Rw")}
    for uname in UNITY_ORDER:
        bname = mapping[uname]
        q0 = tuple(frames[0][bname]["quat"])
        use_convert = uname in USE_FBX_CONVERT
        rots = []
        for fr in frames:
            qt = tuple(fr[bname]["quat"])
            if use_convert:
                rots.append(convert_local_delta_xyzw(q0, qt))
            else:
                rots.append(raw_local_delta_xyzw(q0, qt))
        rots = stabilize_xyzw(rots)
        last = rots[-1]
        axis_u, ang_u = axis_angle_wxyz(xyzw_to_wxyz(last))
        mode = "convert" if use_convert else "raw"
        print(
            f"  open '{uname}' from '{bname}' [{mode}]: "
            f"ang={math.degrees(ang_u):.1f} axisU=({axis_u[0]:.3f},{axis_u[1]:.3f},{axis_u[2]:.3f})"
        )
        for r in rots:
            blocks["Rx"].append(r[0])
            blocks["Ry"].append(r[1])
            blocks["Rz"].append(r[2])
            blocks["Rw"].append(r[3])

    def fmt(vals):
        lines = []
        row = []
        for i, v in enumerate(vals):
            row.append(f"{v:.7f}f")
            if len(row) == 8 or i == len(vals) - 1:
                lines.append("            " + ", ".join(row) + ",")
                row = []
        return "\n".join(lines)

    match_comment = ", ".join(f"{u}<-{mapping[u]}" for u in UNITY_ORDER)
    cs = f"""// AUTO-GENERATED by UnityBake/gen_cube_keys.py — do not edit.
// Matched by rest pos ({match_comment}).
// Top/bottom: FBX Convert delta. Sides: raw Blender local delta.
// Driver: localRotation = bind * Sample. Frame 0 = identity.
using UnityEngine;

namespace Crosswim.Runtime
{{
    /// <summary>
    /// Blender opening 1:1. Top/bottom via FBX Convert; sides raw local.
    /// </summary>
    internal static class CrosswimCubeKeys
    {{
        internal const int FinCount = 4;
        internal const int FrameCount = {n};
        internal const float Fps = {float(fps):g}f;
        internal static readonly string[] Names = {{ "Cube", "Cube.001", "Cube.002", "Cube.003" }};

        private static readonly float[] Rx =
        {{
{fmt(blocks['Rx'])}
        }};

        private static readonly float[] Ry =
        {{
{fmt(blocks['Ry'])}
        }};

        private static readonly float[] Rz =
        {{
{fmt(blocks['Rz'])}
        }};

        private static readonly float[] Rw =
        {{
{fmt(blocks['Rw'])}
        }};

        internal static int Index(int fin, int frame) => fin * FrameCount + frame;

        internal static Quaternion Sample(int fin, float frame)
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
            float t = f - i0;
            int a = Index(fin, i0);
            int b = Index(fin, i1);
            Quaternion qa = new Quaternion(Rx[a], Ry[a], Rz[a], Rw[a]);
            Quaternion qb = new Quaternion(Rx[b], Ry[b], Rz[b], Rw[b]);
            return Quaternion.Slerp(qa, qb, t);
        }}
    }}
}}
"""
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(cs)

    for i, uname in enumerate(UNITY_ORDER):
        a = i * n
        x, y, z, w = blocks["Rx"][a], blocks["Ry"][a], blocks["Rz"][a], blocks["Rw"][a]
        err = abs(x) + abs(y) + abs(z) + abs(abs(w) - 1.0)
        print(f"rest {uname} delta=({x:.6f},{y:.6f},{z:.6f},{w:.6f}) err={err:.2e}")
        if err > 1e-4:
            raise SystemExit(f"frame 0 is not identity for {uname}")
    print("WROTE", OUT, "fins", len(UNITY_ORDER), "frames", n)


if __name__ == "__main__":
    main()
