"""Flatten planes supported by straight guides in independent drawing views."""
import numpy as np


def flatten_supported_planes(mesh,contexts,resolution,cylinders=None):
    original=mesh.vertices.copy();vertices=original.copy();normals=mesh.vertex_normals.copy()
    from cylinder_regions import protected_cylinder_vertices
    cylinder_protected=protected_cylinder_vertices(mesh,cylinders,8/resolution)
    groups=[]
    for c in contexts:
        g=c['meta']
        if g['Kind']!='Straight':continue
        normal=np.asarray(g['Normal'],float);normal/=np.linalg.norm(normal)
        offset=np.asarray(g['Point'])@normal
        if normal[np.argmax(abs(normal))]<0:normal=-normal;offset=-offset
        for group in groups:
            if normal@group['normal']>.9999 and abs(offset-group['offset'])<1.5/resolution:
                group['guides'].append(g);break
        else:groups.append(dict(normal=normal,offset=offset,guides=[g]))
    applied=0
    for group in groups:
        normal=group['normal'];guides=group['guides'];view_axes=[]
        for g in guides:
            view=np.asarray(g.get('ViewNormal',np.cross(g['U'],g['V'])))
            if not any(abs(view@other)>.99 for other in view_axes):view_axes.append(view)
        if len(view_axes)<2:continue
        absolute=[np.asarray(g['Point'])@normal for g in guides if g.get('Mode')=='Absolute']
        offset=float(np.median(absolute or [np.asarray(g['Point'])@normal for g in guides]))
        distance=vertices@normal-offset
        supported=np.zeros(len(vertices),int)
        for view in view_axes:
            covered=np.zeros(len(vertices),bool)
            for g in guides:
                gv=np.asarray(g.get('ViewNormal',np.cross(g['U'],g['V'])))
                if abs(gv@view)<.99:continue
                start=np.asarray(g['Point']);direction=np.asarray(g['End'])-start;length=np.linalg.norm(direction);direction/=length
                relative=vertices-start;along=relative@direction
                near=(along>=-g['Radius'])&(along<=length+g['Radius'])
                if 'SectionNormal' in g:near &= abs(relative@np.asarray(g['SectionNormal']))<=g['Radius']
                covered |= near
            supported+=covered
        chosen=(abs(distance)<2/resolution)&(abs(normals@normal)>.8)&(supported>=2)
        chosen &= ~cylinder_protected
        for corner in contexts:
            g=corner['meta']
            if g['Kind']=='Corner' and g.get('CornerStyle','Pointed')=='Rounded':
                relative=vertices-np.asarray(g['Point'])
                chosen &= (relative@np.asarray(g['U']))**2+(relative@np.asarray(g['V']))**2>g['Radius']**2
        if chosen.any():vertices[chosen]-=distance[chosen,None]*normal;applied+=1
    # Analytic cylinder end faces are planes too. Include their rim vertices.
    for cylinder in cylinders or []:
        axis=cylinder['axis'];center=np.asarray(cylinder['center'])
        relative=vertices-center;radial=np.hypot(relative@cylinder['u'],relative@cylinder['v'])
        for end,sign in [(cylinder['low'],-1),(cylinder['high'],1)]:
            chosen=(abs(vertices[:,axis]-end)<1.25/resolution)&(radial<=cylinder['radius']+.5/resolution)&(normals[:,axis]*sign>.7)
            vertices[chosen,axis]=end
    old_normals=mesh.face_normals.copy()
    for _ in range(32):
        trial=mesh.copy();trial.vertices=vertices
        bad=(np.einsum('ij,ij->i',trial.face_normals,old_normals)<=0)&(np.linalg.norm(old_normals,axis=1)>.5)
        if not bad.any():break
        rejected=np.unique(mesh.faces[bad]);vertices[rejected]=original[rejected]
    if bad.any() or not trial.is_watertight or not trial.is_winding_consistent or trial.volume<=0:return mesh,dict(planes=0,vertices=0)
    return trial,dict(planes=applied,vertices=int((np.linalg.norm(vertices-original,axis=1)>1e-10).sum()))
