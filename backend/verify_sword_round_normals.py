import sys,json
from pathlib import Path
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from round_shading import load_round_surfaces
folder=Path(sys.argv[sys.argv.index('--')+1])
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.wm.obj_import(filepath=str(folder/'shape.obj'),forward_axis='NEGATIVE_Z',up_axis='Y')
source=bpy.context.object
points=np.array([tuple(source.matrix_world@v.co) for v in source.data.vertices]);low=points.min(axis=0);high=points.max(axis=0)
surfaces=load_round_surfaces(folder/'round_surfaces.json',[(low[0]+high[0])/2,(low[1]+high[1])/2,low[2]],high[2]-low[2])
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH');data=mesh.data
points=np.array([tuple(v.co) for v in data.vertices]);indices=np.array([loop.vertex_index for loop in data.loops])
normals=np.array([tuple(n.vector) for n in data.corner_normals]);face_normals=np.empty_like(normals)
for polygon in data.polygons:face_normals[polygon.loop_start:polygon.loop_start+polygon.loop_total]=tuple(polygon.normal)
values=[];radius_errors=[]
for s in surfaces:
    relative=points-s['center'];x=relative@s['u'];y=relative@s['v'];radius=np.hypot(x,y)
    radial=(x[:,None]*s['u']+y[:,None]*s['v'])/np.maximum(radius[:,None],1e-12)
    use=(abs(radius[indices]-s['radius'])<s['tolerance'])&(np.einsum('ij,ij->i',face_normals,radial[indices])>.9)
    radius_errors.extend((abs(radius[indices[use]]-s['radius'])/s['radius']).tolist())
    values.extend(np.einsum('ij,ij->i',normals[use],radial[indices[use]]).tolist())
assert len(values)>100
assert np.percentile(values,1)>.9999,'Round shading did not survive actual sword FBX'
report={'passed':True,'circularFaceCorners':len(values),'normalAlignmentMinimum':float(min(values)),'radiusError95Percent':float(np.percentile(radius_errors,95)*100)}
(folder/'round-shading-checks.json').write_text(json.dumps(report,indent=2));print('PASS:',report)
