"""Remove redundant ridge vertices without merging the two adjoining surfaces."""
import bmesh
import math
import time
import numpy as np


def dissolve_ridge_vertex(bm,vertex,normal):
    faces=list(vertex.link_faces)
    if len(faces)<2:return False
    side={f:abs(float(np.dot(tuple(f.normal),normal)))>.65 for f in faces}
    ridge=[e for e in vertex.link_edges if len(e.link_faces)==2 and e.link_faces[0] in side and e.link_faces[1] in side and side[e.link_faces[0]]!=side[e.link_faces[1]] and e.link_faces[0].normal.dot(e.link_faces[1].normal)<math.cos(math.radians(15))]
    if len(ridge)!=2:
        # A shallow blade bevel can put both adjoining faces on the same
        # side of the guide's normal threshold. Split by the actual surfaces.
        first=max(faces,key=lambda f:max(1-f.normal.dot(other.normal) for other in faces))
        second=min(faces,key=lambda f:first.normal.dot(f.normal))
        if first.normal.dot(second.normal)>math.cos(math.radians(2)):return False
        n1,n2=first.normal.copy(),second.normal.copy()
        side={f:f.normal.dot(n1)>=f.normal.dot(n2) for f in faces}
        ridge=[e for e in vertex.link_edges if len(e.link_faces)==2 and all(f in side for f in e.link_faces) and side[e.link_faces[0]]!=side[e.link_faces[1]]]
        if len(ridge)!=2:return False
    a,b=[e.other_vert(vertex) for e in ridge];left=a.co-vertex.co;right=b.co-vertex.co
    if left.length<1e-8 or right.length<1e-8 or left.normalized().dot(right.normalized())>-.995:return False
    polygons=[]
    for label in (False,True):
        patch={f for f in faces if side[f]==label}
        if not patch:return False
        boundary={}
        for face in patch:
            for loop in face.loops:
                if sum(f in patch for f in loop.edge.link_faces)==1:
                    if loop.vert in boundary:return False
                    boundary[loop.vert]=loop.link_loop_next.vert
        if vertex not in boundary:return False
        polygon=[];current=next(iter(boundary));start=current
        while current not in polygon:
            polygon.append(current)
            if current not in boundary:return False
            current=boundary[current]
        if current!=start or len(polygon)!=len(boundary):return False
        polygon=[v for v in polygon if v!=vertex]
        if len(polygon)<3:return False
        polygons.append(polygon)
    bmesh.ops.delete(bm,geom=faces,context='FACES_ONLY')
    for polygon in polygons:bm.faces.new(polygon).normal_update()
    wire=[e for e in vertex.link_edges if not e.link_faces]
    if wire:bmesh.ops.delete(bm,geom=wire,context='EDGES')
    if vertex.is_valid and not vertex.link_faces:bmesh.ops.delete(bm,geom=[vertex],context='VERTS')
    return True


def simplify_straight_ridges(bm,g,protected,valid,scale):
    bm.normal_update();bm.verts.ensure_lookup_table();bm.verts.index_update()
    indices=[]
    for v in bm.verts:
        if len(v.link_edges)<3:continue
        relative=np.array(tuple(v.co))-g['point'];along=float(relative@g['direction'])
        if not 0<along<g['length'] or abs(relative@g['normal'])>g['radius']:continue
        if 'section_normal' in g and abs(relative@g['section_normal'])>g['radius']:continue
        if not any(abs(float(np.dot(tuple(f.normal),g['normal'])))>.65 for f in v.link_faces):continue
        if not protected(v):indices.append(v.index)
    if not indices:return bm,0
    # One mesh copy per marked line; local surgery retains a shared sharp edge.
    layer=bm.verts.layers.int.get('_straight_cleanup_id') or bm.verts.layers.int.new('_straight_cleanup_id')
    for v in bm.verts:v[layer]=v.index
    from collections import deque
    pending=deque([indices]);positions=[];attempts=0;deadline=time.monotonic()+2
    # Check subdivisions breadth-first so all parts of a long edge get a turn.
    # Bound retries around invalid reconstruction junctions for laptop exports.
    while pending and attempts<64 and time.monotonic()<deadline:
        ids=pending.popleft();attempts+=1
        candidate=bm.copy();tag=candidate.verts.layers.int.get('_straight_cleanup_id')
        vertices={v[tag]:v for v in candidate.verts};volume=bm.calc_volume(signed=True);trial_positions=[];changed_ids=[]
        for identity in ids:
            v=vertices.get(identity)
            if v is None or not v.is_valid or protected(v):continue
            position=float((np.array(tuple(v.co))-g['point'])@g['direction'])/g['length']
            if dissolve_ridge_vertex(candidate,v,g['normal']):trial_positions.append(position);changed_ids.append(identity)
        if trial_positions and valid(candidate,volume):
            bm.free();bm=candidate;positions.extend(trial_positions)
        else:
            candidate.free()
            if len(changed_ids)>1:
                middle=len(changed_ids)//2;pending.append(changed_ids[:middle]);pending.append(changed_ids[middle:])
    tag=bm.verts.layers.int.get('_straight_cleanup_id')
    if tag is not None:bm.verts.layers.int.remove(tag)
    if positions:g['ridge_positions']=positions
    return bm,len(positions)


def simplify_collinear_vertices(bm,g,protected,valid,scale,tolerance):
    """Dissolve exact line subdivisions in batches, preserving valid junctions."""
    from collections import deque
    total=0
    for _ in range(2):
        bm.verts.ensure_lookup_table();bm.verts.index_update();ids=[]
        for w in bm.verts:
            if len(w.link_edges)!=2 or protected(w):continue
            p=np.array(tuple(w.co));relative=p-g['point'];along=float(relative@g['direction'])
            if not tolerance<along<g['length']-tolerance:continue
            if np.linalg.norm(relative-g['direction']*along)>max(tolerance,scale/192*.1):continue
            a,b=[e.other_vert(w).co-w.co for e in w.link_edges]
            if a.length>tolerance and b.length>tolerance and a.normalized().dot(b.normalized())<-.999:ids.append(w.index)
        if not ids:break
        tag=bm.verts.layers.int.new('_collinear_cleanup_id')
        for w in bm.verts:w[tag]=w.index
        pending=deque([ids]);deadline=time.monotonic()+2;round_removed=0;attempts=0
        while pending and attempts<32 and time.monotonic()<deadline:
            chunk=pending.popleft();attempts+=1;candidate=bm.copy();layer=candidate.verts.layers.int.get('_collinear_cleanup_id');lookup={w[layer]:w for w in candidate.verts}
            chosen=[lookup[i] for i in chunk if i in lookup];before=len(candidate.verts);volume=bm.calc_volume(signed=True)
            bmesh.ops.dissolve_verts(candidate,verts=chosen,use_face_split=False,use_boundary_tear=False)
            if valid(candidate,volume):round_removed+=before-len(candidate.verts);bm.free();bm=candidate
            else:
                candidate.free()
                if len(chunk)>1:middle=len(chunk)//2;pending.append(chunk[:middle]);pending.append(chunk[middle:])
        bm.verts.layers.int.remove(bm.verts.layers.int.get('_collinear_cleanup_id'));total+=round_removed
        if not round_removed:break
    return bm,total
