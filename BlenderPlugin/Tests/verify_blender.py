"""Run in Blender with --background --factory-startup --python-exit-code 1."""
import sys
import json
from pathlib import Path
import bpy
root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'BlenderPlugin'))
if '--installed' in sys.argv:
    import importlib
    bpy.ops.preferences.addon_enable(module='bl_ext.user_default.meshdraw_io')
    meshdraw_io=importlib.import_module('bl_ext.user_default.meshdraw_io')
else:
    import meshdraw_io
    meshdraw_io.register()
bridge,bundle=meshdraw_io.bridge,meshdraw_io.bridge.bundle
out = root / 'outputs' / 'blender-plugin-checks'; out.mkdir(exist_ok=True)
import tempfile
(out / 'temp').mkdir(exist_ok=True); tempfile.tempdir=str(out / 'temp')
assert hasattr(bpy.ops.meshdraw, 'import_asset') and hasattr(bpy.ops.meshdraw, 'export_asset')
def reset_scene(empty):
    bpy.ops.wm.read_factory_settings(use_empty=empty)
    if '--installed' in sys.argv: bpy.ops.preferences.addon_enable(module='bl_ext.user_default.meshdraw_io')
results = []
for mode, folder in [('static', 'layer-checks-static'), ('skeletal', 'layer-checks')]:
    reset_scene(False)
    source = root / 'outputs' / folder / 'asset.meshdrawasset'
    original, original_files = bundle.read(source)
    assert bpy.ops.meshdraw.import_asset(filepath=str(source)) == {'FINISHED'}
    collection = bridge.asset_collection(bpy.context)
    assert collection and bpy.data.objects.get('Cube')
    meshes = [o for o in collection.all_objects if o.type == 'MESH']
    rigs = [o for o in collection.all_objects if o.type == 'ARMATURE']
    assert len(meshes) == 1 and bool(rigs) == (mode == 'skeletal')
    mesh = meshes[0]; assert mesh.data.uv_layers.active
    material = mesh.data.materials[0]
    images = {n.label: n.image for n in material.node_tree.nodes if n.type == 'TEX_IMAGE'}
    assert set(images) == set(bundle.CHANNELS) and all(i.packed_file for i in images.values())
    assert images['basecolor'].colorspace_settings.name == 'sRGB'
    assert images['height'].colorspace_settings.name == 'Non-Color'
    shader = material.node_tree.nodes.get('Principled BSDF')
    assert shader.inputs['Normal'].is_linked
    # Edit geometry and replace a texture with a newly painted generated image.
    mesh.location.x += .25
    for vertex in mesh.data.vertices: vertex.co.x *= 1.2
    image = bpy.data.images.new('Edited blue', width=8, height=8, alpha=True)
    image.colorspace_settings.name = 'sRGB'; image.pixels[:] = [0, 0, 1, 1] * 64; image.pack()
    next(n for n in material.node_tree.nodes if n.label == 'basecolor').image = image
    if rigs:
        bpy.context.view_layer.objects.active = rigs[0]
        bpy.ops.object.mode_set(mode='EDIT')
        bone = rigs[0].data.edit_bones.new('blender_added'); bone.head = (0, 0, 0); bone.tail = (0, 0, .2)
        bone.parent = rigs[0].data.edit_bones.get('root')
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.context.view_layer.objects.active = mesh
    bpy.context.view_layer.update()
    expected_min = min((mesh.matrix_world @ v.co).x for v in mesh.data.vertices)
    expected_max = max((mesh.matrix_world @ v.co).x for v in mesh.data.vertices)
    # Packed images + embedded source must survive closing the original source package and saving .blend.
    blend = out / (mode + '.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    collection = bridge.asset_collection(bpy.context)
    assert collection and bpy.data.texts.get(collection[bridge.SOURCE_KEY]) is not None
    selected = set(bpy.context.selected_objects); active = bpy.context.active_object
    destination = out / (mode + '.meshdrawasset')
    assert bpy.ops.meshdraw.export_asset(filepath=str(destination)) == {'FINISHED'}
    assert set(bpy.context.selected_objects) == selected and bpy.context.active_object == active
    exported, exported_files = bundle.read(destination)
    assert exported['outputType'] == mode and len(exported['textures']) == 5
    for name in ('project.meshdraw', 'skeleton.json', 'front.png', 'back.png', 'left.png', 'right.png', 'top.png', 'bottom.png'):
        assert json.loads(exported_files[name]) == json.loads(original_files[name]) if name.endswith(('.json', '.meshdraw')) else exported_files[name] == original_files[name]
    reset_scene(True)
    assert bpy.ops.meshdraw.import_asset(filepath=str(destination)) == {'FINISHED'}
    collection = bridge.asset_collection(bpy.context)
    imported_meshes = [o for o in collection.all_objects if o.type == 'MESH']; assert len(imported_meshes) == 1
    mesh = imported_meshes[0]; bpy.context.view_layer.update()
    assert abs(min((mesh.matrix_world @ v.co).x for v in mesh.data.vertices) - expected_min) < 1e-4
    assert abs(max((mesh.matrix_world @ v.co).x for v in mesh.data.vertices) - expected_max) < 1e-4
    image = next(n.image for n in mesh.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.label == 'basecolor')
    assert image.size[:] == (8, 8) and image.pixels[2] > .99 and image.pixels[0] < .01
    rigs = [o for o in collection.all_objects if o.type == 'ARMATURE']
    if mode == 'skeletal':
        assert 'blender_added' in rigs[0].data.bones
        assert rigs[0].data.bones['drawn_branch'].parent.name == 'drawn_spine'
        assert any(v.groups for v in mesh.data.vertices)
    else: assert not rigs
    results.append(mode + ': import/export, edited geometry, edited texture, UVs, source retention, packed blend reopen, isolated collection, skeleton and skin weights')
# Unsupported materials must not overwrite an existing export.
protected=out/'protected.meshdrawasset';protected.write_bytes(b'keep previous export')
mesh=bpy.context.active_object;original_material=mesh.data.materials[0]
mesh.data.materials.append(bpy.data.materials.new('Second material'))
try: bridge.export_asset(bpy.context,protected);raise AssertionError('Multiple materials accepted')
except ValueError as error: assert 'one material' in str(error)
assert protected.read_bytes()==b'keep previous export'
mesh.data.materials.pop(index=1)
output=next(n for n in original_material.node_tree.nodes if n.type=='OUTPUT_MATERIAL')
diffuse=original_material.node_tree.nodes.new('ShaderNodeBsdfDiffuse');original_material.node_tree.links.new(diffuse.outputs[0],output.inputs['Surface'])
try: bridge.export_asset(bpy.context,protected);raise AssertionError('Unsupported shader accepted')
except ValueError as error: assert 'Principled' in str(error)
assert protected.read_bytes()==b'keep previous export'
results.append('unsupported multi-material/shader exports rejected; previous destination preserved')
# Damaged and unsafe files cannot mutate the scene.
document, files = bundle.read(root / 'outputs/layer-checks/asset.meshdrawasset')
for field, value in [('sha1', '0' * 40), ('name', '../outside.fbx')]:
    changed = json.loads(json.dumps(document)); changed['files'][0][field] = value
    path = out / ('bad-' + field + '.meshdrawasset'); path.write_text(json.dumps(changed), encoding='utf-8')
    before = set(bpy.data.objects)
    try: bridge.import_asset(bpy.context, path); raise AssertionError('Corrupt file accepted')
    except ValueError: pass
    assert set(bpy.data.objects) == before
results.append('checksums and unsafe paths rejected before scene changes')
(out / 'checks.txt').write_text('PASS\n' + '\n'.join(results), encoding='utf-8')
print('PASS:', *results, sep='\n')
if '--installed' in sys.argv: bpy.ops.preferences.addon_disable(module='bl_ext.user_default.meshdraw_io')
else: meshdraw_io.unregister()
