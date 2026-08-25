# Dump MK-65-Crosswim.blend: objects, UCPaint/Principled/Mix, images → crosswim_look.json
import json
import os
import shutil
import sys
import bpy

out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else None
if not out:
    raise SystemExit("need -- <out_json>")


def sock_val(s):
    if s is None:
        return None
    v = s.default_value
    try:
        return [round(float(x), 5) for x in list(v)]
    except TypeError:
        try:
            return round(float(v), 5)
        except Exception:
            return str(v)


def follow_tex(sock):
    if sock is None or not sock.is_linked:
        return None
    n = sock.links[0].from_node
    seen = set()
    while n is not None and id(n) not in seen:
        seen.add(id(n))
        if n.type == "TEX_IMAGE" and getattr(n, "image", None):
            img = n.image
            return {
                "image": img.name,
                "path": bpy.path.abspath(img.filepath) if img.filepath else "",
                "packed": img.packed_file is not None,
            }
        if n.type in ("MIX", "MIX_RGB", "MIX_SHADER", "UCPAINT", "GROUP"):
            for key in ("Color1", "Color", "Base Color", "Shader", "A"):
                if key in n.inputs and n.inputs[key].is_linked:
                    n = n.inputs[key].links[0].from_node
                    break
            else:
                return {"from": n.name, "type": n.type}
            continue
        if n.inputs:
            for inp in n.inputs:
                if inp.is_linked:
                    n = inp.links[0].from_node
                    break
            else:
                return {"from": n.name, "type": n.type}
            continue
        return {"from": n.name, "type": n.type}
    return None


def dump_mat(mat):
    d = {"name": mat.name, "use_nodes": mat.use_nodes}
    if not mat.node_tree:
        return d
    principled = None
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            principled = n
            break
    if principled is None:
        d["nodes"] = [{"name": n.name, "type": n.type} for n in mat.node_tree.nodes]
        return d
    bc = principled.inputs.get("Base Color")
    met = principled.inputs.get("Metallic")
    rough = principled.inputs.get("Roughness")
    nrm = principled.inputs.get("Normal")
    col = sock_val(bc) or [0.8, 0.8, 0.8, 1]
    if isinstance(col, (int, float)):
        col = [col, col, col, 1]
    while len(col) < 4:
        col.append(1.0)
    d["colR"], d["colG"], d["colB"], d["colA"] = col[0], col[1], col[2], col[3]
    d["metallic"] = sock_val(met) if not (met and met.is_linked) else 0.0
    d["roughness"] = sock_val(rough) if not (rough and rough.is_linked) else 0.5
    if isinstance(d["metallic"], list):
        d["metallic"] = d["metallic"][0]
    if isinstance(d["roughness"], list):
        d["roughness"] = d["roughness"][0]
    d["albedoTex"] = follow_tex(bc)
    d["metallicTex"] = follow_tex(met)
    d["roughnessTex"] = follow_tex(rough)
    d["normalTex"] = follow_tex(nrm)
    mix = []
    for n in mat.node_tree.nodes:
        if n.type in ("MIX", "MIX_RGB"):
            mix.append({
                "name": n.name,
                "blend": getattr(n, "blend_type", getattr(n, "data_type", None)),
                "fac": sock_val(n.inputs.get("Fac") or n.inputs.get("Factor")),
                "a": follow_tex(n.inputs.get("A") or n.inputs.get("Color1")),
                "b": follow_tex(n.inputs.get("B") or n.inputs.get("Color2")),
            })
        if "UCPAINT" in n.type or "ucpaint" in (n.bl_idname or "").lower() or "UCPaint" in n.name:
            mix.append({"name": n.name, "type": n.type, "id": n.bl_idname})
    d["mix"] = mix
    return d


scene = {
    "fps": bpy.context.scene.render.fps,
    "openingFrames": 120,
    "objects": [],
    "materials": [],
    "images": [],
}
for obj in bpy.data.objects:
    item = {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "loc": [round(x, 5) for x in obj.location],
        "rot_e": [round(x, 5) for x in obj.rotation_euler],
        "scale": [round(x, 5) for x in obj.scale],
    }
    if obj.type == "MESH" and obj.data:
        me = obj.data
        item["verts"] = len(me.vertices)
        item["mats"] = [s.material.name if s.material else None for s in obj.material_slots]
        item["uv"] = [uv.name for uv in me.uv_layers]
    if obj.animation_data and obj.animation_data.action:
        item["action"] = obj.animation_data.action.name
        item["action_frames"] = [
            int(obj.animation_data.action.frame_range[0]),
            int(obj.animation_data.action.frame_range[1]),
        ]
    scene["objects"].append(item)

for mat in bpy.data.materials:
    if mat.users > 0:
        scene["materials"].append(dump_mat(mat))

tex_dir = os.path.join(os.path.dirname(out), "packed")
os.makedirs(tex_dir, exist_ok=True)
for img in bpy.data.images:
    rec = {
        "name": img.name,
        "path": bpy.path.abspath(img.filepath) if img.filepath else "",
        "size": list(img.size) if img.size else None,
        "packed": img.packed_file is not None,
        "users": img.users,
    }
    dest = None
    try:
        if img.packed_file is not None:
            ext = os.path.splitext(img.name)[1] or ".png"
            dest = os.path.join(tex_dir, bpy.path.clean_name(img.name) + ("" if img.name.lower().endswith(ext.lower()) else ext))
            img.save_render(dest)
        elif rec["path"] and os.path.isfile(rec["path"]):
            dest = os.path.join(tex_dir, os.path.basename(rec["path"]))
            if not os.path.isfile(dest):
                shutil.copy2(rec["path"], dest)
    except Exception as ex:
        rec["copy_error"] = str(ex)
    if dest:
        rec["copied"] = dest
    scene["images"].append(rec)

os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
with open(out, "w", encoding="utf-8") as f:
    json.dump(scene, f, indent=2, ensure_ascii=False)
print("WROTE", out, "OBJS", len(scene["objects"]), "MATS", len(scene["materials"]))
