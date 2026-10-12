import numpy as np
from face_math import register_section_profiles
from section_regions import region_profiles
n=96;y,x=np.mgrid[:n,:n]
a=(y>15)&(y<80)&(abs(x-(46+(y-48)*.08))<(8+(y-15)*.14))
b=np.roll(a[:,::-1],2,axis=1)
faces=[dict(Id='custom_'+'a'*32,Yaw=90,Pitch=0,Offset=.3,IsSection=True,SectionMode='Sections'),dict(Id='custom_'+'b'*32,Yaw=90,Pitch=0,Offset=-.3,IsSection=True,SectionMode='Sections')]
masks={faces[0]['Id']:a,faces[1]['Id']:b}
registered,report=register_section_profiles(faces,masks,np.ones(3))
assert len(report)==1 and registered[1]['_MirrorProfile']
assert '_MirrorProfile' not in faces[1],'Stored face was changed'
profiles=region_profiles(registered,masks,np.ones(3),96,64,47.5)
assert np.array_equal(profiles[0]['field']<=0,profiles[1]['field']<=0),'Opposite silhouettes remain offset'
near=(abs(profiles[0]['field'])<8)&(abs(profiles[1]['field'])<8)
assert np.max(abs(profiles[0]['field'][near]-profiles[1]['field'][near]))<1e-8,'Opposite boundary distances remain offset'
# Distinct/asymmetric profiles and deliberately displaced planes stay independent.
masks[faces[1]['Id']]=np.roll(b,15,axis=1)
assert not register_section_profiles(faces,masks,np.ones(3))[1]
print('PASS: mirrored opposite sections align; originals and deliberate displacement preserved.')
