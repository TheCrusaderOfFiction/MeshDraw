import json,runpy,sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vertex_budget import render_vertex_count
folder=Path(sys.argv[sys.argv.index('--')+1]);reports=[]
for name in ('unmarked','marked','skeletal'):
    case=folder/name;mode='skeletal' if name=='skeletal' else 'static'
    sys.argv=['blender','--',str(case/'shape.obj'),mode]
    runpy.run_path(str(Path(__file__).with_name('export_fbx.py')),run_name='__main__')
    report=json.loads((case/'export-summary.json').read_text())
    if name!='unmarked':assert report['edgeOptimization']['removedVertices']+report['edgeOptimization']['flatSurfaceRemovedVertices']>100,report
    assert report['renderVertices']<=100000 and len(report['textures'])==5
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(case/'mesh.fbx'))
    mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    assert render_vertex_count(mesh)<=100000
    if mode=='skeletal':
        assert any(o.type=='ARMATURE' for o in bpy.context.scene.objects)
        assert all(1<=len(v.groups)<=4 and abs(sum(g.weight for g in v.groups)-1)<1e-4 for v in mesh.data.vertices)
    reports.append({'case':name,**report})
(folder/'checks.json').write_text(json.dumps({'passed':True,'cases':reports},indent=2))
