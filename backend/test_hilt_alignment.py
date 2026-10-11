"""A partial hilt side outline must keep its height beside a full blade view."""
import numpy as np
from face_math import section_vertical_center
from silhouette_fit import reference_volume
from section_regions import section_volume
yy,xx=np.mgrid[:128,:128]
full=(abs(xx-63.5)<30)&(yy>10)&(yy<115)
hilt=(xx-63.5)**2+(yy-100)**2<10**2
masks={n:full.copy() for n in ('front','back','top','bottom')}
masks.update(left=hilt,right=hilt)
face=dict(Id='custom_'+'a'*32,Name='Blade side',Yaw=90,Pitch=0,Offset=0,IsSection=True,SectionMode='Sections')
center=section_vertical_center(masks)
_,extent,targets=reference_volume(masks,64,center)
volume=section_volume(targets,[face],{face['Id']:full},extent,105,64,center)
outer=np.argwhere(volume[50,:,:]);assert len(outer)>0
assert outer[:,0].max()<20,'Hilt profile was moved into blade mid-height'
_,_,old=reference_volume(masks,64)
old_volume=section_volume(old,[face],{face['Id']:full},extent,105,64)
assert np.argwhere(old_volume[50,:,:])[:,0].mean()>outer[:,0].mean()+12
print('PASS: partial hilt profile stays aligned with full sword; previous centering bug reproduced.')
