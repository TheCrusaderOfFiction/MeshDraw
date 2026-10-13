"""Corner annotations inside a tube must not pinch its wall or attachment roots."""
import numpy as np,trimesh
from cylinder_regions import protected_cylinder_vertices
from edge_guidance import pin_corners
from outline_guidance import straighten_surface
mesh=trimesh.creation.cylinder(radius=.15,height=.8,sections=64)
mesh.apply_translation([.5,.5,.5])
cylinder=dict(center=[.5,.5,.5],u=[1,0,0],v=[0,1,0],axis=2,radius=.15,low=.1,high=.9)
original=mesh.vertices.copy()
corner=dict(mark=dict(Kind='Corner'),meta=dict(Point=[.5,.5,.5],Radius=.1),u=np.array([1,0,0]),v=np.array([0,0,1]))
result=pin_corners(mesh,[corner],np.ones(3),192,[cylinder])
assert np.array_equal(result.vertices,original)
line=dict(mark=dict(Kind='Straight',FittedOutline=True),inside=-1,meta=dict(Point=[.648,.5,.1],End=[.648,.5,.9],Normal=[1,0,0],Radius=.04))
result=straighten_surface(mesh,[line],192,[cylinder])
assert np.array_equal(result.vertices,original)
assert protected_cylinder_vertices(mesh,[cylinder],2/192).all()
print('PASS: cylinder wall and attachment-region corners resist guide pinching')

# A rectangular raster envelope must become a round tube while a real blade
# stays connected. Check the implicit surface before any Blender cleanup.
from scipy.ndimage import distance_transform_edt,label,gaussian_filter
from round_profiles import reconstruct_cylinders
n=80;q=(np.arange(n)+.5)/n
x,y,z=np.meshgrid(q,q,q,indexing='ij')
body=(abs(x-.5)<.4)&(abs(y-.5)<.15)&(abs(z-.5)<.15)
blade=(abs(x-.5)<.15)&(y>.5)&(y<.95)&(abs(z-.5)<.035)
volume=body|blade;padded=np.pad(volume,4)
field=(distance_transform_edt(padded)-distance_transform_edt(~padded)).astype(np.float32)
attachment_field=gaussian_filter(field,1)
gaussian_filter(field,3,output=field)
c=dict(center=[.5,.5,.5],u=[0,1,0],v=[0,0,1],axis=0,radius=.15,low=.1,high=.9)
reconstruct_cylinders(field,[c],np.ones(3),n,volume,attachment_field)
solid=field[4:-4,4:-4,4:-4]>0
assert label(solid)[1]==1,'Attachment became disconnected'
wall=solid&(abs(x-.2)<.04)
assert np.hypot(y[wall]-.5,z[wall]-.5).max()<.151,'Raster corners protrude from tube'
assert solid[n//2,int(.8*n),n//2],'Blade was removed'
root_width=solid[n//2,int(.68*n),:].sum()
outside_width=solid[n//2,int(.78*n),:].sum()
assert root_width<=outside_width+2,'Join grew a rounded weld bead'
print('PASS: round tube and blade form one solid without raster protrusions')

# A thick neck crosses the radial diagonal. Sampling must continue along the
# blade direction there, rather than switching to its thickness axis.
thick=body|((abs(x-.5)<.15)&(y>.5)&(y<.95)&(abs(z-.5)<.14))
padded=np.pad(thick,4)
raw=(distance_transform_edt(padded)-distance_transform_edt(~padded)).astype(np.float32)
field=gaussian_filter(raw,3)
reconstruct_cylinders(field,[c],np.ones(3),n,thick,gaussian_filter(raw,1))
assert field[4+n//2,4+int(.6*n),4+int(.63*n)]>0,'Radial diagonal cut a groove into the straight neck'
print('PASS: thick attachment remains straight across the radial diagonal')
