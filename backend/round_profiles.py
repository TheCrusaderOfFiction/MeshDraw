"""Conservative circle recognition and sub-voxel circular surface correction."""
import numpy as np
from scipy.ndimage import label,binary_erosion,binary_dilation

def find_circles(mask):
    components,count=label(mask);circles=[]
    for index in range(1,count+1):
        region=components==index
        if region.sum()<150:continue
        y,x=np.nonzero(region & ~binary_erosion(region))
        if len(x)<24:continue
        matrix=np.column_stack((2*x,2*y,np.ones(len(x))))
        cx,cy,offset=np.linalg.lstsq(matrix,x*x+y*y,rcond=None)[0]
        radius=np.sqrt(max(0,offset+cx*cx+cy*cy));distance=np.hypot(x-cx,y-cy)
        if radius<8 or np.std(distance)/radius>.025 or np.max(abs(distance-radius))/radius>.09:continue
        # Ellipses, square corners and irregular outlines must keep their shape.
        yy,xx=np.ogrid[:mask.shape[0],:mask.shape[1]]
        fitted=(xx-cx)**2+(yy-cy)**2<=(radius+.5)**2
        overlap=(fitted & region).sum()/max(1,(fitted | region).sum())
        if overlap<.95:continue
        circles.append(dict(x=float(cx),y=float(cy),radius=float(radius+.5),component=index))
    return circles

def refine_round_profiles(masks):
    refined={};detected={}
    for name,mask in masks.items():
        circles=find_circles(mask);refined[name]=mask.copy()
        if not circles:continue
        components,_=label(mask);yy,xx=np.ogrid[:mask.shape[0],:mask.shape[1]]
        for circle in circles:
            refined[name][components==circle['component']]=False
            refined[name] |= (xx-circle['x'])**2+(yy-circle['y'])**2<=circle['radius']**2
        detected[name]=circles
    return refined,detected

def refine_round_surface(mesh,circles,masks,extent,faces,vertical_center,scale,resolution):
    from edge_guidance import guide_frame
    vertices=mesh.vertices.copy();original=vertices.copy();normals=mesh.vertex_normals.copy()
    adjusted=np.zeros(len(vertices),bool)
    processed=set();surfaces=[]
    opposites={'left':'right','right':'left','front':'back','back':'front','top':'bottom','bottom':'top'}
    for name,profiles in circles.items():
        if name in processed:continue
        processed.add(name);processed.add(opposites.get(name,''))
        u,v,pixel_center,center=guide_frame(name,masks,extent,faces or [],scale,vertical_center)
        for circle in profiles:
            circle_center=center+(circle['x']-pixel_center[0])/scale*u+(circle['y']-pixel_center[1])/scale*v
            surfaces.append(dict(center=circle_center.tolist(),u=u.tolist(),v=v.tolist(),radius=circle['radius']/scale,tolerance=2/resolution))
            relative=vertices-center;x=relative@u*scale+pixel_center[0]-circle['x'];y=relative@v*scale+pixel_center[1]-circle['y']
            radius=np.hypot(x,y);delta=circle['radius']-radius
            radial=(x[:,None]*u+y[:,None]*v)/np.maximum(radius[:,None],1e-8)
            near=(abs(delta)<1.5*scale/resolution)&(np.einsum('ij,ij->i',normals,radial)>.97)
            vertices[near]+=radial[near]*(delta[near,None]/scale);adjusted |= near
    # Keep correction smaller than two voxels, and reject topology regressions.
    displacement=vertices-original;length=np.linalg.norm(displacement,axis=1)
    vertices=original+displacement*np.minimum(1,1.5/resolution/np.maximum(length,1e-8))[:,None]
    # A conflict at one junction must not discard every valid round vertex.
    # Freeze vertices of any flipped/degenerate face and keep the safe remainder.
    original_normals=mesh.face_normals.copy()
    valid_faces=np.linalg.norm(original_normals,axis=1)>.5
    for _ in range(32):
        trial=mesh.copy();trial.vertices=vertices
        bad=(np.einsum('ij,ij->i',trial.face_normals,original_normals)<=0)&valid_faces
        if not bad.any():break
        rejected=np.unique(mesh.faces[bad]);vertices[rejected]=original[rejected];adjusted[rejected]=False
    mesh.metadata['roundSurfaces']=surfaces
    trial.metadata['roundSurfaces']=surfaces
    if trial.is_watertight and trial.is_winding_consistent and trial.volume>0 and not bad.any():return trial,int(adjusted.sum())
    return mesh,0

def preserve_round_sections(volume,circles,masks,extent,faces,vertical_center,scale,return_cylinders=False):
    """Matching opposite circles define a local round cross-section, not a cap."""
    from edge_guidance import guide_frame
    # Multiple circular projection pairs describe an intersecting round body
    # (for example a sphere), not an isolated cylindrical hilt section.
    if sum(bool(circles.get(a)) and bool(circles.get(b)) for a,b in [('left','right'),('front','back'),('top','bottom')])>1:return (volume,0,[]) if return_cylinders else (volume,0)
    n=volume.shape[0];q=(np.arange(n)+.5)/n-.5
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    result=volume.copy();preserved=0;cylinders=[]
    for first,opposite,axis in [('left','right',0),('front','back',2),('top','bottom',1)]:
        if first not in circles or opposite not in circles:continue
        u,v,center,_=guide_frame(first,masks,extent,faces or [],scale,vertical_center)
        ou,ov,other_center,_=guide_frame(opposite,masks,extent,faces or [],scale,vertical_center)
        for circle in circles[first]:
            local=((circle['x']-center[0])*u+(circle['y']-center[1])*v)/scale
            matching=[c for c in circles[opposite] if abs(c['radius']-circle['radius'])<circle['radius']*.08 and np.linalg.norm(((c['x']-other_center[0])*ou+(c['y']-other_center[1])*ov)/scale-local)<circle['radius']/scale*.08]
            if not matching:continue
            radius=circle['radius']/scale
            dx=sum(axes[i]*u[i] for i in range(3))-local@u
            dy=sum(axes[i]*v[i] for i in range(3))-local@v
            disk=dx*dx+dy*dy<=radius*radius
            core=dx*dx+dy*dy<=(radius*.5)**2
            support=(volume & core).any(axis=tuple(i for i in range(3) if i!=axis))
            indices=np.nonzero(support)[0]
            if len(indices)<2:continue
            # Only extend along the existing object's span through this circle.
            span=np.zeros(n,bool);span[indices.min():indices.max()+1]=True
            shape=[1,1,1];shape[axis]=n
            result |= disk & span.reshape(shape);preserved+=1
            cylinders.append(dict(center=(np.asarray(extent)/2+local).tolist(),u=u.tolist(),v=v.tolist(),axis=axis,radius=float(radius),low=float(indices.min()/n*extent[axis]),high=float((indices.max()+1)/n*extent[axis])))
    return (result,preserved,cylinders) if return_cylinders else (result,preserved)


def reconstruct_cylinders(field,cylinders,extent,resolution,volume=None,junction_field=None):
    """Union exact capped cylinder distance fields into the reconstructed hull.

    Build the curved wall before triangulation so every side and end rim follows
    the same circle, rather than repairing a voxel staircase after smoothing.
    """
    q=(np.arange(field.shape[0],dtype=np.float32)+.5-4)/resolution
    axes=[q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]]
    for cylinder in cylinders:
        center=np.asarray(cylinder['center']);u=cylinder['u'];v=cylinder['v'];axis=cylinder['axis']
        x=sum((axes[i]-center[i])*u[i] for i in range(3) if u[i])
        y=sum((axes[i]-center[i])*v[i] for i in range(3) if v[i])
        radial=cylinder['radius']-np.sqrt(x*x+y*y)
        axial=np.minimum(axes[axis]-cylinder['low'],cylinder['high']-axes[axis])
        exact=np.minimum(radial,axial)*resolution
        if volume is not None:
            # A union alone keeps raster/guide protrusions just outside the wall.
            # Replace the local cylinder envelope, retaining genuine attachments
            # that extend well beyond it (such as the sword blade).
            margin=3/resolution
            # Small rectangular/raster protrusions are not attachments. Seed
            # from parts extending clearly beyond the cylinder, then reach their
            # roots in physical units so thin axes do not leave disconnected gaps.
            from scipy.ndimage import distance_transform_edt
            seeds=np.pad(volume,4)&(radial < -2*margin)
            distance=distance_transform_edt(~seeds,sampling=np.asarray(extent)/resolution) if seeds.any() else np.full(field.shape,np.inf)
            # Avoid placing the zero contour exactly on an integer EDT shell;
            # marching cubes can otherwise create touching duplicate surfaces.
            attachment_field=(2*margin+1.125/resolution-distance)*resolution
            # Guide corrections on the attachment's remote edges must not leave
            # teeth where its root meets the analytic tube. Blend back to the
            # smooth hull here, then crop with a continuous distance field.
            source=field
            if junction_field is not None:
                blend=np.clip((radial+2*margin)/margin,0,1)*np.clip((axial+margin)/margin,0,1)
                source=field*(1-blend)+junction_field*blend
            cleaned=np.maximum(exact,np.minimum(source,attachment_field))
            np.copyto(field,cleaned,where=(radial > -2*margin)&(axial > -margin))
        np.maximum(field,exact,out=field)
    return field
