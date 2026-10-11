"""Conservative circle recognition and sub-voxel circular surface correction."""
import numpy as np
from scipy.ndimage import label,binary_erosion

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
    for name,profiles in circles.items():
        u,v,pixel_center,center=guide_frame(name,masks,extent,faces or [],scale,vertical_center)
        for circle in profiles:
            relative=vertices-center;x=relative@u*scale+pixel_center[0]-circle['x'];y=relative@v*scale+pixel_center[1]-circle['y']
            radius=np.hypot(x,y);delta=circle['radius']-radius
            radial=(x[:,None]*u+y[:,None]*v)/np.maximum(radius[:,None],1e-8)
            near=(abs(delta)<1.5*scale/resolution)&(np.einsum('ij,ij->i',normals,radial)>.8)
            vertices[near]+=radial[near]*(delta[near,None]/scale);adjusted |= near
    # Keep correction smaller than two voxels, and reject topology regressions.
    displacement=vertices-original;length=np.linalg.norm(displacement,axis=1)
    vertices=original+displacement*np.minimum(1,1.5/resolution/np.maximum(length,1e-8))[:,None]
    trial=mesh.copy();trial.vertices=vertices
    if trial.is_watertight and trial.is_winding_consistent and trial.volume>0 and np.all(np.einsum('ij,ij->i',trial.face_normals,mesh.face_normals)>0):return trial,int(adjusted.sum())
    return mesh,0
