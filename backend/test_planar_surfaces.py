import numpy as np,trimesh
from planar_surfaces import flatten_supported_planes
mesh=trimesh.creation.box()
for _ in range(3):mesh=mesh.subdivide()
points=mesh.vertices.copy();top=points[:,2]>.49;inside=top&(abs(points[:,0])<.49)&(abs(points[:,1])<.49)
points[inside,2]+=.003*np.sin(points[inside,0]*37)*np.cos(points[inside,1]*29);mesh.vertices=points
contexts=[]
for view,point,end in [([1,0,0],[0,-.5,.5],[0,.5,.5]),([-1,0,0],[0,-.5,.5],[0,.5,.5]),([0,1,0],[-.5,0,.5],[.5,0,.5]),([0,-1,0],[-.5,0,.5],[.5,0,.5])]:
    contexts.append(dict(meta=dict(Kind='Straight',Normal=[0,0,1],ViewNormal=view,Point=point,End=end,Radius=.05,U=[1,0,0],V=[0,1,0])))
flat,report=flatten_supported_planes(mesh,contexts,192)
assert report['planes']==1 and report['vertices']>0
assert np.max(abs(flat.vertices[top,2]-.5))<1e-10,'Supported face remains wavy'
assert flat.is_watertight and flat.is_winding_consistent
unchanged,report=flatten_supported_planes(mesh,contexts[:2],192)
assert report['planes']==0,'One viewing angle flattened an unsupported face'
assert np.array_equal(unchanged.vertices,mesh.vertices)
print('PASS: four orthographic marks produce an exactly planar face; topology retained; single-view detail preserved.')

