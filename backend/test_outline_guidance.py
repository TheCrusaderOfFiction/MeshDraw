import numpy as np
from outline_guidance import straighten_outlines
from silhouette_fit import drawing_fit
mask=np.zeros((128,128),bool);mask[20:108,20:108]=True
guides={'Front':[dict(Id=f'{i:032x}',Kind=kind,X1=x,Y1=y,X2=xx,Y2=yy,Radius=12) for i,kind,x,y,xx,yy in [(1,'Straight',25,22,25,105),(2,'Straight',22,25,105,25),(3,'Corner',25,25,25,25)]]}
corrected,effective,report=straighten_outlines({'front':mask},guides)
assert report['adjustedLines']==2
assert abs(effective['front'][0]['X1']-19.5)<.01,'Inset marker used as a cut'
assert abs(effective['front'][1]['Y1']-19.5)<.01
assert abs(effective['front'][2]['X1']-19.5)<.01 and abs(effective['front'][2]['Y1']-19.5)<.01
assert np.array_equal(corrected['front'],mask),'Accurate rectangle gained indentations'
# A shallow wavy edge is regularized symmetrically, without an endpoint notch.
wavy=mask.copy()
for row in range(30,98):
    edge=20+int(round(2*np.sin(row*.2)));wavy[row,:24]=False;wavy[row,edge:24]=True
out,_,_=straighten_outlines({'front':wavy},{'Front':[guides['Front'][0]]})
before=np.std([np.nonzero(wavy[y])[0].min() for y in range(35,93)])
after=np.std([np.nonzero(out['front'][y])[0].min() for y in range(35,93)])
assert after<before
mesh=drawing_fit(None,{n:mask for n in ('front','back','left','right','top','bottom')},64,edge_guides=guides)
assert mesh.is_watertight and mesh.is_winding_consistent and mesh.volume>.94
assert np.min(np.linalg.norm(mesh.vertices[:,[0,1]]-[0,1],axis=1))<1e-9,'Relative fitted corner was silently ignored'
print('PASS: inset marks fit actual edges; corners follow fitted intersections; no rectangular dents; wavy edge improves; closed mesh volume preserved.')
