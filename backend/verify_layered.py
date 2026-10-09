"""Blender roundtrip verification for painted materials and the drawn skeleton."""
from pathlib import Path
import sys
import json
import bpy

folder=Path(sys.argv[sys.argv.index('--')+1])
mode=sys.argv[sys.argv.index('--')+2]
bpy.ops.wm.open_mainfile(filepath=str(folder/'mesh.blend'))
mesh=next(obj for obj in bpy.context.scene.objects if obj.type=='MESH')
assert mesh.data.uv_layers.active and len(mesh.data.uv_layers.active.data)>0
material=mesh.data.materials[0]
nodes=material.node_tree.nodes
images={node.label:node.image for node in nodes if node.type=='TEX_IMAGE'}
assert set(images)=={'basecolor','roughness','metallic','emission','height'}
assert any(node.type=='BUMP' for node in nodes)
for image in images.values():
    assert image.size[0]==512 and image.size[1]==512
    assert Path(bpy.path.abspath(image.filepath)).exists()
shader=nodes.get('Principled BSDF')
for input_name in ('Base Color','Roughness','Metallic','Emission Color','Normal'):
    assert shader.inputs[input_name].is_linked,input_name
custom=json.loads((folder/'skeleton.json').read_text())
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
mesh=next(obj for obj in bpy.context.scene.objects if obj.type=='MESH')
rigs=[obj for obj in bpy.context.scene.objects if obj.type=='ARMATURE']
assert bool(rigs)==(mode=='skeletal')
if rigs:
    bones=rigs[0].data.bones
    assert set(bones.keys())=={'root','drawn_spine','drawn_branch'}
    assert bones['drawn_branch'].parent.name=='drawn_spine'
    for vertex in mesh.data.vertices:
        assert 1<=len(vertex.groups)<=4
        assert abs(sum(group.weight for group in vertex.groups)-1)<1e-4
assert mesh.data.uv_layers.active
print('PASS: all five material maps and shader connections, UVs, FBX roundtrip, drawn bone hierarchy, and normalized skin weights:',mode)
