"""Check exported edge bands, manifoldness, tip, and actual cylinder radii."""
import sys,json
from pathlib import Path
import numpy as np
import bpy,bmesh
sys.path.insert(0,str(Path(__file__).resolve().parent))
from round_shading import load_round_surfaces
folder=Path(sys.argv[sys.argv.index('--')+1]);before_path=Path(sys.argv[sys.argv.index('--')+2])
summary=json.loads((folder/'export-summary.json').read_text());normalization=summary['normalization'];factor=1.8/normalization['height'];origin=np.array(normalization['origin'])
convert=lambda p:np.asarray(p,dtype=float)[[0,2,1]]*np.array([1,-1,1])
guides=json.loads((folder/'edge_guides.json').read_text());counts=[];circle_comparison=[]
rounds=load_round_surfaces(folder/'round_surfaces.json',origin,normalization['height'])
def wall_errors(xyz,data):
    errors=[]
    for surface in rounds:
        d=xyz-surface['center'];axis=np.cross(surface['u'],surface['v']);axial=abs(d@axis);scope=axial>axial.max()*.6
        x=d@surface['u'];y=d@surface['v'];radius=np.hypot(x,y)
        radial=(x[:,None]*surface['u']+y[:,None]*surface['v'])/np.maximum(radius[:,None],1e-12)
        for face in data.polygons:
            ids=np.array(face.vertices);aligned=radial[ids]@np.array(tuple(face.normal))>.9
            near=(abs(radius[ids]-surface['radius'])<surface['tolerance'])&scope[ids]
            errors.extend(abs(radius[ids[aligned&near]]-surface['radius']).tolist())
    return errors
for path in (before_path,folder/'mesh.fbx'):
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(path))
    mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH');xyz=np.array([tuple(w.co) for w in mesh.data.vertices]);bands={}
    for g in guides:
        if g['Kind']!='Straight':continue
        p=(convert(g['Point'])-origin)*factor;direction=convert(g['End'])-convert(g['Point']);length=np.linalg.norm(direction)*factor;direction/=np.linalg.norm(direction);n=convert(g['Normal']);d=xyz-p;along=d@direction
        near=(along>length*.1)&(along<length*.9)&(abs(d@n)<g['Radius']*factor*.35)
        if 'SectionNormal' in g:near &= abs(d@convert(g['SectionNormal']))<g['Radius']*factor
        bands[g['Id']]=int(near.sum())
    counts.append(bands);circle_comparison.append(wall_errors(xyz,mesh.data))
bm=bmesh.new();bm.from_mesh(mesh.data);assert all(e.is_manifold for e in bm.edges),'Final FBX is not closed';bm.free()
maximum=xyz[:,2].max();assert int((maximum-xyz[:,2]<1e-6).sum())==1,'Tip gained extra vertices'
before_errors,errors=circle_comparison
print('Cylinder wall radius deviation, before/after:',max(before_errors),max(errors),flush=True)
assert errors and before_errors and max(errors)<=max(before_errors)+1e-5,'Cleanup worsened the circular wall geometry'
total_before=sum(counts[0].values());total_after=sum(counts[1].values())
assert total_after<total_before*.8,('Exported edge bands did not become substantially simpler',total_before,total_after)
report=dict(checks='PASS',beforeEdgeBandSamples=total_before,afterEdgeBandSamples=total_after,circleWallMaximumRadiusError=max(errors),geometryVertices=len(xyz),tipVertices=1)
(folder/'exported-strip-checks.json').write_text(json.dumps(report,indent=2));print('PASS: exported edge strips reduced; closed mesh, one tip, and circular walls preserved',report,flush=True)
