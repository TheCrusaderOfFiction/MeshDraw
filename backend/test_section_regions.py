import numpy as np
from silhouette_fit import reference_volume,drawing_fit
from section_regions import section_volume
n=64;yy,xx=np.mgrid[:128,:128]
wide=np.ones((128,128),bool);narrow=(abs(xx-63.5)<15)&(abs(yy-63.5)<50)
masks={name:wide.copy() for name in ('front','back','left','right','top','bottom')}
masks['front']=narrow;masks['back']=narrow
_,extent,targets=reference_volume(masks,n)
face=dict(Id='custom_'+'a'*32,Name='Blade',Yaw=0,Pitch=0,Offset=0,IsSection=True,SectionMode='Sections')
blade=(abs(xx-63.5)<40)&(abs(yy-63.5)<50);extras={face['Id']:blade}
v=section_volume(targets,[face],extras,extent,128,n)
assert v[:,:,10].sum()>v[:,:,50].sum()*2,'Interior outline still clipped by hilt outline'
assert np.array_equal(v[:,:,10],v[:,:,25]) and np.array_equal(v[:,:,40],v[:,:,50]),'Profiles leaked across boundary'
reverse=section_volume(targets,[dict(face,ReverseSection=True)],extras,extent,128,n)
assert reverse[:,:,50].sum()>reverse[:,:,10].sum()*2
second=dict(face,Id='custom_'+'b'*32,Offset=-.5)
tip=(abs(xx-63.5)<7)&(abs(yy-63.5)<50)
multi=section_volume(targets,[face,second],dict(extras,**{second['Id']:tip}),extent,128,n)
assert multi[:,:,4].sum()<multi[:,:,23].sum()*.5 and multi[:,:,23].sum()>multi[:,:,50].sum()*2
blend=section_volume(targets,[dict(face,SectionMode='Blend')],extras,extent,128,n)
assert blend[:,:,35].sum()>blend[:,:,55].sum(),'Transition does not gradually widen'
mesh=drawing_fit(None,masks,n,[face],extras)
assert mesh.is_watertight and mesh.is_winding_consistent and mesh.volume>0
print('PASS: independent hilt/blade profiles; widening; bounded regions; reverse flow; stacked sections; smooth transition; closed generated mesh.')
