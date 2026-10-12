"""Validate circular ends and connected topology after FBX round-trip."""
import json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy,bmesh,numpy as np
from round_shading import load_round_surfaces
folder=Path(sys.argv[sys.argv.index('--')+1]);report=json.loads((folder/'export-summary.json').read_text())
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bm=bmesh.new();bm.from_mesh(obj.data)
assert all(e.is_manifold for e in bm.edges),'Open or non-manifold cylinder junction'
assert bm.calc_volume(signed=True)>0
remaining=set(bm.verts);parts=0
while remaining:
    parts+=1;stack=[remaining.pop()]
    while stack:
        w=stack.pop()
        for edge in w.link_edges:
            other=edge.other_vert(w)
            if other in remaining:remaining.remove(other);stack.append(other)
assert parts==1,'Blade, tube, and handle must form one connected surface'
surfaces=load_round_surfaces(folder/'round_surfaces.json',report['normalization']['origin'],report['normalization']['height'])
points=np.array([tuple(w.co) for w in bm.verts]);errors=[]
for s in surfaces:
    relative=points-s['center'];axial=relative@s['axis'];radial=np.hypot(relative@s['u'],relative@s['v'])
    for end in (s['low'],s['high']):
        rim=(abs(axial-end)<1e-5)&(radial>s['radius']*.9)
        assert rim.sum()>=16,'Missing circular end rim'
        error=float(abs(radial[rim]-s['radius']).max());errors.append(error)
        assert error<1e-5,'Cylinder end became jagged during export'
assert report['renderVertices']<=report['vertexLimit']
assert report['edgeOptimization']['finalCylinderCaps']['caps']==2
bm.free()
(folder/'cylinder-junction-check.json').write_text(json.dumps(dict(checks='PASS',connectedComponents=parts,capRadiusErrors=errors,renderVertices=report['renderVertices']),indent=2))
print('PASS: one closed connected surface, two exact circular rims, vertex budget respected',flush=True)
