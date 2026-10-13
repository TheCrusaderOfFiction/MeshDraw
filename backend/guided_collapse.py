"""Contract redundant triangles along annotated straight strips, retaining shape."""
import json
import numpy as np
import bmesh


def contract_vertex(bm,w,g,error):
    p=np.array(tuple(w.co));direction=g['direction'];left=[];right=[]
    for edge in w.link_edges:
        other=edge.other_vert(w);d=np.array(tuple(other.co))-p;t=float(d@direction)
        if abs(t)<1e-9 or np.linalg.norm(d-direction*t)>error*3:continue
        (left if t<0 else right).append(other)
    best=None
    for a in left:
        for b in right:
            pa=np.array(tuple(a.co));pb=np.array(tuple(b.co));d=pb-pa
            t=float((p-pa)@d/max(d@d,1e-20))
            deviation=np.linalg.norm(p-pa-np.clip(t,0,1)*d)
            if not 0<t<1 or deviation>error:continue
            score=deviation+np.linalg.norm(d)*1e-5
            if best is None or score<best[0]:best=(score,a,b)
    if best is None:return False
    _,a,b=best;target=min((a,b),key=lambda v:(v.co-w.co).length_squared)
    # Reject folds in the surviving fan before changing the topology.
    for face in w.link_faces:
        if target in face.verts:continue
        points=np.array([tuple(target.co if v==w else v.co) for v in face.verts]);q=points-points[0]
        area=np.sum(np.cross(q,np.roll(q,-1,axis=0)),axis=0)
        if np.linalg.norm(area)<1e-12 or area@np.array(tuple(face.normal))<=0:return False
    coordinate=target.co.copy()
    bmesh.ops.pointmerge(bm,verts=[w,target],merge_co=coordinate)
    return True


def collapse_guided_strips(bm,guides,protected,valid,scale,round_path,round_surfaces=None):
    convert=lambda p:np.asarray(p,dtype=float)[[0,2,1]]*np.array([1,-1,1])
    rounds=[]
    if round_surfaces is not None:
        rounds=[(s['center'],s['u'],s['v'],s['radius'],s['tolerance'],s.get('kind','cylinder')) for s in round_surfaces]
    elif round_path.exists():
        for s in json.loads(round_path.read_text()):rounds.append((convert(s['center']),convert(s['u']),convert(s['v']),s['radius'],s['tolerance'],s.get('kind','cylinder')))
    # Preserve the complete cylinder disk, including cap interiors and any
    # triangle joining it to the blade. A radius-only shell missed those faces.
    protected_round_points=set()
    if rounds:
        from round_shading import round_protected_points
        protected_round_points=round_protected_points(bm,round_surfaces if round_surfaces is not None else [dict(center=c,u=u,v=v,radius=r,tolerance=m,kind=k) for c,u,v,r,m,k in rounds])
    corners=[g for g in guides if g['kind']=='Corner']
    tolerance=max(scale*1e-6,1e-8)
    corner_points=np.array([g['point'] for g in corners]).reshape(-1,3)
    corner_u=np.array([g['u'] for g in corners]).reshape(-1,3)
    corner_v=np.array([g['v'] for g in corners]).reshape(-1,3)
    corner_radius=np.array([g['radius'] if g.get('corner_style')=='Rounded' else tolerance for g in corners])
    def guard(w):
        if protected(w):return True
        p=np.array(tuple(w.co))
        if tuple(w.co) in protected_round_points:return True
        if len(corners):
            d=p-corner_points;x=np.einsum('ij,ij->i',d,corner_u);y=np.einsum('ij,ij->i',d,corner_v)
            if np.any(x*x+y*y<=corner_radius*corner_radius):return True
        for center,u,v,radius,margin,kind in rounds:
            d=p-center
            if (np.linalg.norm(d) if kind=='sphere' else np.hypot(d@u,d@v))<=radius+margin:return True
        return False
    removed=0;coverage=[]
    for g in guides:
        if g['kind']!='Straight':continue
        error=max(scale/192*.6,g['radius']*.2)
        positions=[]
        for _ in range(3):
            bm.normal_update();bm.verts.ensure_lookup_table();bm.verts.index_update();indices=[]
            for w in bm.verts:
                d=np.array(tuple(w.co))-g['point'];along=float(d@g['direction'])
                if not tolerance<along<g['length']-tolerance or abs(d@g['normal'])>g['radius']:continue
                if 'section_normal' in g and abs(d@g['section_normal'])>g['radius']:continue
                if not guard(w):indices.append(w.index)
            if not indices:break
            # Independent small batches retain successful contractions near
            # difficult corners, instead of discarding an entire long strip.
            tag=bm.verts.layers.int.new('_guided_contract_id')
            for w in bm.verts:w[tag]=w.index
            pass_removed=0
            for start in range(0,len(indices),48):
                candidate=bm.copy();layer=candidate.verts.layers.int.get('_guided_contract_id');lookup={w[layer]:w for w in candidate.verts};before=len(candidate.verts);volume=bm.calc_volume(signed=True);progress=[]
                for identity in indices[start:start+48]:
                    w=lookup.get(identity)
                    if w is None or not w.is_valid or guard(w):continue
                    t=float((np.array(tuple(w.co))-g['point'])@g['direction'])/g['length']
                    if contract_vertex(candidate,w,g,error):progress.append(t)
                if progress:
                    bmesh.ops.dissolve_degenerate(candidate,dist=tolerance,edges=list(candidate.edges))
                if progress and valid(candidate,volume):
                    pass_removed+=before-len(candidate.verts);positions.extend(progress);bm.free();bm=candidate
                else:candidate.free()
            bm.verts.layers.int.remove(bm.verts.layers.int.get('_guided_contract_id'))
            removed+=pass_removed
            if not pass_removed:break
        if positions:coverage.append(dict(id=g.get('id'),removedVertices=len(positions),quarters=np.histogram(positions,bins=np.linspace(0,1,5))[0].tolist()))
    return bm,removed,coverage


def finalize_guided_strips(mesh,path,origin,height,round_surfaces):
    """Run after shape reduction so its triangulation cannot undo the cleanup."""
    if not path.exists():return dict(removedVertices=0,coverage=[])
    print('Cleaning final exported straight-edge strips...',flush=True)
    factor=1.8/height;origin=np.asarray(origin)
    convert=lambda p:np.asarray(p,dtype=float)[[0,2,1]]*np.array([1,-1,1])
    guides=[]
    for mark in json.loads(path.read_text(encoding='utf-8')):
        g=dict(kind=mark['Kind'],point=(convert(mark['Point'])-origin)*factor,u=convert(mark['U']),v=convert(mark['V']),radius=mark['Radius']*factor,id=mark['Id'],corner_style=mark.get('CornerStyle','Pointed'))
        if 'SectionNormal' in mark:g['section_normal']=convert(mark['SectionNormal'])
        if g['kind']=='Straight':
            d=convert(mark['End'])-convert(mark['Point']);length=np.linalg.norm(d)*factor
            g.update(direction=d/np.linalg.norm(d),length=length,normal=convert(mark['Normal']))
        guides.append(g)
    bm=bmesh.new();bm.from_mesh(mesh.data);bm.normal_update();before=len(bm.verts);tiny=sum(f.calc_area()<=1e-12 for f in bm.faces)
    fixed=[g['point'] for g in guides if g['kind']=='Corner']
    for g in guides:
        if g['kind']=='Straight':fixed.extend([g['point'],g['point']+g['direction']*g['length']])
    fixed=np.asarray(fixed);maximum=max(w.co.z for w in bm.verts)
    def protected(w):
        p=np.array(tuple(w.co))
        if maximum-p[2]<1e-7:return True
        if len(fixed) and np.min(np.linalg.norm(fixed-p,axis=1))<1e-6:return True
        return False
    def valid(candidate,volume):
        candidate.normal_update()
        return all(e.is_manifold for e in candidate.edges) and sum(f.calc_area()<=1e-12 for f in candidate.faces)<=tiny and candidate.calc_volume(signed=True)>0 and abs(candidate.calc_volume(signed=True)-volume)<=max(abs(volume)*.01,1e-9)
    bm,removed,coverage=collapse_guided_strips(bm,guides,protected,valid,1.8,path.parent/'round_surfaces.json',round_surfaces)
    bm.to_mesh(mesh.data);bm.free();mesh.data.update()
    if removed:mesh.data.normals_split_custom_set([(0,0,0)]*len(mesh.data.loops))
    report=dict(removedVertices=before-len(mesh.data.vertices),coverage=coverage)
    print('Final exported edge-strip cleanup:',report,flush=True)
    return report
