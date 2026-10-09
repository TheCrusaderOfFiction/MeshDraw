"""Independent straight-edge fixtures and real Blender cleanup checks."""
import json,os,subprocess
from pathlib import Path
import numpy as np
from PIL import Image
from silhouette_fit import drawing_fit

root=Path(__file__).resolve().parent.parent
folder=root/'outputs'/'edge-guide-checks';folder.mkdir(parents=True,exist_ok=True)
mask=np.zeros((128,128),bool);mask[20:108,20:108]=True
masks={n:mask for n in ('front','back','left','right','top','bottom')}
guides={'Front':[dict(Id=f'{i:032x}',Kind=k,X1=x,Y1=y,X2=xx,Y2=yy,Radius=8) for i,k,x,y,xx,yy in [(1,'Straight',20,20,20,107),(2,'Straight',20,20,107,20),(3,'Corner',20,20,20,20)]]}
for name,marks in [('unmarked',None),('marked',guides),('skeletal',guides)]:
    case=folder/name;case.mkdir(exist_ok=True)
    mesh=drawing_fit(None,masks,64,edge_guides=marks)
    assert mesh.is_watertight and mesh.is_winding_consistent and mesh.volume>0
    if marks:assert mesh.metadata['edgeGuidance']['applied']==3
    mesh.export(case/'shape.obj')
    (case/'edge_guides.json').write_text(json.dumps(mesh.metadata['edgeGuides']))
    (case/'job.json').write_text(json.dumps({'outputType':'skeletal' if name=='skeletal' else 'static','vertexLimit':100000,'textureSize':128}))
    for face in masks:
        Image.fromarray(np.where(mask,0,255).astype('uint8')).save(case/f'{face}.png')
        for kind in ('basecolor','roughness','metallic','emission','height'):
            Image.new('RGBA',(128,128),(90,150,210,255)).save(case/f'{face}_{kind}.png')
resources=folder/'blender-runtime';resources.mkdir(exist_ok=True)
env=dict(os.environ,BLENDER_USER_RESOURCES=str(resources))
blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
subprocess.run([str(blender),'--background','--factory-startup','--python-exit-code','1','--python',str(root/'backend'/'verify_edge_guides.py'),'--',str(folder)],env=env,check=True)
print('PASS: watertight marked surface and Blender edge cleanup.')
