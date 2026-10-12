"""Straight guides cannot grow sheets outside the constrained drawing hull."""
import numpy as np
from scipy.ndimage import distance_transform_edt,gaussian_filter
from silhouette_fit import reference_volume,reconstruct_surface
from outline_guidance import straighten_outlines
from edge_guidance import prepare_guidance,guide_distance_field
mask=np.zeros((128,128),bool);mask[20:108,20:108]=True
thin=np.zeros_like(mask);thin[20:108,60:68]=True
masks=dict(front=mask,back=mask,left=thin,right=thin,top=thin.T,bottom=thin.T)
guides={'Front':[dict(Id='a'*32,Kind='Straight',X1=20,Y1=20,X2=20,Y2=107,Radius=24,Mode='Relative')]}
masks,effective,_=straighten_outlines(masks,guides)
volume,extent,_=reference_volume(masks,64)
contexts,_=prepare_guidance(effective,masks,extent)
padded=np.pad(volume,4)
raw=(distance_transform_edt(padded)-distance_transform_edt(~padded)).astype(np.float32)
field=gaussian_filter(raw,3)
guide_distance_field(field,raw,contexts,extent,64)
assert not np.any(field[raw<0]>0),'Straight guide grew unsupported occupied cells'
mesh=reconstruct_surface(volume,64,contexts,extent)
assert mesh.is_watertight and mesh.is_winding_consistent
assert len(mesh.split())==1,'Extra disconnected sheets were generated'
print('PASS: straight guides stay in the constrained hull; closed mesh; no extra sheets.')
