"""Fit edge marks to painted boundaries before reconstructing the volume."""
import numpy as np
from scipy.ndimage import distance_transform_edt,binary_erosion,map_coordinates

def straighten_outlines(masks,guides):
    from edge_guidance import validate_guides
    normalized=validate_guides(guides,masks);output={k:v.copy() for k,v in masks.items()};effective={};adjusted=0
    for name,marks in normalized.items():
        mask=masks[name];boundary=mask & ~binary_erosion(mask)
        distance,indices=distance_transform_edt(~boundary,return_indices=True)
        field=distance_transform_edt(mask)-distance_transform_edt(~mask)
        gradient_y,gradient_x=np.gradient(field)
        gradient_length=np.maximum(np.hypot(gradient_x,gradient_y),1e-8)
        yy,xx=np.ogrid[:mask.shape[0],:mask.shape[1]];effective[name]=[]
        for mark in marks:
            start=np.array([mark['X1'],mark['Y1']],float);end=np.array([mark['X2'],mark['Y2']],float);radius=mark['Radius'];copy=dict(mark)
            if mark.get('Mode','Relative')=='Absolute':effective[name].append(copy);continue
            if mark['Kind']=='Corner':
                row=int(np.clip(round(start[1]),0,mask.shape[0]-1));column=int(np.clip(round(start[0]),0,mask.shape[1]-1))
                if distance[row,column]<=radius:
                    copy.update(X1=float(indices[1,row,column]),Y1=float(indices[0,row,column]),X2=float(indices[1,row,column]),Y2=float(indices[0,row,column]))
                effective[name].append(copy);continue
            direction=end-start;length=np.linalg.norm(direction);direction/=length;normal=np.array([-direction[1],direction[0]])
            samples=np.linspace(start,end,max(17,int(length/2)))
            rows=np.clip(np.rint(samples[:,1]).astype(int),0,mask.shape[0]-1);columns=np.clip(np.rint(samples[:,0]).astype(int),0,mask.shape[1]-1)
            close=distance[rows,columns]<=radius
            if close.mean()<.75:effective[name].append(copy);continue
            points=np.column_stack((indices[1,rows[close],columns[close]],indices[0,rows[close],columns[close]]))
            # The artist's mark specifies direction; fit its offset to the
            # actual boundary, instead of treating the mark as a cutting plane.
            offset=float(np.median((points-start)@normal));start+=normal*offset;end+=normal*offset
            probes=np.array([start+direction*length*t+normal*d for t in (.25,.5,.75) for d in (-radius,radius)])
            values=map_coordinates(field,[probes[:,1],probes[:,0]],order=1,mode='constant',cval=-radius*4).reshape(3,2).mean(axis=0)
            inside=1 if values[1]>values[0] else -1
            # Filled pixels represent cell centers: move the fitted boundary
            # half a pixel outward, avoiding systematic erosion of the outline.
            start-=normal*inside*.5;end-=normal*inside*.5
            along=(xx-start[0])*direction[0]+(yy-start[1])*direction[1]
            signed=inside*((xx-start[0])*normal[0]+(yy-start[1])*normal[1])
            near=np.maximum(abs(signed),abs(field))/radius
            weight=np.clip(1-near,0,1)
            taper=np.clip(np.minimum(along+radius,length+radius-along)/radius,0,1)
            weight*=taper;weight=weight*weight*(3-2*weight)
            alignment=inside*(gradient_x*normal[0]+gradient_y*normal[1])/gradient_length
            # Do not replace the neighboring edge around either endpoint.
            weight*=np.clip((alignment-.8)/.15,0,1)
            # Symmetric blending fills shallow dents as well as removing bumps.
            field+=(signed-field)*weight
            copy.update(X1=float(np.clip(start[0],0,mask.shape[1])),Y1=float(np.clip(start[1],0,mask.shape[0])),X2=float(np.clip(end[0],0,mask.shape[1])),Y2=float(np.clip(end[1],0,mask.shape[0])),FittedOutline=True)
            if np.hypot(copy['X2']-copy['X1'],copy['Y2']-copy['Y1'])<4:copy=dict(mark)
            effective[name].append(copy);adjusted+=1
        # When two marked edges meet, use their fitted intersection as the
        # corner guide. A tap offset from the drawn corner must not pull it in.
        lines=[g for g in effective[name] if g['Kind']=='Straight' and g.get('Mode','Relative')=='Relative']
        for corner in effective[name]:
            if corner['Kind']!='Corner' or corner.get('Mode','Relative')=='Absolute':continue
            point=np.array([corner['X1'],corner['Y1']]);near=[]
            for line in lines:
                a=np.array([line['X1'],line['Y1']]);b=np.array([line['X2'],line['Y2']])
                if min(np.linalg.norm(a-point),np.linalg.norm(b-point))<=corner['Radius']*2:near.append((a,b))
            if len(near)<2:continue
            a,b=near[0];c,d=near[1];matrix=np.column_stack((b-a,c-d))
            if abs(np.linalg.det(matrix))<1e-6:continue
            intersection=a+(b-a)*np.linalg.solve(matrix,c-a)[0]
            if np.linalg.norm(intersection-point)>corner['Radius']:continue
            corner.update(X1=float(np.clip(intersection[0],0,mask.shape[1])),Y1=float(np.clip(intersection[1],0,mask.shape[0])),X2=float(np.clip(intersection[0],0,mask.shape[1])),Y2=float(np.clip(intersection[1],0,mask.shape[0])))
        candidate=field>0
        if candidate.any():output[name]=candidate
    return output,effective,{'adjustedLines':adjusted,'method':'boundary-fitted outline correction with tapered influence'}

def straighten_surface(mesh,contexts,resolution):
    """Remove sub-voxel reconstruction noise along fitted edges, never cut them."""
    vertices=mesh.vertices.copy();normals=mesh.vertex_normals.copy()
    for context in contexts:
        if context['mark']['Kind']!='Straight' or not context['mark'].get('FittedOutline'):continue
        meta=context['meta'];point=np.asarray(meta['Point']);normal=np.asarray(meta['Normal']);end=np.asarray(meta['End']);direction=end-point;length=np.linalg.norm(direction);direction/=length
        relative=vertices-point;offset=relative@normal;along=relative@direction
        near=(abs(offset)<=.75/resolution)&(normals@(-context['inside']*normal)>.95)
        if 'SectionNormal' in meta:near &= abs(relative@np.asarray(meta['SectionNormal']))<=meta['Radius']
        fade=np.clip(np.minimum(along,length-along)/max(meta['Radius'],1e-8),0,1)
        vertices[near]-=normal*(offset[near]*fade[near])[:,None]
    trial=mesh.copy();trial.vertices=vertices
    if trial.is_watertight and trial.is_winding_consistent and trial.volume>0 and np.all(np.einsum('ij,ij->i',trial.face_normals,mesh.face_normals)>0):return trial
    return mesh
