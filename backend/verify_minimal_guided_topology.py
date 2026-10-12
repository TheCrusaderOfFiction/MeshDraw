"""Blender integration: one pointed vertex and endpoint-only straight ridges."""
import json,sys
from pathlib import Path
import bpy,bmesh
import numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from edge_optimizer import optimize_marked_edges

folder=Path(sys.argv[sys.argv.index('--')+1]);folder.mkdir(parents=True,exist_ok=True)
def source(p):return [p[0],p[2],-p[1]]
corner=(-1,-1,-1);end=(1,-1,-1)
def create(name,bevel=False):
    data=bpy.data.meshes.new(name);obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
    bm=bmesh.new();bmesh.ops.create_cube(bm,size=2)
    if bevel:
        vertex=min(bm.verts,key=lambda v:(v.co-Vector(corner)).length)
        bmesh.ops.bevel(bm,geom=[vertex],offset=.006,segments=1,affect='VERTICES')
    else:bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=8,use_grid_fill=True)
    bm.to_mesh(data);bm.free();return obj
from straight_ridges import simplify_straight_ridges
raw=create('raw-ridge');bm=bmesh.new();bm.from_mesh(raw.data)
g=dict(kind='Straight',point=np.array(corner),direction=np.array((1,0,0)),normal=np.array((0,0,1)),length=2.,radius=.05)
def closed(candidate,volume):
    candidate.normal_update();return all(e.is_manifold for e in candidate.edges) and abs(candidate.calc_volume(signed=True)-volume)<1e-7
bm,removed=simplify_straight_ridges(bm,g,lambda v:False,closed,2.)
assert removed>0,'High-valence ridge cleanup removed no vertices'
assert sum(abs(v.co.y+1)<1e-7 and abs(v.co.z+1)<1e-7 for v in bm.verts)==2,'Dense ridge still has intermediate vertices'
bm.free()
reports=[]
for name,style,bevel in [('dense','Pointed',False),('pointed-cap','Pointed',True),('rounded-cap','Rounded',True)]:
    mesh=create(name,bevel)
    path=folder/(name+'.json')
    guides=[dict(Kind='Corner',Point=source(corner),U=source((1,0,0)),V=source((0,0,1)),Radius=.1,CornerStyle=style)]
    if name=='dense':guides.append(dict(Kind='Straight',Point=source(corner),End=source(end),U=source((1,0,0)),V=source((0,0,1)),Normal=source((0,0,1)),Radius=.1))
    path.write_text(json.dumps(guides));report=optimize_marked_edges(mesh,path)
    bm=bmesh.new();bm.from_mesh(mesh.data)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True)>0,'Cleanup opened or inverted the surface'
    vertices=[v.co.copy() for v in bm.verts]
    at_corner=sum((v-Vector(corner)).length<1e-7 for v in vertices)
    if style=='Pointed':assert at_corner==1,(name,at_corner,report)
    if name=='dense':
        along=[v for v in vertices if abs(v.y+1)<1e-7 and abs(v.z+1)<1e-7]
        assert len(along)==2,(len(along),report)
    if name=='pointed-cap':assert report['weldedCornerVertices']>=2,report
    if style=='Rounded':assert at_corner==0 and report['weldedCornerVertices']==0,'Rounded corner was sharpened'
    reports.append(dict(case=name,vertices=len(vertices),**report));bm.free()
(folder/'checks.json').write_text(json.dumps(dict(passed=True,cases=reports),indent=2))
print('PASS: pointed caps use one welded geometric vertex; straight ridge has endpoint vertices only; rounded cap and closed topology preserved.')
