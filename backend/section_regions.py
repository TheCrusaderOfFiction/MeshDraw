"""Interior profile planes replace outer projections within their own regions."""
import numpy as np
from scipy.ndimage import distance_transform_edt,map_coordinates
from face_math import face_basis,section_image_center

PAIRS={0:('left','right'),1:('top','bottom'),2:('front','back')}

def active_sections(faces):
    return [f for f in (faces or []) if f.get('IsSection',False) and f.get('SectionMode','Whole')!='Whole']

def region_profiles(faces,masks,extent,scale,n,vertical_center=None):
    q=(np.arange(n,dtype=np.float32)+.5)/n*2-1
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    profiles=[]
    for face in active_sections(faces):
        u,v,normal=face_basis(face);sign=-1 if face.get('ReverseSection',False) else 1
        shift=np.array([face.get(k,0) for k in ('MoveX','MoveY','MoveZ')])*extent
        radius=abs(normal)@extent;boundary=face.get('Offset',0)*radius
        depth=sum(axes[i]*normal[i] for i in range(3))-shift@normal
        t=sign*(depth-boundary)
        horizontal=sum(axes[i]*u[i] for i in range(3))-shift@u
        vertical=sum(axes[i]*v[i] for i in range(3))-shift@v
        mask=masks[face['Id']]
        if not mask.any():raise ValueError('Empty extra face: '+face.get('Name',face['Id']))
        field=distance_transform_edt(~mask)-distance_transform_edt(mask)
        center=section_image_center(face,mask.shape,vertical_center)
        rows,columns=np.broadcast_arrays(center[1]-vertical*scale/2,center[0]+horizontal*scale/2)
        sampled=map_coordinates(field,[rows.ravel(),columns.ravel()],order=1,mode='constant',cval=max(mask.shape)).reshape(rows.shape)
        profiles.append(dict(face=face,t=t,field=sampled,axis=int(np.argmax(abs(normal))),span=max(.001,radius-sign*(shift@normal+boundary))))
    return profiles

def section_volume(targets,faces,masks,extent,scale,n,vertical_center=None):
    profiles=region_profiles(faces,masks,extent,scale,n,vertical_center)
    volume=np.ones((n,n,n),bool)
    for axis,pair in PAIRS.items():
        group=[p for p in profiles if p['axis']==axis]
        # Original opposing views still define the outer region. Once crossed,
        # the closest preceding interior plane owns this region's profile.
        def plane(name):
            a,b={'front':(0,1),'back':(0,1),'left':(2,1),'right':(2,1),'top':(0,2),'bottom':(0,2)}[name]
            target=targets[name].T
            return target[:,:,None] if (a,b)==(0,1) else target.T[None,:,:] if a==2 else target[:,None,:]
        base=plane(pair[0])&plane(pair[1])
        if not group:volume &= base;continue
        # Signed distances are interpolated, rather than opacity or masks, so
        # smooth transitions can widen as well as narrow a profile.
        binary=targets[pair[0]] & targets[pair[1]]
        row_axis,column_axis={0:(1,2),1:(2,0),2:(1,0)}[axis]
        sampling=(extent[row_axis]*scale/n,extent[column_axis]*scale/n)
        signed=distance_transform_edt(~binary,sampling=sampling)-distance_transform_edt(binary,sampling=sampling)
        target=signed.T
        basefield=target[:,:,None] if axis==2 else target.T[None,:,:] if axis==0 else target[:,None,:]
        best=np.full((n,n,n),np.inf,np.float32);owned=np.broadcast_to(basefield,(n,n,n)).copy()
        for p in group:
            distance=-p['t'];select=(distance>=0)&(distance<best)
            owned=np.where(select,p['field'],owned);best=np.where(select,distance,best)
        near=np.full((n,n,n),np.inf,np.float32);blended=owned.copy()
        for p in group:
            if p['face'].get('SectionMode')!='Blend':continue
            select=(p['t']>0)&(p['t']<near)
            weight=np.clip(1-p['t']/p['span'],0,1)
            blended=np.where(select,owned*(1-weight)+p['field']*weight,blended);near=np.where(select,p['t'],near)
        volume &= blended<=0
    return volume
