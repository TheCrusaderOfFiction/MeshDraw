"""Blender acceptance checks: budgets survive seam splitting and FBX import."""
import json
import runpy
import sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vertex_budget import render_vertex_count,read_vertex_limit

folder=Path(sys.argv[sys.argv.index('--')+1]);reports=[]
for mode,limit in [('static',256),('static',2000),('static',3000),('static',100000),('skeletal',1000)]:
    case=folder/f'{mode}-{limit}';sys.argv=['blender','--',str(case/'shape.obj'),mode]
    runpy.run_path(str(Path(__file__).with_name('export_fbx.py')),run_name='__main__')
    summary=json.loads((case/'export-summary.json').read_text())
    assert summary['renderVertices']<=limit and summary['geometryVertices']<=limit
    assert len(summary['textures'])==5
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(case/'mesh.fbx'))
    mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    actual=render_vertex_count(mesh)
    assert actual<=limit,f'{mode} FBX has {actual} render vertices above {limit}'
    assert all(p.use_smooth for p in mesh.data.polygons)
    if mode=='skeletal':
        assert any(o.type=='ARMATURE' for o in bpy.context.scene.objects)
        for vertex in mesh.data.vertices:
            assert 1<=len(vertex.groups)<=4
            assert abs(sum(g.weight for g in vertex.groups)-1)<1e-4
    if limit in (3000,100000):assert summary['simplificationPasses']==0,'Mesh below budget was simplified'
    reports.append({'mode':mode,'limit':limit,'roundtripRenderVertices':actual,'geometryVertices':len(mesh.data.vertices),'passes':summary['simplificationPasses']})
for bad in (0,255,100001,True,1000.5,'5000'):
    try:read_vertex_limit({'vertexLimit':bad});raise AssertionError('Invalid limit accepted')
    except ValueError:pass
(folder/'checks.json').write_text(json.dumps({'passed':True,'cases':reports},indent=2),encoding='utf-8')
print('PASS: static/skeletal budgets, actual FBX seam vertices, five maps, smooth shading, skin weights, no simplification below budget, invalid values rejected.',flush=True)
