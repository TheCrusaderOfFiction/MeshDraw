import json,runpy,sys
from pathlib import Path
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vertex_budget import render_vertex_count
case=Path(sys.argv[sys.argv.index('--')+1]);sys.argv=['blender','--',str(case/'shape.obj'),'static']
runpy.run_path(str(Path(__file__).with_name('export_fbx.py')),run_name='__main__')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(case/'mesh.fbx'));mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
positions=np.array([tuple(v.co) for v in mesh.data.vertices]);positions-=(positions.min(axis=0)+positions.max(axis=0))/2
ring=positions[abs(positions[:,1])<np.ptp(positions[:,2])*.15]
radius=np.hypot(ring[:,0],ring[:,2]);variation=float(np.std(radius)/np.mean(radius))
assert variation<.003,variation
assert render_vertex_count(mesh)<=100000
assert all(p.use_smooth for p in mesh.data.polygons)
(case/'checks.json').write_text(json.dumps({'passed':True,'radialVariation':variation,'vertices':render_vertex_count(mesh)},indent=2))
print('PASS: circular FBX radius variation below 0.3%, smooth shading, vertex cap.',flush=True)
