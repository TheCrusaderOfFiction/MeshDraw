"""Non-tip silhouette corners, shallow blade bevels, and partial edge cleanup."""
import sys,json
from pathlib import Path
import bpy,bmesh,numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from edge_optimizer import optimize_marked_edges
from straight_ridges import simplify_straight_ridges
folder=Path(sys.argv[sys.argv.index('--')+1]);folder.mkdir(parents=True,exist_ok=True)
def source(p):return [p[0],p[2],-p[1]]
reports=[]
for style in ('Pointed','Rounded'):
    data=bpy.data.meshes.new(style);obj=bpy.data.objects.new(style,data);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj
    bm=bmesh.new();bmesh.ops.create_cube(bm,size=2)
    edge=next(e for e in bm.edges if all(abs(v.co.x-1)<1e-6 and abs(v.co.y-1)<1e-6 for v in e.verts))
    bmesh.ops.bevel(bm,geom=[edge],offset=.006,segments=3,affect='EDGES')
    bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=6,use_grid_fill=True)
    bm.to_mesh(data);bm.free();before=len(data.vertices)
    path=folder/(style+'.json');path.write_text(json.dumps([dict(Id=style,Kind='Corner',Point=source((1,1,0)),U=source((1,0,0)),V=source((0,1,0)),Radius=.04,CornerStyle=style)]))
    report=optimize_marked_edges(obj,path)
    bm=bmesh.new();bm.from_mesh(data);assert all(e.is_manifold for e in bm.edges)
    ridge=[v for v in bm.verts if abs(v.co.x-1)<1e-6 and abs(v.co.y-1)<1e-6]
    if style=='Pointed':
        assert report['projectedCornerRidges']>0,report
        assert len(ridge)==2,('Ordinary corner still has subdivisions',len(ridge),report)
    else:assert len(ridge)==0 and report['projectedCornerRidges']==0,'Rounded corner was sharpened'
    reports.append(dict(style=style,before=before,after=len(data.vertices),**report));bm.free()
# A shallow central blade bevel: both adjoining normals pass the old .65 test.
section=[(-1,0),(0,.03),(1,0),(1,-1),(-1,-1)];bm=bmesh.new();rings=[]
for z in (-1,1):rings.append([bm.verts.new((x,y,z)) for x,y in section])
bm.faces.new(list(reversed(rings[0])));bm.faces.new(rings[1])
for i in range(5):j=(i+1)%5;bm.faces.new((rings[0][i],rings[0][j],rings[1][j],rings[1][i]))
bm.normal_update();bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=8,use_grid_fill=True)
def valid(candidate,volume):
    candidate.normal_update();return all(e.is_manifold for e in candidate.edges) and abs(candidate.calc_volume(signed=True)-volume)<1e-7
guide=dict(point=np.array((0,.03,-1)),direction=np.array((0,0,1)),normal=np.array((0,1,0)),length=2.,radius=.04)
bm,count=simplify_straight_ridges(bm,guide,lambda v:False,valid,2)
assert count>0,'Shallow bevel threshold still rejects straight ridges'
assert sum(abs(v.co.x)<1e-6 and abs(v.co.y-.03)<1e-6 for v in bm.verts)==2
bm.free()
# One intentionally protected local change must not reject the whole guide.
bm=bmesh.new();bmesh.ops.create_cube(bm,size=2);bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=8,use_grid_fill=True)
guard=next(v.co.copy() for v in bm.verts if abs(v.co.y+1)<1e-6 and abs(v.co.z+1)<1e-6 and .05<v.co.x<.2)
def locally_valid(candidate,volume):return valid(candidate,volume) and any((v.co-guard).length<1e-7 for v in candidate.verts)
guide=dict(point=np.array((-1,-1,-1)),direction=np.array((1,0,0)),normal=np.array((0,0,1)),length=2.,radius=.04)
bm,count=simplify_straight_ridges(bm,guide,lambda v:False,locally_valid,2)
assert count>0 and any((v.co-guard).length<1e-7 for v in bm.verts),'Rejected junction blocked cleanup elsewhere'
bm.free()
(folder/'body-corner-checks.json').write_text(json.dumps(dict(checks='PASS',cases=reports),indent=2))
print('PASS: non-tip corner uses two depth endpoints; rounded corner preserved; shallow bevel and partial edge cleanup pass',flush=True)
