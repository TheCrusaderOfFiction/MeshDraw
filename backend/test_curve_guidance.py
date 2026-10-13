import json
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from curve_guidance import curve_points
from edge_guidance import validate_guides
from silhouette_fit import drawing_fit
guide=dict(Id='a'*32,Kind='Curve',X1=24,Y1=92,X2=104,Y2=92,Control1X=24,Control1Y=20,Control2X=104,Control2Y=20,Radius=8,Mode='Relative')
points=curve_points(guide)
assert np.allclose(points[0],[24,92]) and np.allclose(points[-1],[104,92]) and points[:,1].min()<40
img=Image.new('1',(128,128));ImageDraw.Draw(img).polygon([tuple(p) for p in points],fill=1);front=np.array(img,bool)
side=Image.new('1',(128,128));ImageDraw.Draw(side).rectangle([48,38,80,92],fill=1)
top=Image.new('1',(128,128));ImageDraw.Draw(top).rectangle([24,48,104,80],fill=1)
masks=dict(front=front,back=front,left=np.array(side,bool),right=np.array(side,bool),top=np.array(top,bool),bottom=np.array(top,bool))
for mode in ('Relative','Absolute'):
    marks={'Front':[dict(guide,Mode=mode)]}
    mesh=drawing_fit(None,masks,80,edge_guides=marks)
    assert mesh.is_watertight and mesh.is_winding_consistent and mesh.volume>0
    metadata=mesh.metadata['edgeGuides'];assert len(metadata)==1 and metadata[0]['Kind']=='Curve' and len(metadata[0]['Points'])>50
    assert mesh.metadata['edgeGuidance']['outlineCorrection']['adjustedLines']>=1,mesh.metadata['edgeGuidance']
    folder=Path('verification/curve-generation');folder.mkdir(parents=True,exist_ok=True)
    mesh.export(folder/'shape.obj');(folder/'edge_guides.json').write_text(json.dumps(metadata));(folder/'round_surfaces.json').write_text('[]');(folder/'job.json').write_text(json.dumps(dict(vertexLimit=4000,textureSize=256)))
for bad in (None,-1,float('nan'),129,True):
    try:validate_guides({'Front':[dict(guide,Control1X=bad)]},masks)
    except ValueError:pass
    else:raise AssertionError('Invalid curve control accepted')
print('PASS: curved outlines generate closed geometry; controls validated; curve guide export preserved')
