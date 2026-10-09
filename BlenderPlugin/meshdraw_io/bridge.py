"""Blender bridge: import FBX, pack maps, preserve source data, export current geometry."""
import base64
import json
import tempfile
import uuid
from pathlib import Path
import bpy
from . import bundle

SOURCE_KEY = "meshdraw_source"
INPUTS = {"basecolor": "Base Color", "roughness": "Roughness", "metallic": "Metallic", "emission": "Emission Color"}
DEFAULTS = {"basecolor": (0.8, 0.8, 0.8, 1), "roughness": (0.5, 0.5, 0.5, 1), "metallic": (0, 0, 0, 1), "emission": (0, 0, 0, 1), "height": (0.5, 0.5, 0.5, 1)}

def asset_collection(context):
    def find(collection):
        if SOURCE_KEY in collection: return collection
        for parent in bpy.data.collections:
            if collection.name in parent.children:
                found = find(parent)
                if found: return found
        return None
    if context.active_object:
        for collection in context.active_object.users_collection:
            found = find(collection)
            if found: return found
    active = context.view_layer.active_layer_collection.collection
    return find(active)

def build_material(folder, textures, name):
    material = bpy.data.materials.new(name); material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    shader = nodes.get("Principled BSDF")
    shader.inputs["Emission Strength"].default_value = 1
    for kind, filename in textures.items():
        image = bpy.data.images.load(str(folder / filename), check_existing=False)
        image.name = name + "_" + kind
        image.colorspace_settings.name = "sRGB" if kind in ("basecolor", "emission") else "Non-Color"
        image.pack()
        node = nodes.new("ShaderNodeTexImage"); node.image = image; node.label = kind; node["meshdraw_channel"] = kind
        if kind == "height":
            bump = nodes.new("ShaderNodeBump"); bump.inputs["Distance"].default_value = .04
            links.new(node.outputs["Color"], bump.inputs["Height"]); links.new(bump.outputs["Normal"], shader.inputs["Normal"])
        else: links.new(node.outputs["Color"], shader.inputs[INPUTS[kind]])
    return material

def import_asset(context, filepath):
    if context.mode != "OBJECT": raise ValueError("Switch to Object Mode before importing")
    document, files = bundle.read(filepath)
    previous = set(bpy.data.objects); previous_materials = set(bpy.data.materials); previous_images = set(bpy.data.images)
    collection = None; text = None
    try:
        with tempfile.TemporaryDirectory(prefix="meshdraw-import-") as directory:
            folder = Path(directory)
            # Never open or execute the embedded .blend file.
            for name, payload in files.items():
                if name == "mesh.fbx" or name.endswith(".png"): (folder / name).write_bytes(payload)
            result = bpy.ops.import_scene.fbx(filepath=str(folder / "mesh.fbx"), use_anim=True)
            if "FINISHED" not in result: raise ValueError("Blender could not import the mesh")
            objects = set(bpy.data.objects) - previous
            meshes = [o for o in objects if o.type == "MESH"]
            rigs = [o for o in objects if o.type == "ARMATURE"]
            if not meshes or bool(rigs) != (document["outputType"] == "skeletal"):
                raise ValueError("FBX contents do not match the declared mesh type")
            collection = bpy.data.collections.new("MeshDraw · " + Path(filepath).stem)
            context.scene.collection.children.link(collection)
            material = build_material(folder, document["textures"], collection.name)
            for obj in objects:
                for old in list(obj.users_collection): old.objects.unlink(obj)
                collection.objects.link(obj)
                if obj.type == "MESH": obj.data.materials.clear(); obj.data.materials.append(material)
            # Source-only archive survives saving/reopening the .blend, without requiring the original asset file.
            source_files = {n: p for n, p in files.items() if n not in ("mesh.fbx", "mesh.blend", "shape.obj", "result.json", "export-summary.json") and n not in document["textures"].values()}
            source = {"document": {k: v for k, v in document.items() if k != "files"}, "files": {n: base64.b64encode(p).decode("ascii") for n, p in source_files.items()}}
            text = bpy.data.texts.new("MeshDraw source " + uuid.uuid4().hex)
            text.write(json.dumps(source, separators=(",", ":"))); text.use_fake_user = True
            collection[SOURCE_KEY] = text.name
            collection["meshdraw_output_type"] = document["outputType"]
            collection["meshdraw_source_name"] = Path(filepath).name
            for image in set(bpy.data.images) - previous_images:
                if image.has_data: image.pack()
        for material in set(bpy.data.materials) - previous_materials:
            if material.users == 0: bpy.data.materials.remove(material)
        bpy.ops.object.select_all(action="DESELECT")
        for obj in objects: obj.select_set(True)
        context.view_layer.objects.active = meshes[0]
        return collection
    except Exception:
        for obj in set(bpy.data.objects) - previous: bpy.data.objects.remove(obj, do_unlink=True)
        if collection: bpy.data.collections.remove(collection)
        if text: bpy.data.texts.remove(text)
        for material in set(bpy.data.materials) - previous_materials:
            if material.users == 0: bpy.data.materials.remove(material)
        for image in set(bpy.data.images) - previous_images:
            if image.users == 0: bpy.data.images.remove(image)
        raise

def map_source(material, kind):
    if not material or not material.use_nodes: return None, DEFAULTS[kind]
    tree = material.node_tree
    outputs = [n for n in tree.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output]
    if not outputs or not outputs[0].inputs["Surface"].is_linked: return None, DEFAULTS[kind]
    shader = outputs[0].inputs["Surface"].links[0].from_node
    if shader.type != "BSDF_PRINCIPLED": raise ValueError("Use a Principled BSDF material for MeshDraw export")
    if kind == "height":
        socket = shader.inputs["Normal"]
        if not socket.is_linked: return None, DEFAULTS[kind]
        bump = socket.links[0].from_node
        if bump.type != "BUMP": raise ValueError("Use a Bump node with a height image for height export")
        socket = bump.inputs["Height"]
    else: socket = shader.inputs[INPUTS[kind]]
    if socket.is_linked:
        link = socket.links[0]
        if link.from_node.type != "TEX_IMAGE" or link.from_node.image is None or link.from_socket.name != "Color":
            raise ValueError("Bake procedural " + kind + " nodes to an image before exporting")
        image = link.from_node.image
        if image.source not in ("FILE", "GENERATED"): raise ValueError("Use a single image for " + kind)
        return image, None
    value = socket.default_value
    return None, tuple(value) if hasattr(value, "__len__") else (value, value, value, 1)

def export_image(image, color, path, kind):
    temporary = image is None
    if temporary:
        image = bpy.data.images.new("MeshDraw export " + kind, width=1, height=1, alpha=True)
        image.colorspace_settings.name = "sRGB" if kind in ("basecolor", "emission") else "Non-Color"
        image.pixels[:] = color
    # Packed images load lazily after reopening a blend. Access pixels to decode them first.
    if len(image.pixels) == 0: raise ValueError("Texture has no pixel data: " + kind)
    original_path, original_format = image.filepath_raw, image.file_format
    try:
        image.filepath_raw = str(path); image.file_format = "PNG"; image.save()
    finally:
        image.filepath_raw = original_path; image.file_format = original_format
        if temporary: bpy.data.images.remove(image)

def export_asset(context, filepath, collection=None):
    if context.mode != "OBJECT": raise ValueError("Switch to Object Mode before exporting")
    collection = collection or asset_collection(context)
    if not collection: raise ValueError("Select an object from an imported MeshDraw collection")
    text = bpy.data.texts.get(collection.get(SOURCE_KEY, ""))
    if not text: raise ValueError("This collection has lost its MeshDraw source data")
    source = json.loads(text.as_string()); document = source["document"]
    files = {n: base64.b64decode(p, validate=True) for n, p in source["files"].items()}
    objects = [o for o in collection.all_objects if o.type in ("MESH", "ARMATURE", "EMPTY")]
    meshes = [o for o in objects if o.type == "MESH"]
    rigs = [o for o in objects if o.type == "ARMATURE"]
    if not meshes: raise ValueError("Collection has no mesh to export")
    if len(rigs) > 1: raise ValueError("Export one skeleton per MeshDraw asset")
    for mesh in meshes:
        if mesh.find_armature() and mesh.find_armature() not in rigs: raise ValueError("Move the mesh's armature into the MeshDraw collection")
    materials = {slot.material for mesh in meshes for slot in mesh.material_slots if slot.material}
    if len(materials) > 1: raise ValueError("MeshDraw uses one material atlas. Combine or bake materials into one before exporting")
    material = next(iter(materials), None)
    maps = {kind: map_source(material, kind) for kind in bundle.CHANNELS}
    selection = list(context.selected_objects); active = context.view_layer.objects.active
    visibility = {obj: (obj.hide_get(), obj.hide_viewport) for obj in objects}
    try:
        with tempfile.TemporaryDirectory(prefix="meshdraw-export-") as directory:
            folder = Path(directory)
            bpy.ops.object.select_all(action="DESELECT")
            for obj in objects:
                if obj.name not in context.view_layer.objects: raise ValueError("Enable the MeshDraw collection in this view layer before exporting")
                obj.hide_viewport = False; obj.hide_set(False); obj.select_set(True)
            context.view_layer.objects.active = meshes[0]
            result = bpy.ops.export_scene.fbx(filepath=str(folder / "mesh.fbx"), use_selection=True, object_types={"MESH", "ARMATURE", "EMPTY"}, add_leaf_bones=False, bake_anim=False, axis_forward="-Z", axis_up="Y", apply_unit_scale=True, path_mode="STRIP")
            if "FINISHED" not in result: raise ValueError("Blender could not export the mesh")
            files["mesh.fbx"] = (folder / "mesh.fbx").read_bytes()
            document["textures"] = {}
            for kind, (image, color) in maps.items():
                name = f"mesh_{kind}.png"; export_image(image, color, folder / name, kind)
                files[name] = (folder / name).read_bytes(); document["textures"][kind] = name
            mode = "skeletal" if rigs else "static"; document["outputType"] = mode
            project = json.loads(files["project.meshdraw"]); project["OutputType"] = 1 if rigs else 0
            files["project.meshdraw"] = json.dumps(project, separators=(",", ":")).encode()
            job = json.loads(files["job.json"]); job["outputType"] = mode
            files["job.json"] = json.dumps(job, separators=(",", ":")).encode()
            files["export-summary.json"] = json.dumps({"textures": document["textures"], "rig": "Blender edited" if rigs else None, "boneCount": len(rigs[0].data.bones) if rigs else 0, "sourceDrawings": "Original MeshDraw drawings; Blender geometry and rig edits are in mesh.fbx", "blenderVersion": bpy.app.version_string}).encode()
            bundle.write(filepath, document, files)
    finally:
        bpy.ops.object.select_all(action="DESELECT")
        for obj, (hidden, hidden_viewport) in visibility.items():
            obj.hide_set(hidden); obj.hide_viewport = hidden_viewport
        for obj in selection:
            if obj.name in context.view_layer.objects: obj.select_set(True)
        context.view_layer.objects.active = active
    return len(meshes), mode
