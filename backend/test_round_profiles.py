import numpy as np
from unittest.mock import patch
from round_profiles import find_circles
from silhouette_fit import drawing_fit
yy,xx=np.mgrid[:128,:128];circle=(xx-63.5)**2+(yy-63.5)**2<=40**2
assert len(find_circles(circle))==1
ellipse=((xx-63.5)/40)**2+((yy-63.5)/28)**2<=1
square=(abs(xx-63.5)<40)&(abs(yy-63.5)<40)
assert not find_circles(ellipse) and not find_circles(square)
rectangle=(abs(xx-63.5)<25)&(abs(yy-63.5)<40)
top=(abs(xx-63.5)<40)&(abs(yy-63.5)<25)
masks=dict(front=circle,back=circle,left=rectangle,right=rectangle,top=top,bottom=top)
with patch('round_profiles.refine_round_profiles',lambda m:(m,{})):
    before=drawing_fit(None,masks,80)
after=drawing_fit(None,masks,80)
def error(mesh):
    p=mesh.vertices-mesh.bounds.mean(axis=0)
    selected=abs(p[:,2])<.15
    radius=np.hypot(p[selected,0],p[selected,1])
    return np.std(radius)/np.mean(radius)
old,new=error(before),error(after)
assert after.is_watertight and after.is_winding_consistent and after.volume>0
assert after.metadata['roundProfiles']['correctedVertices']>0
assert new<old*.8,(old,new)
assert abs(after.volume/before.volume-1)<.04
print(f'PASS: circular side variation reduced {100*(1-new/old):.1f}%; ellipse/square preserved; closed mesh; volume change below 4%.')
