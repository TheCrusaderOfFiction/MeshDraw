"""Reduce pointed drawing corners through depth without collapsing thickness."""
import numpy as np
import bmesh
from mathutils import Vector


def clean_corner_ridges(bm,corners,rounded,valid,scale,tolerance,protected=None):
    removed=0;processed=set();ridges=[]
    for g in corners:
        if g.get('corner_group') or 'section_normal' in g:continue
        bm.verts.ensure_lookup_table();bm.verts.index_update()
        xyz=np.array([tuple(v.co) for v in bm.verts]);relative=xyz-g['point']
        distance=np.hypot(relative@g['u'],relative@g['v'])
        radius=min(g['radius'],max(scale/192*1.5,float(distance.min())*1.5))
        near=distance<=radius
        if protected is not None:near &= np.array([not protected(w) for w in bm.verts])
        for r in rounded:
            d=xyz-r['point'];near &= (d@r['u'])**2+(d@r['v'])**2>r['radius']**2
        indices=np.flatnonzero(near)
        if len(indices)<2:continue
        candidate=bm.copy();candidate.verts.ensure_lookup_table();volume=bm.calc_volume(signed=True);before=len(bm.verts)
        selected=[candidate.verts[int(i)] for i in indices]
        for w in selected:
            p=np.array(tuple(w.co));d=p-g['point']
            w.co=Vector(p-g['u']*(d@g['u'])-g['v']*(d@g['v']))
        bmesh.ops.remove_doubles(candidate,verts=selected,dist=tolerance)
        bmesh.ops.dissolve_degenerate(candidate,dist=tolerance,edges=list(candidate.edges))
        if not valid(candidate,volume):candidate.free();continue
        normal=np.cross(g['u'],g['v']);normal/=np.linalg.norm(normal)
        depth=relative[indices]@normal;start=g['point']+normal*depth.min();length=float(np.ptp(depth))
        if length>tolerance:
            # An ordinary silhouette corner describes a ridge through depth,
            # rather than a single vertex at the drawing plane's center.
            ridges.append(dict(kind='Straight',point=start,direction=normal,length=length,normal=g['u'],radius=tolerance*4,id=g.get('id')))
        removed+=max(0,before-len(candidate.verts));processed.add(g.get('id'));bm.free();bm=candidate
    return bm,removed,processed,ridges
