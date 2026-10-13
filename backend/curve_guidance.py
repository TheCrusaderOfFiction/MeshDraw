"""Cubic curve marks correct a local silhouette without flattening its surface."""
import numpy as np
from scipy.ndimage import map_coordinates
from scipy.spatial import cKDTree

def curve_points(mark):
    p=np.array([[mark['X1'],mark['Y1']],[mark['Control1X'],mark['Control1Y']],[mark['Control2X'],mark['Control2Y']],[mark['X2'],mark['Y2']]],float)
    count=min(32768,max(16,int(np.ceil(np.linalg.norm(np.diff(p,axis=0),axis=1).sum()))))
    t=np.linspace(0,1,count+1)[:,None];q=1-t
    return q**3*p[0]+3*q*q*t*p[1]+3*q*t*t*p[2]+t**3*p[3]

def curve_outline(field,mark):
    points=curve_points(mark);radius=mark['Radius']
    samples=map_coordinates(abs(field),[points[:,1],points[:,0]],order=1,mode='constant',cval=1e6)
    if np.median(samples)>radius:return False
    tangent=np.gradient(points,axis=0);normal=np.column_stack((-tangent[:,1],tangent[:,0]));normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-8)
    probes=points[::max(1,len(points)//32)];normals=normal[::max(1,len(points)//32)]
    plus=probes+normals*radius;minus=probes-normals*radius
    inside=1 if np.mean(map_coordinates(field,[plus[:,1],plus[:,0]],order=1,mode='constant',cval=-radius))>np.mean(map_coordinates(field,[minus[:,1],minus[:,0]],order=1,mode='constant',cval=-radius)) else -1
    x0,y0=np.maximum(0,np.floor(points.min(axis=0)-radius-1)).astype(int);x1,y1=np.minimum([field.shape[1],field.shape[0]],np.ceil(points.max(axis=0)+radius+2)).astype(int)
    yy,xx=np.mgrid[y0:y1,x0:x1];query=np.column_stack((xx.ravel(),yy.ravel()))
    distance,index=cKDTree(points).query(query);delta=query-points[index]
    signed=inside*np.einsum('ij,ij->i',delta,normal[index])
    patch=field[y0:y1,x0:x1];weight=np.clip(1-np.maximum(distance,abs(patch.ravel()))/radius,0,1)
    # Keep neighboring outlines around the two ends; do not extend the curve.
    weight*=np.clip(np.minimum(index,len(points)-1-index)/max(1,radius),0,1)
    gy,gx=np.gradient(patch);alignment=inside*(gx.ravel()*normal[index,0]+gy.ravel()*normal[index,1])/np.maximum(np.hypot(gx,gy).ravel(),1e-8)
    weight*=np.clip((alignment-.6)/.3,0,1);weight=weight*weight*(3-2*weight)
    patch+=(signed.reshape(patch.shape)-patch)*weight.reshape(patch.shape)
    return True
