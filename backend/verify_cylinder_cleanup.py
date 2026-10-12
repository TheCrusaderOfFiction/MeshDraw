"""Straight-strip cleanup must preserve cylinder caps, walls, and split normals."""
import sys,json
from pathlib import Path
import bpy,bmesh,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from guided_collapse import collapse_guided_strips
from round_shading import smooth_round_normals,capture_round_normals,restore_round_normals
from paint_materials import prepare_uv
from vertex_budget import preserve_sharp_shading
folder=Path(sys.argv[sys.argv.index('--')+1]);folder.mkdir(parents=True,exist_ok=True)
data=bpy.data.meshes.new('cylinder-cap-regression');obj=bpy.data.objects.new('cylinder-cap-regression',data);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
bm=bmesh.new();bmesh.ops.create_cone(bm,cap_ends=True,cap_tris=True,segments=48,radius1=.15,radius2=.15,depth=2)
bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=2,use_grid_fill=True)
cube=bmesh.ops.create_cube(bm,size=2)['verts'];bmesh.ops.translate(bm,verts=cube,vec=(2,2,0));bmesh.ops.subdivide_edges(bm,edges=[e for e in bm.edges if all(w in cube for w in e.verts)],cuts=8,use_grid_fill=True)
bm.to_mesh(data);bm.free()
for face in data.polygons:face.use_smooth=True
rounds=[dict(center=np.array((0,0,0)),u=np.array((1,0,0)),v=np.array((0,1,0)),radius=.15,tolerance=.004)]
prepare_uv(obj);preserve_sharp_shading(obj);smooth_round_normals(obj,rounds);saved=capture_round_normals(obj,rounds)
before_faces={tuple(sorted(tuple(data.vertices[i].co) for i in f.vertices)) for f in data.polygons if any(np.hypot(data.vertices[i].co.x,data.vertices[i].co.y)<.2 for i in f.vertices)}
before_points={tuple(w.co) for w in data.vertices if np.hypot(w.co.x,w.co.y)<.2}
# Also exercise the earlier straight-plane and pointed-corner corrections.
from edge_optimizer import optimize_marked_edges
source=lambda p:[p[0],p[2],-p[1]]
(folder/'round_surfaces.json').write_text(json.dumps([dict(center=[0,0,0],u=source((1,0,0)),v=source((0,1,0)),radius=.15,tolerance=.004)]))
marks=[dict(Id='wall',Kind='Straight',Point=source((0,.15,-1)),End=source((0,.15,1)),U=source((0,0,1)),V=source((0,1,0)),Normal=source((0,1,0)),Radius=.04),dict(Id='cap',Kind='Corner',Point=source((0,0,1)),U=source((1,0,0)),V=source((0,1,0)),Radius=.04,CornerStyle='Pointed')]
(folder/'edge_guides.json').write_text(json.dumps(marks))
early_report=optimize_marked_edges(obj,folder/'edge_guides.json')
assert before_points=={tuple(w.co) for w in data.vertices if np.hypot(w.co.x,w.co.y)<.2},'Earlier corner/straight-plane pass moved cylinder vertices'
bm=bmesh.new();bm.from_mesh(data);bm.normal_update()
guides=[dict(kind='Straight',point=np.array((0,-.15,1)),direction=np.array((0,1,0)),normal=np.array((1,0,0)),length=.3,radius=.1,id='cap'),dict(kind='Straight',point=np.array((1,1,1)),direction=np.array((1,0,0)),normal=np.array((0,1,0)),length=2.,radius=.04,id='blade')]
def valid(candidate,volume):
    candidate.normal_update();return all(e.is_manifold for e in candidate.edges) and abs(candidate.calc_volume(signed=True)-volume)<=abs(volume)*.01
bm,removed,_=collapse_guided_strips(bm,guides,lambda w:False,valid,4,folder/'none.json',rounds)
assert removed+early_report['removedVertices']>0,'Blade cleanup was disabled'
bm.to_mesh(data);bm.free();data.update();data.normals_split_custom_set([(0,0,0)]*len(data.loops))
preserve_sharp_shading(obj);prepare_uv(obj);smooth_round_normals(obj,rounds);restored=restore_round_normals(obj,saved)
after_faces={tuple(sorted(tuple(data.vertices[i].co) for i in f.vertices)) for f in data.polygons if any(np.hypot(data.vertices[i].co.x,data.vertices[i].co.y)<.2 for i in f.vertices)}
after_points={tuple(w.co) for w in data.vertices if np.hypot(w.co.x,w.co.y)<.2}
assert before_points==after_points and before_faces==after_faces,'Cylinder cap or wall topology changed'
assert restored==sum(len(values) for values in saved.values()),'Protected circular face normals were lost'
after=capture_round_normals(obj,rounds)
for face,values in saved.items():
    for point,normal in values.items():assert np.linalg.norm(np.array(after[face][point])-normal)<1e-5,'Cylinder gained a shading seam'
(folder/'cylinder-cleanup-checks.json').write_text(json.dumps(dict(checks='PASS',removedBladeVertices=removed+early_report['removedVertices'],preservedRoundNormalLoops=restored),indent=2))
print('PASS: cylinder cap/wall faces and split normals unchanged; blade vertices reduced',flush=True)
