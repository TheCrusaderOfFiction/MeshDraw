import json
from pathlib import Path
import numpy as np
from silhouette_fit import drawing_fit
y,x=np.mgrid[:128,:128]
circle=(x-63.5)**2+(y-63.5)**2<=40**2
square=(abs(x-63.5)<=40)&(abs(y-63.5)<=40)
for views in [('front','back','left','right','top','bottom'),('front','left')]:
    masks={n:circle if n in views else square for n in ('front','back','left','right','top','bottom')}
    mesh=drawing_fit(None,masks,80)
    assert mesh.is_watertight and mesh.is_winding_consistent and mesh.volume>0
    spheres=[s for s in mesh.metadata['roundSurfaces'] if s.get('kind')=='sphere']
    assert len(spheres)==1,mesh.metadata['roundProfiles']
    sphere=spheres[0];r=np.linalg.norm(mesh.vertices-sphere['center'],axis=1)
    assert np.max(abs(r-sphere['radius']))<1e-6,'Sphere radius is uneven'
    assert abs(mesh.volume/(4*np.pi*sphere['radius']**3/3)-1)<.01
    if len(views)==6:
        folder=Path('verification/sphere-generation');folder.mkdir(parents=True,exist_ok=True)
        mesh.export(folder/'shape.obj')
        (folder/'round_surfaces.json').write_text(json.dumps(spheres))
        (folder/'job.json').write_text(json.dumps(dict(vertexLimit=4000,textureSize=256)))
        (folder/'edge_guides.json').write_text('[]')
# Opposite circular ends and rectangular side views define a cylinder.
rectangle=(abs(x-63.5)<25)&(abs(y-63.5)<40)
top=(abs(x-63.5)<40)&(abs(y-63.5)<25)
cylinder=drawing_fit(None,dict(front=circle,back=circle,left=rectangle,right=rectangle,top=top,bottom=top),80)
assert cylinder.metadata['roundProfiles']['analyticSpheres']==0
assert cylinder.metadata['roundProfiles']['analyticCylinders']==1
# Conflicting narrow third-axis profiles must not become a sphere.
thin=(abs(x-63.5)<10)&(abs(y-63.5)<40)
conflict=drawing_fit(None,dict(front=circle,back=circle,left=circle,right=circle,top=thin,bottom=thin),80)
assert conflict.metadata['roundProfiles']['analyticSpheres']==0
small=(x-63.5)**2+(y-63.5)**2<=16**2
stacked=((x-63.5)**2+(y-30.5)**2<=16**2)|((x-63.5)**2+(y-96.5)**2<=16**2)
multiple=drawing_fit(None,{n:small if n in ('top','bottom') else stacked for n in ('front','back','left','right','top','bottom')},80)
assert multiple.metadata['roundProfiles']['analyticSpheres']==2
assert len(multiple.split())==2,'Separate spheres became joined'
print('PASS: perpendicular circles form exact spheres; cylinders and conflicting views preserved')
