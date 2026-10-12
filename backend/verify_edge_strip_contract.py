"""Regress dense noisy edge strips that survive collinearity-only dissolving."""
import sys,json
from pathlib import Path
import numpy as np
import bpy,bmesh
sys.path.insert(0,str(Path(__file__).resolve().parent))
from guided_collapse import collapse_guided_strips
folder=Path(sys.argv[sys.argv.index('--')+1]);folder.mkdir(parents=True,exist_ok=True)
bm=bmesh.new();bmesh.ops.create_cube(bm,size=2);bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=32,use_grid_fill=True)
bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.verts.ensure_lookup_table()
for i,w in enumerate(sorted([v for v in bm.verts if abs(v.co.y+1)<1e-7 and abs(v.co.z-1)<1e-7],key=lambda v:v.co.x)):
    if abs(w.co.x)<.999:w.co.y+=.004*(1 if i%2 else -1)
bm.normal_update();before=len(bm.verts);original=sum(abs(v.co.y+1)<.005 and abs(v.co.z-1)<1e-7 for v in bm.verts)
volume=bm.calc_volume(signed=True)
g=dict(kind='Straight',point=np.array((-1,-1,1)),direction=np.array((1,0,0)),normal=np.array((0,1,0)),length=2.,radius=.04,id='noisy-edge')
def protected(w):return abs(w.co.x)>.999 and abs(w.co.y+1)<.01 and abs(w.co.z-1)<.01
def valid(candidate,old):
    candidate.normal_update();return all(e.is_manifold for e in candidate.edges) and abs(candidate.calc_volume(signed=True)-old)<abs(old)*.01
bm,removed,coverage=collapse_guided_strips(bm,[g],protected,valid,2.,folder/'round_surfaces.json')
remaining=sum(abs(v.co.y+1)<.005 and abs(v.co.z-1)<1e-7 for v in bm.verts)
assert removed>20 and remaining<original*.3,(original,remaining,removed)
assert valid(bm,volume),'Strip cleanup opened or distorted the shape'
assert sum(protected(v) for v in bm.verts)==2,'Endpoint corners were removed'
assert coverage and all(v>0 for v in coverage[0]['quarters']),'Cleanup did not cover the entire edge'
# Analytic circular regions must not be contracted by the straight-strip pass.
bm.free();bm=bmesh.new();bmesh.ops.create_cube(bm,size=2);bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=8,use_grid_fill=True);before_round=len(bm.verts)
# Protect a circular cross-section near the selected strip with a broad band.
(folder/'round_surfaces.json').write_text(json.dumps([dict(center=[0,0,0],u=[1,0,0],v=[0,0,-1],radius=1,tolerance=1)]))
bm,round_removed,_=collapse_guided_strips(bm,[g],lambda v:False,valid,2.,folder/'round_surfaces.json')
assert round_removed==0 and len(bm.verts)==before_round,'Circular region protection was ignored'
bm.free();(folder/'round_surfaces.json').unlink()
(folder/'strip-contract-checks.json').write_text(json.dumps(dict(checks='PASS',originalEdgeVertices=original,remainingEdgeVertices=remaining,removedVertices=removed,coverage=coverage),indent=2))
print('PASS: dense noisy edge strip simplified across all four quarters; endpoints, closed shape, and round regions preserved',flush=True)
