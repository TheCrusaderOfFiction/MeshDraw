import numpy as np
from silhouette_fit import drawing_fit
from edge_guidance import validate_guides
mask=np.zeros((128,128),bool);mask[20:108,20:108]=True
masks={n:mask for n in ('front','back','left','right','top','bottom')}
guides={'Front':[dict(Id='1'*32,Kind='Straight',X1=20,Y1=22,X2=20,Y2=105,Radius=12),dict(Id='2'*32,Kind='Straight',X1=22,Y1=20,X2=105,Y2=20,Radius=12),dict(Id='3'*32,Kind='Corner',X1=20,Y1=20,X2=20,Y2=20,Radius=12,CornerStyle='Pointed')]}
a=drawing_fit(None,masks,64,edge_guides=guides)
guides['Front'][-1]['CornerStyle']='Rounded'
b=drawing_fit(None,masks,64,edge_guides=guides)
da=np.min(np.linalg.norm(a.vertices[:,[0,1]]-[0,1],axis=1));db=np.min(np.linalg.norm(b.vertices[:,[0,1]]-[0,1],axis=1))
assert da<1e-8 and db>da+.005,'Rounded and pointed corners generate the same shape'
assert a.is_watertight and b.is_watertight
assert validate_guides(guides,masks)['front'][-1]['CornerStyle']=='Rounded'
guides['Front'][-1]['CornerStyle']='Bad'
try:validate_guides(guides,masks)
except ValueError:pass
else:raise AssertionError('Invalid corner style accepted')
print('PASS: rounded vs pointed generated corners differ; closed meshes; valid persisted property.')
