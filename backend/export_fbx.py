"""Blender background export. Humanoid mode expects an upright T-pose, Y up in the source."""
from pathlib import Path
import os
# These small three-axis products are faster without a BLAS thread fan-out.
os.environ.setdefault('OPENBLAS_NUM_THREADS','1')
import sys
import math
import json
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from drawn_rig import load_rig, populate
from paint_materials import paint_material
from vertex_budget import read_vertex_limit,enforce_vertex_budget,render_vertex_count
from edge_optimizer import optimize_marked_edges,dissolve_flat_surfaces
from round_shading import load_round_surfaces,smooth_round_normals,capture_round_normals,restore_round_normals
from guided_collapse import finalize_guided_strips
from paint_materials import prepare_uv
from vertex_budget import preserve_sharp_shading

def create_humanoid(bone):
    bone('root', (0,0,0), (0,0,.15))
    bone('pelvis', (0,0,.86), (0,0,1.0), 'root')
    bone('spine_01', (0,0,1.0), (0,0,1.2), 'pelvis')
    bone('spine_02', (0,0,1.2), (0,0,1.45), 'spine_01')
    bone('neck', (0,0,1.45), (0,0,1.57), 'spine_02')
    bone('head', (0,0,1.57), (0,0,1.8), 'neck')
    for side, sign in [('l',1), ('r',-1)]:
        bone(f'clavicle_{side}', (0,0,1.45), (.2*sign,0,1.45), 'spine_02')
        bone(f'upperarm_{side}', (.2*sign,0,1.45), (.48*sign,0,1.45), f'clavicle_{side}')
        bone(f'lowerarm_{side}', (.48*sign,0,1.45), (.72*sign,0,1.45), f'upperarm_{side}')
        bone(f'hand_{side}', (.72*sign,0,1.45), (.85*sign,0,1.45), f'lowerarm_{side}')
        bone(f'thigh_{side}', (.12*sign,0,.86), (.12*sign,0,.46), 'pelvis')
        bone(f'calf_{side}', (.12*sign,0,.46), (.12*sign,0,.08), f'thigh_{side}')
        bone(f'foot_{side}', (.12*sign,0,.08), (.12*sign,-.2,.04), f'calf_{side}')

source, mode = sys.argv[sys.argv.index('--') + 1:]
if mode not in ('static', 'skeletal'):
    raise ValueError('Unsupported output mode')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.wm.obj_import(filepath=source, forward_axis='NEGATIVE_Z', up_axis='Y')
meshes = [obj for obj in bpy.context.selected_objects if obj.type == 'MESH']
if not meshes:
    raise ValueError('No mesh was imported')
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
mesh = bpy.context.object
mesh.name = 'MeshDrawObject'
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
flat_removed=dissolve_flat_surfaces(mesh)
edge_optimization=optimize_marked_edges(mesh,Path(source).parent/'edge_guides.json')
edge_optimization['flatSurfaceRemovedVertices']=flat_removed
low = Vector(tuple(min((mesh.matrix_world @ v.co)[i] for v in mesh.data.vertices) for i in range(3)))
high = Vector(tuple(max((mesh.matrix_world @ v.co)[i] for v in mesh.data.vertices) for i in range(3)))
height = high.z - low.z
if height <= 0:
    raise ValueError('Mesh has no height')
# Default asset height: 1.8 m. Ground the source before Unreal's centimeter conversion.
for vertex in mesh.data.vertices:
    vertex.co = (vertex.co - Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))) * (1.8 / height)
for polygon in mesh.data.polygons: polygon.use_smooth = True
rig = None
folder=Path(source).parent
job=json.loads((folder/'job.json').read_text()) if (folder/'job.json').exists() else {}
vertex_limit=read_vertex_limit(job)
round_surfaces=load_round_surfaces(folder/'round_surfaces.json',((low.x+high.x)/2,(low.y+high.y)/2,low.z),height)
render_vertices,simplification_passes=enforce_vertex_budget(mesh,vertex_limit,round_surfaces)
saved_round_normals=capture_round_normals(mesh,round_surfaces)
edge_optimization['finalStripCleanup']=finalize_guided_strips(mesh,folder/'edge_guides.json',((low.x+high.x)/2,(low.y+high.y)/2,low.z),height,round_surfaces)
edge_optimization['removedVertices']+=edge_optimization['finalStripCleanup']['removedVertices']
edge_optimization['straightVerticesRemoved']=edge_optimization.get('straightVerticesRemoved',0)+edge_optimization['finalStripCleanup']['removedVertices']
if edge_optimization['finalStripCleanup']['removedVertices']:
    preserve_sharp_shading(mesh)
    prepare_uv(mesh)
    smooth_round_normals(mesh,round_surfaces)
    edge_optimization['preservedRoundNormalLoops']=restore_round_normals(mesh,saved_round_normals)
    render_vertices=render_vertex_count(mesh)
painted_maps=paint_material(mesh,folder,job.get('textureSize',1024),prepared=True)
if render_vertex_count(mesh)>vertex_limit:raise RuntimeError('Material baking exceeded the vertex limit.')
print(f'Vertex limit: {render_vertices:,} / {vertex_limit:,} (including texture seams)',flush=True)
drawn=load_rig(folder/'skeleton.json') if mode=='skeletal' else None
if mode == 'skeletal':
    armature = bpy.data.armatures.new('MeshDrawSkeleton')
    rig = bpy.data.objects.new('MeshDrawRig', armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    mesh.select_set(False)
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    def bone(name, head, tail, parent=None):
        item = armature.edit_bones.new(name)
        item.head, item.tail = head, tail
        if parent:
            item.parent = armature.edit_bones[parent]
        return item
    if drawn:
        populate(armature,drawn,mesh)
    else:
        create_humanoid(bone)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Deterministic blended distance weights avoid heat-weight failures on AI topology.
    groups = {b.name: mesh.vertex_groups.new(name=b.name) for b in armature.bones if b.name != 'root'}
    for vertex in mesh.data.vertices:
        distances = []
        for b in armature.bones:
            if b.name == 'root': continue
            segment = b.tail_local - b.head_local
            t = max(0.0, min(1.0, (vertex.co - b.head_local).dot(segment) / segment.length_squared))
            distance = (vertex.co - (b.head_local + segment*t)).length
            distances.append((distance, b.name))
        nearest = sorted(distances)[:4]
        values = [(name, 1 / max(distance, .005)**4) for distance, name in nearest]
        total = sum(value for _, value in values)
        for name, value in values:
            groups[name].add([vertex.index], value / total, 'REPLACE')
    modifier = mesh.modifiers.new('Skeleton', 'ARMATURE')
    modifier.object = rig
    mesh.parent = rig
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
if rig: rig.select_set(True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0
destination = str(Path(source).with_name('mesh.fbx'))
bpy.ops.export_scene.fbx(filepath=destination, use_selection=True, object_types={'MESH','ARMATURE'},
    add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE', axis_forward='-Z', axis_up='Y', apply_unit_scale=True)
bpy.ops.wm.save_as_mainfile(filepath=str(Path(source).with_name('mesh.blend')))
(folder/'export-summary.json').write_text(json.dumps({'textures':painted_maps,'rig':'drawn' if drawn else ('basic humanoid' if rig else None),
    'boneCount':len(rig.data.bones) if rig else 0,'vertexLimit':vertex_limit,'renderVertices':render_vertices,
    'normalization':{'origin':[(low.x+high.x)/2,(low.y+high.y)/2,low.z],'height':height},'geometryVertices':len(mesh.data.vertices),'triangles':len(mesh.data.polygons),'simplificationPasses':simplification_passes,'edgeOptimization':edge_optimization},indent=2))
print('FBX export complete:', destination)
