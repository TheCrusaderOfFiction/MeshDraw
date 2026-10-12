import numpy as np,trimesh
from edge_guidance import pin_corners
mesh=trimesh.creation.box();points=mesh.vertices.copy();index=np.argmin(np.linalg.norm(points-[.5,.5,.5],axis=1));points[index]=[.49,.49,.49];mesh.vertices=points
members=[]
for u,v,point in [([1,0,0],[0,1,0],[.5,.5,0]),([1,0,0],[0,0,1],[.5,0,.5])]:
    members.append(dict(mark=dict(Kind='Corner',CornerGroup='a'*32,CornerStyle='Pointed'),u=np.array(u),v=np.array(v),meta=dict(Point=point,Radius=.1)))
result=pin_corners(mesh,members,np.ones(3),64)
assert np.min(np.linalg.norm(result.vertices-[.5,.5,.5],axis=1))<1e-8
assert result.is_watertight and result.is_winding_consistent
for c in members:c['mark']['CornerStyle']='Rounded'
rounded=pin_corners(mesh,members,np.ones(3),64)
assert np.array_equal(rounded.vertices,mesh.vertices),'Rounded group was pinned pointed'
print('PASS: merged face projections pin one shared vertex; rounded group preserved; closed mesh.')
