"""Rebuild exposed cylinder ends with a circular rim and a single planar cap."""
import numpy as np
import bmesh
from mathutils import Vector


def rebuild_cylinder_caps(mesh,surfaces):
    report=dict(caps=0,rimVertices=0)
    for s in surfaces:
        center=np.asarray(s['center']);u=np.asarray(s['u']);v=np.asarray(s['v']);axis=np.asarray(s.get('axis',np.cross(u,v)),dtype=float);axis/=np.linalg.norm(axis)
        bm=bmesh.new();bm.from_mesh(mesh.data);bm.normal_update();xyz=np.array([tuple(w.co) for w in bm.verts]);relative=xyz-center
        radius=np.hypot(relative@u,relative@v);near=radius<=s['radius']+s['tolerance']
        if not near.any():bm.free();continue
        extent=relative[near]@axis
        low=s.get('low',float(extent.min()));high=s.get('high',float(extent.max()))
        # Legacy circular metadata may describe a sphere rather than a tube.
        if 'low' not in s and (len(surfaces)!=1 or high-low<s['radius']*2.25):bm.free();continue
        band=min(s['tolerance']*1.5,(high-low)*.08)
        for end,sign in [(low,-1),(high,1)]:
            candidate=bm.copy();volume=bm.calc_volume(signed=True);cut=end-sign*band;plane=center+axis*cut
            bmesh.ops.bisect_plane(candidate,geom=list(candidate.verts)+list(candidate.edges)+list(candidate.faces),dist=1e-7,plane_co=Vector(plane),plane_no=Vector(axis),clear_inner=False,clear_outer=False)
            cap={f for f in candidate.faces if sign*((np.array(tuple(f.calc_center_median()))-center)@axis-cut)>1e-7}
            if not cap or any(np.hypot((np.array(tuple(w.co))-center)@u,(np.array(tuple(w.co))-center)@v)>s['radius']+s['tolerance']*1.5 for f in cap for w in f.verts):candidate.free();continue
            boundary={};simple=True
            for edge in candidate.edges:
                if len(edge.link_faces)==2 and sum(f in cap for f in edge.link_faces)==1:
                    outside=next(f for f in edge.link_faces if f not in cap);loop=next(l for l in outside.loops if l.edge==edge)
                    a,b=loop.link_loop_next.vert,loop.vert
                    if a in boundary:simple=False
                    boundary[a]=b
            if len(boundary)<8 or not simple:candidate.free();continue
            ordered=[];first=next(iter(boundary));current=first
            while current not in ordered:
                ordered.append(current)
                if current not in boundary:simple=False;break
                current=boundary[current]
            if not simple or current!=first or len(ordered)!=len(boundary):candidate.free();continue
            bmesh.ops.delete(candidate,geom=list(cap),context='FACES_ONLY')
            wire=[e for e in candidate.edges if not e.link_faces]
            if wire:bmesh.ops.delete(candidate,geom=wire,context='EDGES')
            unused=[w for w in candidate.verts if not w.link_faces and w not in boundary]
            if unused:bmesh.ops.delete(candidate,geom=unused,context='VERTS')
            for w in ordered:
                d=np.array(tuple(w.co))-center;x=d@u;y=d@v;r=np.hypot(x,y)
                if r<1e-9:simple=False;break
                w.co=Vector(center+axis*end+s['radius']*(u*x+v*y)/r)
            if not simple:candidate.free();continue
            face=candidate.faces.new(ordered);face.smooth=False;candidate.normal_update()
            for edge in face.edges:edge.smooth=False
            if face.normal.dot(Vector(axis*sign))<.99 or not all(e.is_manifold for e in candidate.edges) or candidate.calc_volume(signed=True)<=0 or abs(candidate.calc_volume(signed=True)-volume)>max(abs(volume)*.05,1e-8):candidate.free();continue
            report['caps']+=1;report['rimVertices']+=len(ordered);bm.free();bm=candidate
        bm.to_mesh(mesh.data);bm.free();mesh.data.update()
    print('Circular end caps:',report,flush=True)
    return report


def finish_cylinder_caps(mesh,surfaces,limit,count):
    """Keep rebuilt rims inside the user's limit, including their UV seams."""
    from vertex_budget import enforce_vertex_budget,render_vertex_count,preserve_sharp_shading
    from paint_materials import prepare_uv
    from round_shading import smooth_round_normals
    passes=0
    for attempt in range(5):
        report=rebuild_cylinder_caps(mesh,surfaces)
        if report['caps']:
            preserve_sharp_shading(mesh);prepare_uv(mesh);smooth_round_normals(mesh,surfaces)
            count=render_vertex_count(mesh)
        if count<=limit:return report,count,passes
        # Reserve room for the end-rim seams before rebuilding again.
        count,extra=enforce_vertex_budget(mesh,max(64,int(limit*(.8**(attempt+1)))),surfaces)
        passes+=extra
    raise ValueError('Could not preserve circular end rims within the vertex limit. Increase Maximum vertices.')
