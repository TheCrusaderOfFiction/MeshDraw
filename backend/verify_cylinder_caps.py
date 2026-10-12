"""Check circular cap reconstruction without changing the tube wall."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy,bmesh,numpy as np
from cylinder_caps import rebuild_cylinder_caps,finish_cylinder_caps
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_cylinder_add(vertices=96,radius=.3,depth=2)
mesh=bpy.context.object
# Jagged end rim reproduces the axial/radial sawtooth seen in the export.
for w in mesh.data.vertices:
    theta=np.arctan2(w.co.y,w.co.x)
    w.co.x*=1+.01*np.sin(theta*13);w.co.y*=1+.01*np.sin(theta*13)
    w.co.z+=.008*np.sin(theta*17)
s=dict(center=np.zeros(3),u=np.array([1,0,0]),v=np.array([0,1,0]),axis=np.array([0,0,1]),radius=.3,tolerance=.02,low=-1,high=1)
report=rebuild_cylinder_caps(mesh,[s]);assert report['caps']==2,report
bm=bmesh.new();bm.from_mesh(mesh.data)
assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True)>0
for w in bm.verts:
    if abs(w.co.z)>.99:
        assert abs(abs(w.co.z)-1)<1e-6
        assert abs(np.hypot(w.co.x,w.co.y)-.3)<1e-6
bm.free()
from vertex_budget import enforce_vertex_budget,render_vertex_count,preserve_sharp_shading
from paint_materials import prepare_uv
from round_shading import smooth_round_normals
count,_=enforce_vertex_budget(mesh,256,[s])
caps,count,_=finish_cylinder_caps(mesh,[s],256,count)
assert count<=256 and caps['caps']==2
print('Low budget cap vertex count:',count,flush=True)
bpy.ops.mesh.primitive_uv_sphere_add(radius=.3)
sphere=bpy.context.object
legacy={k:v for k,v in s.items() if k not in ('axis','low','high')}
assert rebuild_cylinder_caps(sphere,[legacy])['caps']==0
print('PASS: two circular planar ends, manifold tube, sphere preserved',flush=True)
