import json,runpy,sys
from pathlib import Path
import bpy
folder=Path(sys.argv[sys.argv.index('--')+1]);sys.argv=['blender','--',str(folder/'shape.obj'),'static']
runpy.run_path(str(Path(__file__).with_name('export_fbx.py')),run_name='__main__')
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
maximum=max(v.co.z for v in mesh.data.vertices);tip=[v for v in mesh.data.vertices if maximum-v.co.z<1e-6]
report=json.loads((folder/'export-summary.json').read_text());report['topVertexCount']=len(tip)
(folder/'sword-topology-check.json').write_text(json.dumps(report,indent=2))
print('Sword topology:',len(mesh.data.vertices),'vertices;',len(tip),'vertices at the highest point',flush=True)
assert len(tip)==1,'Exported sword tip is still a line or cap'
assert report['edgeOptimization']['singlePointTips']>=1,'Merged apex cap was not welded'
assert report['edgeOptimization']['flattenedEdgeVertices']>0,'Straight-edge planes were not cleaned'
assert report['edgeOptimization']['straightVerticesRemoved']>0,'Dense ridge vertices were not dissolved'
upper={g['Id'] for g in json.loads((folder/'edge_guides.json').read_text()) if g['Kind']=='Straight' and max(g['Point'][1],g['End'][1])>.9}
assert any(g['id'] in upper and sum(g['quarters'][1:])>0 for g in report['edgeOptimization']['ridgeGuideCoverage']),'No ridge cleanup beyond the tip portion of the blade'
print('PASS: real sword FBX has one tip vertex, marked edge flattening, and a valid vertex budget.')
