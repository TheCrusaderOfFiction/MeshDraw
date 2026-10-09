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
            if not isinstance(mark,dict) or not re.fullmatch('[a-f0-9]{32}',mark.get('Id','')) or mark['Id'] in ids or mark.get('Kind') not in ('Straight','Corner'):
                raise ValueError('Invalid edge or corner mark.')
            ids.add(mark['Id'])
            values=[mark.get(k) for k in ('X1','Y1','X2','Y2','Radius')]
            if any(isinstance(v,bool) or not isinstance(v,(int,float)) or not np.isfinite(v) for v in values):raise ValueError('Invalid edge mark coordinates.')
            x1,y1,x2,y2,r=values
            if not (0<=x1<=w and 0<=x2<=w and 0<=y1<=h and 0<=y2<=h and 2<=r<=64):raise ValueError('Edge mark outside canvas.')
            if mark['Kind']=='Straight' and np.hypot(x2-x1,y2-y1)<4:raise ValueError('Straight edge is too short.')
        result[name]=marks
    return result


def guide_frame(name,masks,extent,faces,pixel_scale):
    if name in AXES:
        a,b=AXES[name];u=np.eye(3)[a];v=np.eye(3)[b]
        if name in ('back','right'):u=-u
        if name in ('front','back','left','right','bottom'):v=-v
        yy,xx=np.nonzero(masks[name]);center=np.array([(xx.min()+xx.max())/2,(yy.min()+yy.max())/2])
    else:
        face=next(f for f in faces if f['Id']==name);u,up,_=face_basis(face);v=-up
        h,w=masks[name].shape;center=np.array([(w-1)/2,(h-1)/2])
    return u,v,center,np.asarray(extent)/2


def prepare_guidance(guides,masks,extent,faces=None):
    guides=validate_guides(guides,masks)
    scale=max(max(np.ptp(np.nonzero(m)[0])+1,np.ptp(np.nonzero(m)[1])+1) for n,m in masks.items() if n in AXES)
    contexts=[];ignored=[]
    for name,marks in guides.items():
        u,v,pixel_center,center=guide_frame(name,masks,extent,faces or [],scale)
        signed=distance_transform_edt(masks[name])-distance_transform_edt(~masks[name])
        for mark in marks:
            start=np.array([mark['X1'],mark['Y1']],dtype=float);end=np.array([mark['X2'],mark['Y2']],dtype=float);radius=mark['Radius']
            samples=start[None,:] if mark['Kind']=='Corner' else np.linspace(start,end,17)
            distance=map_coordinates(abs(signed),[samples[:,1],samples[:,0]],order=1,mode='constant',cval=1e6)
            if np.median(distance)>radius:
                ignored.append({'id':mark['Id'],'reason':'Mark is not near the outline boundary.'});continue
            point=center+(start[0]-pixel_center[0])/scale*u+(start[1]-pixel_center[1])/scale*v
            meta={'Id':mark['Id'],'Kind':mark['Kind'],'Point':point.tolist(),'Radius':radius/scale,'U':u.tolist(),'V':v.tolist()}
            context=dict(mark=mark,u=u,v=v,center=center,pixel_center=pixel_center,start=start,end=end,scale=scale,meta=meta)
            face=next((f for f in (faces or []) if f['Id']==name),None)
            if face and face.get('IsSection',False):
                _,_,normal=face_basis(face)
                depth=face.get('Offset',0)*np.dot(abs(normal),extent)/2
                point=point+normal*depth;meta['Point']=point.tolist()
                meta['SectionNormal']=normal.tolist();context.update(section_normal=normal,section_depth=depth)
            if mark['Kind']=='Straight':
                direction=end-start;length=np.linalg.norm(direction);direction/=length;normal=np.array([-direction[1],direction[0]])
                probes=np.array([start+direction*length*t+normal*d for t in (.25,.5,.75) for d in (-radius,radius)])
                values=map_coordinates(signed,[probes[:,1],probes[:,0]],order=1,mode='constant',cval=-radius*4).reshape(3,2).mean(axis=0)
                inside=1 if values[1]>values[0] else -1
                n=normal[0]*u+normal[1]*v
                meta.update(Normal=n.tolist(),End=(point+length/scale*(direction[0]*u+direction[1]*v)).tolist())
                context.update(direction=direction,length=length,normal=normal,inside=inside)
            contexts.append(context)
    return contexts,{'marked':sum(len(m) for m in guides.values()),'applied':len(contexts),'ignored':ignored}


def guide_distance_field(field,raw,contexts,extent,resolution):
    # Grid axes remain separate until projection, avoiding three dense XYZ arrays.
    q=(np.arange(field.shape[0],dtype=np.float32)+.5-4)/resolution-.5
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    influence=np.zeros_like(field)
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
        distance,_=projection(c);np.maximum(influence,np.clip(1-distance/c['mark']['Radius'],0,1)**2,out=influence)
    field+=(raw-field)*influence
    for c in contexts:
        if c['mark']['Kind']!='Straight':continue
        distance,offset=projection(c)
        norm=np.linalg.norm(np.asarray(c['meta']['Normal'])*extent)
        plane=c['inside']*offset*resolution/(c['scale']*norm)
        np.minimum(field,np.where(distance<=c['mark']['Radius'],plane,np.inf),out=field)
    return field


def pin_corners(mesh,contexts,extent,resolution):
    for c in contexts:
        if c['mark']['Kind']!='Corner':continue
        trial=mesh.copy();vertices=trial.vertices.copy();relative=vertices-c['meta']['Point']
        x=relative@c['u'];y=relative@c['v']
        # Only pin the sub-voxel corner cap, never the entire influence region.
        radius=min(c['meta']['Radius'],1.25*max(extent)/resolution)
        near=x*x+y*y<=radius*radius
        if 'section_normal' in c:near &= abs(relative@c['section_normal'])<=c['meta']['Radius']
        vertices[near]-=x[near,None]*c['u']+y[near,None]*c['v']
        trial.vertices=vertices;trial.merge_vertices(digits_vertex=8)
        trial.update_faces(trial.nondegenerate_faces(height=1e-8));trial.update_faces(trial.unique_faces());trial.remove_unreferenced_vertices()
        if trial.is_watertight and trial.is_winding_consistent and trial.volume>0:mesh=trial
    return mesh
