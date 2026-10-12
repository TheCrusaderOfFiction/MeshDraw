import numpy as np
from silhouette_fit import drawing_fit
yy,xx=np.mgrid[:128,:128]
circle=(xx-63.5)**2+(yy-63.5)**2<25**2
# Contradictory rectangular views used to clip a round hilt into flat bands.
rectangle=(abs(xx-63.5)<50)&(abs(yy-63.5)<15)
top=(abs(xx-63.5)<50)&(abs(yy-63.5)<25)
masks=dict(front=rectangle,back=rectangle,left=circle,right=circle,top=top,bottom=top)
mesh=drawing_fit(None,masks,96)
assert mesh.metadata['roundProfiles']['preservedSections']==1
assert mesh.metadata['roundProfiles']['correctedVertices']>0
assert mesh.is_watertight and mesh.is_winding_consistent
# Cross-sections near either end, avoiding the rounded end caps.
points=mesh.vertices-mesh.bounds.mean(axis=0)
selected=(abs(points[:,0])>.25)&(abs(points[:,0])<.4)
radius=np.hypot(points[selected,1],points[selected,2])
assert np.std(radius)/np.mean(radius)<.025,'Round hilt has flat/clipped sides'
assert np.ptp(points[:,1])/np.ptp(points[:,2])>.95,'Circle still clipped by rectangular view'
print('PASS: opposite circular hilt survives conflicting rectangles; round correction retained; circular section aspect; closed mesh.')

assert mesh.metadata['roundProfiles']['analyticCylinders']==1
surface=mesh.metadata['roundSurfaces'][0]
relative=mesh.vertices-surface['center'];x=relative@surface['u'];y=relative@surface['v']
radius=np.hypot(x,y)
radial=(x[:,None]*surface['u']+y[:,None]*surface['v'])/np.maximum(radius[:,None],1e-12)
wall=np.einsum('ij,ij->i',mesh.vertex_normals,radial)>.9
for hemisphere in (y>0,y<0):
    selected=wall&hemisphere
    assert selected.sum()>100
    assert np.max(abs(radius[selected]-surface['radius']))/surface['radius']<.001,'Cylinder wall deviates at upper/lower curve'
print('PASS: analytic cylinder upper and lower walls retain exact radius.')

# Old raster ridges must be replaced rather than retained by a cylinder union.
from scipy.ndimage import distance_transform_edt
from round_profiles import reconstruct_cylinders
n=64;q=(np.arange(n)+.5)/n
x=q[:,None,None];y=q[None,:,None];z=q[None,None,:]
r=np.sqrt((y-.5)**2+(z-.5)**2)
volume=(r<.2)&(x>.2)&(x<.8)
# Small ridge on lower wall, and a genuine blade attachment on upper wall.
volume |= (y<.32)&(y>.275)&(abs(z-.5)<.035)&(x>.2)&(x<.8)
volume |= (y>.65)&(y<.95)&(abs(z-.5)<.035)&(x>.45)&(x<.55)
padded=np.pad(volume,4)
field=(distance_transform_edt(padded)-distance_transform_edt(~padded)).astype(np.float32)
cylinder=dict(center=[.5,.5,.5],u=[0,1,0],v=[0,0,1],axis=0,radius=.2,low=.2,high=.8)
reconstruct_cylinders(field,[cylinder],np.ones(3),n,volume)
assert field[36,22,36]<0,'Raster ridge remains outside cylinder'
assert field[36,60,36]>0,'Blade attachment was erased'
print('PASS: cylinder replaces raster ridges while retaining blade attachment.')
