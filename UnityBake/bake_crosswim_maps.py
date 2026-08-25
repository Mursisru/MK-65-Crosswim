# Bake MK-65 Crosswim UCUPaint/Principled Base Color (+ rough/normal) to UV PNGs + look.json.
# Face-isolated per material so overlapping 0-1 UVs on other slots cannot overwrite.
# blender.exe MK-65-Crosswim.blend --background --python bake_crosswim_maps.py -- <out_dir>
import json
import os
import sys
import bpy
import bmesh

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else None
if not OUT:
    raise SystemExit("need -- <out_dir>")
os.makedirs(OUT, exist_ok=True)

RES = 2048
TARGET_MATS = ("GlossyMetal", "Metal", "Metal2", "LightMaterial")


def find_bsdf(nt):
    for n in nt.nodes:
        if n.type == "BSDF_PRINCIPLED":
            return n
    return None


def sock_value(s):
    if s is None:
        return None
    v = s.default_value
    try:
        return [round(float(x), 5) for x in list(v)]
    except TypeError:
        try:
            return round(float(v), 5)
        except Exception:
            return None


def ensure_uv(obj):
    me = obj.data
    if me.uv_layers:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=66.0, island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")


def objects_using_material(mat):
    out = []
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.data is None:
            continue
        for slot in obj.material_slots:
            if slot.material == mat:
                out.append(obj)
                break
    return out


def isolate_faces(obj, mat):
    idxs = {i for i, s in enumerate(obj.material_slots) if s.material == mat}
    if not idxs:
        return None
    dup = obj.copy()
    dup.data = obj.data.copy()
    dup.name = obj.name + "__bake"
    bpy.context.scene.collection.objects.link(dup)
    bm = bmesh.new()
    bm.from_mesh(dup.data)
    drop = [f for f in bm.faces if f.material_index not in idxs]
    if drop:
        bmesh.ops.delete(bm, geom=drop, context="FACES")
    bm.to_mesh(dup.data)
    bm.free()
    if len(dup.data.polygons) == 0:
        me = dup.data
        bpy.data.objects.remove(dup, do_unlink=True)
        bpy.data.meshes.remove(me)
        return None
    dup.data.materials.clear()
    dup.data.materials.append(mat)
    for p in dup.data.polygons:
        p.material_index = 0
    ensure_uv(dup)
    return dup


def free_temps(temps):
    for t in temps:
        if t is None:
            continue
        me = t.data
        bpy.data.objects.remove(t, do_unlink=True)
        if me is not None:
            bpy.data.meshes.remove(me)


def wire_emit_from_socket(mat, from_node, from_socket_name):
    nt = mat.node_tree
    bsdf = find_bsdf(nt)
    if bsdf is None:
        raise RuntimeError("no Principled on " + mat.name)
    for link in list(nt.links):
        if link.to_node == bsdf and link.to_socket.name in (
            "Emission Color",
            "Emission Strength",
            "Emission",
        ):
            nt.links.remove(link)
    emit_col = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
    emit_str = bsdf.inputs.get("Emission Strength")
    src = from_node.outputs.get(from_socket_name)
    if emit_col is None or src is None:
        raise RuntimeError("emit wire fail " + mat.name)
    nt.links.new(src, emit_col)
    if emit_str is not None:
        emit_str.default_value = 1.0


def wire_emit_color(mat, color):
    nt = mat.node_tree
    bsdf = find_bsdf(nt)
    if bsdf is None:
        raise RuntimeError("no Principled on " + mat.name)
    for link in list(nt.links):
        if link.to_node == bsdf and link.to_socket.name in (
            "Emission Color",
            "Emission Strength",
            "Emission",
        ):
            nt.links.remove(link)
    emit_col = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
    emit_str = bsdf.inputs.get("Emission Strength")
    if emit_col is None:
        raise RuntimeError("no emission sock " + mat.name)
    while len(color) < 4:
        color.append(1.0)
    emit_col.default_value = (color[0], color[1], color[2], color[3])
    if emit_str is not None:
        emit_str.default_value = 1.0


def add_bake_target(mat, img):
    nodes = mat.node_tree.nodes
    for n in nodes:
        if n.type == "TEX_IMAGE":
            n.select = False
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.select = True
    nodes.active = tex
    return tex


def drop_node(mat, node):
    if node is not None:
        mat.node_tree.nodes.remove(node)


def bake_emit(temps, mat, path, img_name, srgb=True):
    img = bpy.data.images.new(img_name, width=RES, height=RES, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    tex = add_bake_target(mat, img)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in temps:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = temps[0]
    bpy.context.scene.render.engine = "CYCLES"
    bpy.context.scene.cycles.samples = 32
    bpy.context.scene.cycles.bake_type = "EMIT"
    bpy.context.scene.render.bake.use_clear = True
    bpy.context.scene.render.bake.margin = 16
    bpy.ops.object.bake(type="EMIT")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    drop_node(mat, tex)
    bpy.data.images.remove(img)


def bake_normal(temps, mat, path, img_name):
    img = bpy.data.images.new(img_name, width=RES, height=RES, alpha=False, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    tex = add_bake_target(mat, img)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in temps:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = temps[0]
    bpy.context.scene.render.engine = "CYCLES"
    bpy.context.scene.cycles.samples = 32
    bpy.context.scene.render.bake.normal_space = "TANGENT"
    bpy.context.scene.render.bake.use_clear = True
    bpy.context.scene.render.bake.margin = 16
    bpy.ops.object.bake(type="NORMAL")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    drop_node(mat, tex)
    bpy.data.images.remove(img)


def unpack_images():
    for img in bpy.data.images:
        if img.packed_file is None or not img.size[0]:
            continue
        if img.name in ("Render Result", "Viewer Node"):
            continue
        # Skip UCUPaint solid-color masks — they are not albedo.
        if img.name.startswith("Mask (Solid Color"):
            continue
        stem = os.path.splitext(os.path.basename(img.name))[0]
        ext = os.path.splitext(img.name)[1] or ".png"
        path = os.path.join(OUT, stem + ext)
        img.filepath_raw = path
        img.file_format = "JPEG" if ext.lower() in (".jpg", ".jpeg") else "PNG"
        img.save()
        print("UNPACKED", path)


def dump_look(albedo_map):
    mats = []
    for mat in bpy.data.materials:
        if mat.name not in TARGET_MATS:
            continue
        if mat.users <= 0 or mat.node_tree is None:
            continue
        bsdf = find_bsdf(mat.node_tree)
        if bsdf is None:
            continue
        ins = bsdf.inputs
        bc = sock_value(ins.get("Base Color")) or [1, 1, 1, 1]
        if not isinstance(bc, list):
            bc = [bc, bc, bc, 1.0]
        while len(bc) < 4:
            bc.append(1.0)
        rough = sock_value(ins.get("Roughness"))
        if isinstance(rough, list):
            rough = rough[0]
        met = sock_value(ins.get("Metallic"))
        if isinstance(met, list):
            met = met[0]
        entry = {
            "name": mat.name,
            "colR": bc[0],
            "colG": bc[1],
            "colB": bc[2],
            "colA": bc[3],
            "metallic": met if met is not None else 0.0,
            "roughness": rough if rough is not None else 0.5,
            "albedo": albedo_map.get(mat.name, ""),
            "occlusion": "GlossyMetal AO.png" if mat.name == "GlossyMetal" else "",
            "roughnessMap": mat.name + "_Roughness.png"
            if os.path.isfile(os.path.join(OUT, mat.name + "_Roughness.png"))
            else "",
            "normalMap": mat.name + "_Normal.png"
            if os.path.isfile(os.path.join(OUT, mat.name + "_Normal.png"))
            else "",
            "baseColorLinked": 1 if (ins.get("Base Color") and ins["Base Color"].is_linked) else 0,
        }
        mats.append(entry)
    path = os.path.join(OUT, "crosswim_look.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"fps": 24, "openingFrames": 120, "mats": mats}, f, indent=2)
    print("LOOK", path, "n", len(mats))


bpy.context.scene.render.engine = "CYCLES"
bpy.context.scene.cycles.device = "CPU"

unpack_images()
albedo_map = {}

for mat_name in TARGET_MATS:
    mat = bpy.data.materials.get(mat_name)
    if mat is None or mat.users <= 0 or mat.node_tree is None:
        print("SKIP missing", mat_name)
        continue
    bsdf = find_bsdf(mat.node_tree)
    if bsdf is None:
        print("SKIP no bsdf", mat_name)
        continue

    src_objs = objects_using_material(mat)
    if not src_objs:
        print("SKIP no objs", mat_name)
        continue
    temps = []
    for o in src_objs:
        iso = isolate_faces(o, mat)
        if iso is not None:
            temps.append(iso)
    if not temps:
        print("SKIP empty isolate", mat_name)
        continue

    try:
        bc = bsdf.inputs.get("Base Color")
        albedo_path = os.path.join(OUT, mat_name + "_Albedo.png")
        if bc is not None and bc.is_linked:
            ln = bc.links[0]
            wire_emit_from_socket(mat, ln.from_node, ln.from_socket.name)
            bake_emit(temps, mat, albedo_path, mat_name + "_AlbedoBake", srgb=True)
            albedo_map[mat_name] = mat_name + "_Albedo.png"
            print("BAKED albedo", albedo_path)
        else:
            # Solid tint only (Metal2 / LightMaterial) — still write flat albedo for UV paint parity.
            col = sock_value(bc) or [0.8, 0.8, 0.8, 1.0]
            if not isinstance(col, list):
                col = [col, col, col, 1.0]
            wire_emit_color(mat, list(col))
            bake_emit(temps, mat, albedo_path, mat_name + "_AlbedoBake", srgb=True)
            albedo_map[mat_name] = mat_name + "_Albedo.png"
            print("BAKED solid albedo", albedo_path)

        rough = bsdf.inputs.get("Roughness")
        if rough is not None and rough.is_linked:
            ln = rough.links[0]
            wire_emit_from_socket(mat, ln.from_node, ln.from_socket.name)
            path = os.path.join(OUT, mat_name + "_Roughness.png")
            bake_emit(temps, mat, path, mat_name + "_RoughnessBake", srgb=False)
            print("BAKED roughness", path)

        nrm = bsdf.inputs.get("Normal")
        if nrm is not None and nrm.is_linked:
            path = os.path.join(OUT, mat_name + "_Normal.png")
            bake_normal(temps, mat, path, mat_name + "_NormalBake")
            print("BAKED normal", path)
    finally:
        free_temps(temps)

dump_look(albedo_map)
print("DONE", OUT)
