"""Infer isolated spherical components from matching perpendicular circles."""
import numpy as np
from scipy.ndimage import label

def sphere_sections(volume,circles,masks,extent,faces,vertical_center,scale,resolution):
    from edge_guidance import guide_frame
    profiles=[]
    for name,items in circles.items():
        u,v,pixel,center=guide_frame(name,masks,extent,faces or [],scale,vertical_center)
        for item in items:
            p=center+u*(item['x']-pixel[0])/scale+v*(item['y']-pixel[1])/scale
            profiles.append((name,item,u,v,p,item['radius']/scale))
    if not any(abs(np.cross(a[2],a[3])@np.cross(b[2],b[3]))<.1 for i,a in enumerate(profiles) for b in profiles[i+1:]):return volume,[],circles
    components,_=label(volume);result=volume.copy();spheres=[];used=set();owners=set()
    q=(np.arange(resolution)+.5)/resolution
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    for i,a in enumerate(profiles):
        for b in profiles[i+1:]:
            if abs(np.cross(a[2],a[3])@np.cross(b[2],b[3]))>.1:continue
            if abs(a[5]-b[5])>max(a[5],b[5])*.08:continue
            matrix=np.vstack((a[2],a[3],b[2],b[3]));rhs=np.array([a[2]@a[4],a[3]@a[4],b[2]@b[4],b[3]@b[4]])
            center=np.linalg.lstsq(matrix,rhs,rcond=None)[0];radius=(a[5]+b[5])/2
            if np.max(abs(matrix@center-rhs))>radius*.06:continue
            index=np.clip((center/extent*resolution).astype(int),0,resolution-1);owner=components[tuple(index)]
            if not owner:continue
            if owner in owners:used.update([(a[0],a[1]['component']),(b[0],b[1]['component'])]);continue
            coords=np.array(np.nonzero(components==owner)).T
            bounds=(coords.max(axis=0)-coords.min(axis=0)+1)*extent/resolution
            if max(bounds)>radius*2.4:continue
            ball=sum((axes[k]-center[k])**2 for k in range(3))<=radius**2
            # Other views/inside sections can contradict the sphere hypothesis.
            if (ball&volume).sum()/max(1,ball.sum())<.94:continue
            result[components==owner]=False;result|=ball;owners.add(owner)
            spheres.append(dict(kind='sphere',center=center.tolist(),radius=float(radius),tolerance=2/resolution,u=[1,0,0],v=[0,1,0]))
            used.update([(a[0],a[1]['component']),(b[0],b[1]['component'])])
    remaining={name:[p for p in items if (name,p['component']) not in used] for name,items in circles.items()}
    return result,spheres,{name:items for name,items in remaining.items() if items}

def sphere_field(field,spheres,extent,resolution):
    q=(np.arange(field.shape[0])+.5-4)/resolution
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    for s in spheres:
        distance=np.sqrt(sum((axes[k]-s['center'][k])**2 for k in range(3)))
        np.copyto(field,(s['radius']-distance)*resolution,where=distance<s['radius']+4/resolution)

def refine_spheres(mesh,spheres):
    if not spheres:return mesh
    vertices=mesh.vertices.copy()
    for s in spheres:
        delta=vertices-s['center'];distance=np.linalg.norm(delta,axis=1)
        near=(distance<s['radius']+s['tolerance'])&(distance>1e-8)
        vertices[near]=s['center']+delta[near]*(s['radius']/distance[near,None])
    trial=mesh.copy();trial.vertices=vertices
    if trial.is_watertight and trial.is_winding_consistent and trial.volume>0 and np.all(np.einsum('ij,ij->i',trial.face_normals,mesh.face_normals)>0):return trial
    return mesh
