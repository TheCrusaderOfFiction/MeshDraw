import numpy as np
from outline_guidance import straighten_outlines
from silhouette_fit import drawing_fit
from edge_guidance import validate_guides
mask=np.zeros((128,128),bool);mask[20:108,20:108]=True
guide=dict(Id='1'*32,Kind='Straight',X1=25,Y1=22,X2=25,Y2=105,Radius=12)
_,relative,_=straighten_outlines({'front':mask},{'Front':[guide]})
_,absolute,_=straighten_outlines({'front':mask},{'Front':[dict(guide,Mode='Absolute')]})
assert relative['front'][0]['X1']==19.5 and absolute['front'][0]['X1']==25
masks={n:mask for n in ('front','back','left','right','top','bottom')}
a=drawing_fit(None,masks,64,edge_guides={'Front':[guide]})
b=drawing_fit(None,masks,64,edge_guides={'Front':[dict(guide,Mode='Absolute')]})
assert a.is_watertight and b.is_watertight and b.volume<a.volume*.99
try:validate_guides({'Front':[dict(guide,Mode='Invalid')]},masks);raise AssertionError('Invalid mode accepted')
except ValueError:pass
print('PASS: relative fits drawn boundary; absolute retains exact mark and old cutting behavior; closed meshes; legacy default; invalid mode rejected.')
