import sys
import bpy
path, mode = sys.argv[sys.argv.index('--')+1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=path)
meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
rigs = [obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE']
assert len(meshes) == 1
assert (len(rigs) == 1) == (mode == 'skeletal')
if rigs:
    assert 'root' in rigs[0].data.bones and len(rigs[0].data.bones) == 20
    assert any(mod.type == 'ARMATURE' for mod in meshes[0].modifiers)
    for vertex in meshes[0].data.vertices:
        assert 1 <= len(vertex.groups) <= 4
        assert abs(sum(group.weight for group in vertex.groups) - 1) < 1e-4
print('FBX roundtrip passed:', mode)
