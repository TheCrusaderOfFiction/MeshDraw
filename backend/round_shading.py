"""Analytic round shading after simplification, retaining caps and angular junctions."""
import json
import numpy as np


def round_region_mask(positions,surfaces):
    positions=np.asarray(positions);mask=np.zeros(len(positions),bool)
    for s in surfaces or []:
        d=positions-s['center'];mask |= np.hypot(d@s['u'],d@s['v'])<=s['radius']+s['tolerance']
    return mask


def round_protected_points(bm,surfaces):
    if not surfaces:return set()
    vertices=list(bm.verts);inside=round_region_mask([tuple(w.co) for w in vertices],surfaces)
    region={w for w,hit in zip(vertices,inside) if hit};points={tuple(w.co) for w in region}
    for face in bm.faces:
        boundary=list(face.verts)
        for i,w in enumerate(boundary):
            if w in region:
                # Lock adjoining triangle corners, without extending protection
                # across an entire long blade polygon that touches the hilt.
                points.add(tuple(boundary[i-1].co));points.add(tuple(boundary[(i+1)%len(boundary)].co))
    return points


def _round_face_key(data,polygon):
    return tuple(sorted(tuple(round(float(x),7) for x in data.vertices[i].co) for i in polygon.vertices))


def capture_round_normals(mesh,surfaces):
    """Keep split cap/wall normals before unrelated blade topology changes."""
    if not surfaces:return {}
    data=mesh.data;data.update();xyz=np.array([tuple(w.co) for w in data.vertices]);inside=np.zeros(len(xyz),bool)
    for s in surfaces:
        d=xyz-s['center'];inside |= np.hypot(d@s['u'],d@s['v'])<=s['radius']+s['tolerance']
    saved={}
    for face in data.polygons:
        if not inside[np.array(face.vertices)].any():continue
        normals={}
        for loop in range(face.loop_start,face.loop_start+face.loop_total):
            point=tuple(round(float(x),7) for x in data.vertices[data.loops[loop].vertex_index].co)
            normals[point]=tuple(data.corner_normals[loop].vector)
        saved[_round_face_key(data,face)]=normals
    return saved


def restore_round_normals(mesh,saved):
    """Restore unchanged circular faces after UVs and normals are rebuilt."""
    if not saved:return 0
    data=mesh.data;data.update();normals=[tuple(n.vector) for n in data.corner_normals];restored=0
    for face in data.polygons:
        original=saved.get(_round_face_key(data,face))
        if original is None:continue
        for loop in range(face.loop_start,face.loop_start+face.loop_total):
            point=tuple(round(float(x),7) for x in data.vertices[data.loops[loop].vertex_index].co)
            if point in original:normals[loop]=original[point];restored+=1
    data.normals_split_custom_set(normals)
    return restored


def load_round_surfaces(path,low,height):
    if not path.exists():return []
    convert=lambda p:np.asarray(p,dtype=float)[[0,2,1]]*np.array([1,-1,1])
    origin=np.array([low[0],low[1],low[2]])
    factor=1.8/height
    result=[]
    for surface in json.loads(path.read_text()):
        item=dict(center=(convert(surface['center'])-origin)*factor,u=convert(surface['u']),v=convert(surface['v']),radius=surface['radius']*factor,tolerance=surface['tolerance']*factor)
        if 'axis' in surface and 'low' in surface and 'high' in surface:
            index=surface['axis'];item.update(axis=convert(np.eye(3)[index]),low=(surface['low']-surface['center'][index])*factor,high=(surface['high']-surface['center'][index])*factor)
        result.append(item)
    return result


def smooth_round_normals(mesh,surfaces):
    if not surfaces:return 0
    # Tiny junction triangles can acquire sharp edges during reduction. They
    # belong to the smoothly meeting root, while end caps and true right-angle
    # blade corners must keep their split normals.
    import bmesh
    bm=bmesh.new();bm.from_mesh(mesh.data);bm.normal_update()
    changed=False
    for edge in bm.edges:
        if edge.smooth or len(edge.link_faces)!=2:continue
        a,b=edge.link_faces
        if a.normal.dot(b.normal)<np.cos(np.deg2rad(70)):continue
        for s in surfaces:
            if 'axis' not in s:continue
            axis=np.asarray(s['axis'])
            if max(abs(np.asarray(a.normal)@axis),abs(np.asarray(b.normal)@axis))>.8:continue
            d=np.asarray([tuple(w.co) for w in edge.verts])-s['center']
            radii=np.hypot(d@s['u'],d@s['v']);axial=d@axis
            if ((radii>=s['radius']-s['tolerance']*2)&(radii<=s['radius']+s['tolerance']*2)&(axial>s['low']+s['tolerance']*2)&(axial<s['high']-s['tolerance']*2)).any():
                edge.smooth=True;changed=True;break
    if changed:bm.to_mesh(mesh.data);mesh.data.update()
    bm.free()
    data=mesh.data;data.update()
    positions=np.array([tuple(v.co) for v in data.vertices])
    normals=np.array([tuple(n.vector) for n in data.corner_normals])
    indices=np.array([loop.vertex_index for loop in data.loops])
    # Use actual face orientation, not previously split or averaged vertex normals.
    face_normals=np.empty_like(normals)
    for polygon in data.polygons:face_normals[polygon.loop_start:polygon.loop_start+polygon.loop_total]=tuple(polygon.normal)
    selected=np.zeros(len(normals),bool)
    for s in surfaces:
        relative=positions-s['center'];x=relative@s['u'];y=relative@s['v']
        radius=np.hypot(x,y)
        radial=(x[:,None]*s['u']+y[:,None]*s['v'])/np.maximum(radius[:,None],1e-12)
        near=abs(radius-s['radius'])<s['tolerance']
        aligned=np.einsum('ij,ij->i',face_normals,radial[indices])>.9
        use=near[indices]&aligned
        normals[use]=radial[indices[use]];selected |= use
    # Custom loop normals override accidental sharp shading on the round wall;
    # cap and blade loop normals remain untouched even at shared vertices.
    data.normals_split_custom_set(normals.tolist())
    return int(selected.sum())


def restore_round_geometry(mesh,surfaces):
    """Keep simplification from pulling cylinder wall vertices inside the circle."""
    if not surfaces:return 0
    data=mesh.data;data.calc_loop_triangles()
    original=np.array([tuple(v.co) for v in data.vertices]);positions=original.copy()
    triangles=np.array([tuple(t.vertices) for t in data.loop_triangles])
    if not len(triangles):return 0
    old_cross=np.cross(original[triangles[:,1]]-original[triangles[:,0]],original[triangles[:,2]]-original[triangles[:,0]])
    lengths=np.linalg.norm(old_cross,axis=1)
    face_normals=old_cross/np.maximum(lengths[:,None],1e-12)
    for s in surfaces:
        relative=positions-s['center'];x=relative@s['u'];y=relative@s['v'];radius=np.hypot(x,y)
        radial=(x[:,None]*s['u']+y[:,None]*s['v'])/np.maximum(radius[:,None],1e-12)
        side=np.einsum('ij,ij->i',face_normals,radial[triangles].mean(axis=1))>.95
        near=abs(radius-s['radius'])<s['tolerance']
        chosen=np.zeros(len(positions),bool);chosen[np.unique(triangles[side])]=True;chosen &= near
        positions[chosen]+=radial[chosen]*(s['radius']-radius[chosen])[:,None]
    for _ in range(32):
        cross=np.cross(positions[triangles[:,1]]-positions[triangles[:,0]],positions[triangles[:,2]]-positions[triangles[:,0]])
        bad=(np.einsum('ij,ij->i',cross,old_cross)<=0)&(lengths>1e-12)
        if not bad.any():break
        rejected=np.unique(triangles[bad]);positions[rejected]=original[rejected]
    if bad.any():return 0
    changed=np.linalg.norm(positions-original,axis=1)>1e-8
    data.vertices.foreach_set('co',positions.ravel());data.update()
    return int(changed.sum())
