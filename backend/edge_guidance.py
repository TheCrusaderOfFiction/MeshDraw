"""Independent drawing annotations for planar edges and pinned silhouette corners."""
import re
import numpy as np
from scipy.ndimage import distance_transform_edt,map_coordinates
from silhouette_fit import AXES,face_basis


def validate_guides(guides,masks):
    result={};ids=set()
    for face,marks in (guides or {}).items():
        name=face.lower() if face.lower() in AXES else face
        if name not in masks or not isinstance(marks,list) or len(marks)>128:raise ValueError('Invalid face edge marks.')
        h,w=masks[name].shape
        for mark in marks:
            if not isinstance(mark,dict) or not re.fullmatch('[a-f0-9]{32}',mark.get('Id','')) or mark['Id'] in ids or mark.get('Kind') not in ('Straight','Corner') or mark.get('Mode','Relative') not in ('Relative','Absolute') or mark.get('CornerStyle','Pointed') not in ('Pointed','Rounded'):
                raise ValueError('Invalid edge or corner mark.')
            ids.add(mark['Id'])
            values=[mark.get(k) for k in ('X1','Y1','X2','Y2','Radius')]
            if any(isinstance(v,bool) or not isinstance(v,(int,float)) or not np.isfinite(v) for v in values):raise ValueError('Invalid edge mark coordinates.')
            depths=[mark.get('Depth1',0),mark.get('Depth2',0)]
            if any(isinstance(d,bool) or not isinstance(d,(int,float)) or not np.isfinite(d) or abs(d)>8 for d in depths):raise ValueError('Invalid edge depth.')
            corner_group=mark.get('CornerGroup')
            if corner_group is not None and (not isinstance(corner_group,str) or not re.fullmatch('[a-f0-9]{32}',corner_group)):raise ValueError('Invalid merged corner group.')
            group=mark.get('LinkGroup');locked=mark.get('PositionLocked',False)
            if group is not None and (not isinstance(group,str) or not re.fullmatch('[a-f0-9]{32}',group)):raise ValueError('Invalid line connection.')
            lock_group=mark.get('LockGroup')
            if lock_group is not None and (not isinstance(lock_group,str) or not re.fullmatch('[a-f0-9]{32}',lock_group)):raise ValueError('Invalid position lock group.')
            if not isinstance(locked,bool) or (locked and not (group or lock_group)):raise ValueError('Invalid line position lock.')
            x1,y1,x2,y2,r=values
            if not (0<=x1<=w and 0<=x2<=w and 0<=y1<=h and 0<=y2<=h and 2<=r<=64):raise ValueError('Edge mark outside canvas.')
            if mark['Kind']=='Straight' and np.hypot(x2-x1,y2-y1)<4:raise ValueError('Straight edge is too short.')
        result[name]=marks
    return result


def guide_frame(name,masks,extent,faces,pixel_scale,vertical_center=None):
    if name in AXES:
        a,b=AXES[name];u=np.eye(3)[a];v=np.eye(3)[b]
        if name in ('back','right'):u=-u
        if name in ('front','back','left','right','bottom'):v=-v
        yy,xx=np.nonzero(masks[name]);center=np.array([(xx.min()+xx.max())/2,(yy.min()+yy.max())/2])
        if vertical_center is not None and b==1:center[1]=vertical_center
    else:
        face=next(f for f in faces if f['Id']==name);u,up,_=face_basis(face);v=-up
        from face_math import section_image_center
        center=section_image_center(face,masks[name].shape,vertical_center)
    return u,v,center,np.asarray(extent)/2


def prepare_guidance(guides,masks,extent,faces=None,vertical_center=None):
    guides=validate_guides(guides,masks)
    scale=max(max(np.ptp(np.nonzero(m)[0])+1,np.ptp(np.nonzero(m)[1])+1) for n,m in masks.items() if n in AXES)
    contexts=[];ignored=[]
    for name,marks in guides.items():
        u,v,pixel_center,center=guide_frame(name,masks,extent,faces or [],scale,vertical_center)
        signed=distance_transform_edt(masks[name])-distance_transform_edt(~masks[name])
        for mark in marks:
            start=np.array([mark['X1'],mark['Y1']],dtype=float);end=np.array([mark['X2'],mark['Y2']],dtype=float);radius=mark['Radius']
            samples=start[None,:] if mark['Kind']=='Corner' else np.linspace(start,end,17)
            distance=map_coordinates(abs(signed),[samples[:,1],samples[:,0]],order=1,mode='constant',cval=1e6)
            if np.median(distance)>radius:
                ignored.append({'id':mark['Id'],'reason':'Mark is not near the outline boundary.'});continue
            shift=np.zeros(3) if name in AXES else np.array([next(f for f in (faces or []) if f['Id']==name).get(k,0) for k in ('MoveX','MoveY','MoveZ')])*extent/2
            point=center+shift+(start[0]-pixel_center[0])/scale*u+(start[1]-pixel_center[1])/scale*v
            depth_normal=np.cross(u,v);depth_offset=depth_normal*np.asarray(extent)/2
            point=point+depth_offset*mark.get('Depth1',0)
            meta={'Id':mark['Id'],'Kind':mark['Kind'],'Point':point.tolist(),'Radius':radius/scale,'U':u.tolist(),'V':v.tolist(),'ViewNormal':np.cross(u,v).tolist(),'Mode':mark.get('Mode','Relative'),'CornerStyle':mark.get('CornerStyle','Pointed'),'CornerGroup':mark.get('CornerGroup')}
            context=dict(mark=mark,u=u,v=v,center=center,pixel_center=pixel_center-np.array([shift@u,shift@v])*scale,start=start,end=end,scale=scale,meta=meta)
            face=next((f for f in (faces or []) if f['Id']==name),None)
            if face and face.get('IsSection',False):
                _,_,normal=face_basis(face)
                depth=face.get('Offset',0)*np.dot(abs(normal),extent)/2
                point=point+normal*depth;meta['Point']=point.tolist()
                meta['SectionNormal']=normal.tolist();context.update(section_normal=normal,section_depth=depth+shift@normal)
            if mark['Kind']=='Straight':
                direction=end-start;length=np.linalg.norm(direction);direction/=length;normal=np.array([-direction[1],direction[0]])
                probes=np.array([start+direction*length*t+normal*d for t in (.25,.5,.75) for d in (-radius,radius)])
                values=map_coordinates(signed,[probes[:,1],probes[:,0]],order=1,mode='constant',cval=-radius*4).reshape(3,2).mean(axis=0)
                inside=1 if values[1]>values[0] else -1
                n=normal[0]*u+normal[1]*v
                meta.update(Normal=n.tolist(),End=(point+length/scale*(direction[0]*u+direction[1]*v)+depth_offset*(mark.get('Depth2',0)-mark.get('Depth1',0))).tolist())
                context.update(direction=direction,length=length,normal=normal,inside=inside)
            contexts.append(context)
    return contexts,{'marked':sum(len(m) for m in guides.values()),'applied':len(contexts),'ignored':ignored}


def guide_distance_field(field,raw,contexts,extent,resolution):
    # Grid axes remain separate until projection, avoiding three dense XYZ arrays.
    q=(np.arange(field.shape[0],dtype=np.float32)+.5-4)/resolution-.5
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    influence=np.zeros_like(field)
    rounded=np.zeros_like(field)
    def projection(c):
        x=c['pixel_center'][0]+sum(axes[i]*c['u'][i] for i in range(3))*c['scale']-c['start'][0]
        y=c['pixel_center'][1]+sum(axes[i]*c['v'][i] for i in range(3))*c['scale']-c['start'][1]
        depth2=0
        if 'section_normal' in c:
            depth=sum(axes[i]*c['section_normal'][i] for i in range(3))-c['section_depth']
            depth2=(depth*c['scale'])**2
        if c['mark']['Kind']=='Corner':return np.sqrt(x*x+y*y+depth2),None
        d=c['direction'];n=c['normal'];along=x*d[0]+y*d[1];offset=x*n[0]+y*n[1]
        distance=np.sqrt(offset*offset+np.maximum(np.maximum(-along,along-c['length']),0)**2+depth2)
        return distance,offset
    for c in contexts:
        if c['mark']['Kind']=='Corner' and c['mark'].get('CornerStyle','Pointed')=='Rounded':
            distance,_=projection(c)
            np.maximum(rounded,np.clip(1-distance/c['mark']['Radius'],0,1),out=rounded)
    for c in contexts:
        if c['mark'].get('Mode','Relative')!='Absolute' or c['mark'].get('CornerStyle','Pointed')=='Rounded':continue
        distance,_=projection(c)
        weight=np.clip(1-distance/c['mark']['Radius'],0,1)
        # Retain a broader crisp edge band, with a smooth transition into curves.
        weight=weight*weight*(3-2*weight)
        np.maximum(influence,weight,out=influence)
    field+=(raw-field)*influence
    # Relative marks already fit the painted boundary. Use their analytic plane
    # instead of restoring the raster staircase that the artist marked straight.
    gradients=np.gradient(field)
    lengths=np.maximum(np.sqrt(sum(g*g for g in gradients)),1e-8)
    for c in contexts:
        if c['mark']['Kind']!='Straight' or c['mark'].get('Mode','Relative')!='Relative' or not c['mark'].get('FittedOutline'):continue
        distance,offset=projection(c)
        normal=np.asarray(c['meta']['Normal'])*extent
        norm=np.linalg.norm(normal);normal/=norm
        alignment=sum(gradients[i]*normal[i]*c['inside'] for i in range(3))/lengths
        plane=c['inside']*offset*resolution/(c['scale']*norm)
        weight=np.clip(2*(1-distance/c['mark']['Radius']),0,1)
        weight*=np.clip((alignment-.7)/.2,0,1)
        # Only correct a sub-voxel boundary band. A projected plane is not a
        # replacement for the other five silhouettes or interior section limits.
        weight*=((abs(raw)<=1.5)&(abs(field)<=1.5)&(abs(plane-field)<=1.5))
        weight*=1-rounded
        field+=(plane-field)*weight
    # Relative guides must never create occupied samples outside the constrained
    # drawing volume. Interpolate its existing boundary rather than grow sheets.
    np.minimum(field,np.where(raw<0,-.05,np.inf),out=field)
    del gradients,lengths

    # Straightening already happened in drawing space. Restoring local detail
    # avoids rounded corners without clipping the reconstructed volume again.
    for c in contexts:
        if c['mark']['Kind']!='Straight' or c['mark'].get('Mode','Relative')!='Absolute':continue
        distance,offset=projection(c)
        norm=np.linalg.norm(np.asarray(c['meta']['Normal'])*extent)
        plane=c['inside']*offset*resolution/(c['scale']*norm)
        np.minimum(field,np.where((distance<=c['mark']['Radius'])&(rounded<=0),plane,np.inf),out=field)
    return field


def pin_corners(mesh,contexts,extent,resolution):
    groups={}
    for c in contexts:
        if c['mark']['Kind']=='Corner' and c['mark'].get('CornerGroup'):
            groups.setdefault(c['mark']['CornerGroup'],[]).append(c)
    for members in groups.values():
        if len(members)<2 or any(c['mark'].get('CornerStyle','Pointed')=='Rounded' for c in members):continue
        matrix=np.zeros((3,3));rhs=np.zeros(3);mean=np.mean([c['meta']['Point'] for c in members],axis=0)
        for c in members:
            projection=np.outer(c['u'],c['u'])+np.outer(c['v'],c['v']);matrix+=projection;rhs+=projection@c['meta']['Point']
        point=np.linalg.solve(matrix+np.eye(3)*1e-8,rhs+mean*1e-8)
        distance=np.linalg.norm(mesh.vertices-point,axis=1);index=int(np.argmin(distance))
        if distance[index]>min(min(c['meta']['Radius'] for c in members),3.5*max(extent)/resolution):continue
        candidate=mesh.copy();vertices=candidate.vertices.copy();vertices[index]=point;candidate.vertices=vertices
        if candidate.is_watertight and candidate.volume>0 and np.all(np.einsum('ij,ij->i',candidate.face_normals,mesh.face_normals)>0):mesh=candidate
    for c in contexts:
        if c['mark']['Kind']!='Corner' or c['mark'].get('CornerStyle','Pointed')=='Rounded' or (c['mark'].get('CornerGroup') and len(groups.get(c['mark']['CornerGroup'],[]))>=2):continue
        trial=mesh.copy();vertices=trial.vertices.copy();relative=vertices-c['meta']['Point']
        x=relative@c['u'];y=relative@c['v']
        # Only pin the sub-voxel corner cap, never the entire influence region.
        limit=1.25 if c['mark'].get('Mode','Relative')=='Absolute' else 3
        radius=min(c['meta']['Radius'],limit*max(extent)/resolution)
        near=x*x+y*y<=radius*radius
        if 'section_normal' in c:near &= abs(relative@c['section_normal'])<=c['meta']['Radius']
        vertices[near]-=x[near,None]*c['u']+y[near,None]*c['v']
        trial.vertices=vertices;trial.merge_vertices(digits_vertex=8)
        trial.update_faces(trial.nondegenerate_faces(height=1e-8));trial.update_faces(trial.unique_faces());trial.remove_unreferenced_vertices()
        if trial.is_watertight and trial.is_winding_consistent and trial.volume>0:mesh=trial
        elif c['mark'].get('Mode','Relative')=='Relative' and near.any():
            # Collapsing a whole cap may make it non-manifold. Extend its closest
            # existing vertex to the fitted corner while retaining connectivity.
            closest=np.flatnonzero(near)[np.argmin((x*x+y*y)[near])]
            candidate=mesh.copy();points=candidate.vertices.copy()
            points[closest]-=x[closest]*c['u']+y[closest]*c['v'];candidate.vertices=points
            alignment=np.einsum('ij,ij->i',candidate.face_normals,mesh.face_normals)
            if candidate.is_watertight and candidate.volume>0 and np.all(alignment>0):mesh=candidate
    return mesh
