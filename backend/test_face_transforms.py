import numpy as np
from face_math import face_basis
from silhouette_fit import constrain_extra_faces,validate_faces
face=dict(Id='custom_'+'a'*32,Name='Moved',Yaw=0,Pitch=0,Offset=0,IsSection=False)
full=np.ones((64,)*3,bool);mask=np.zeros((128,128),bool);mask[:,48:80]=True
original=constrain_extra_faces(full.copy(),[face],{face['Id']:mask})
moved=constrain_extra_faces(full.copy(),[dict(face,MoveX=.5)],{face['Id']:mask})
assert np.argwhere(moved)[:,0].mean()>np.argwhere(original)[:,0].mean()+12
rolled=constrain_extra_faces(full.copy(),[dict(face,Roll=90)],{face['Id']:mask})
assert rolled.any(axis=(0,2)).sum()<rolled.any(axis=(1,2)).sum()
section=dict(face,IsSection=True,MoveZ=.5)
circle=np.fromfunction(lambda y,x:(x-63.5)**2+(y-63.5)**2<24**2,(128,128))
volume=constrain_extra_faces(full.copy(),[section],{face['Id']:circle})
assert volume[:,:,48].sum()<volume[:,:,32].sum()
u,v,n=face_basis(dict(face,Yaw=34,Pitch=20,Roll=-75));assert np.allclose(np.stack([u,v,n])@np.stack([u,v,n]).T,np.eye(3))
for key,value in [('MoveX',3),('MoveY',float('nan')),('MoveZ',float('inf')),('Roll',181)]:
    try:validate_faces([dict(face,**{key:value})]);raise AssertionError('Invalid transform accepted')
    except ValueError:pass
print('PASS: translation, roll, moved slice constraints, orthonormal basis, transform validation.')
