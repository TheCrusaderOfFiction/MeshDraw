"""Verify circular surfaces survive Blender export under the vertex cap."""
import json,os,subprocess
from pathlib import Path
from PIL import Image
from test_round_profiles import after,masks
root=Path(__file__).resolve().parent.parent;case=root/'outputs'/'round-export-checks';case.mkdir(parents=True,exist_ok=True)
after.export(case/'shape.obj');(case/'job.json').write_text(json.dumps({'vertexLimit':100000,'textureSize':128,'outputType':'static'}))
for name,mask in masks.items():
    import numpy as np
    Image.fromarray(np.where(mask,0,255).astype('uint8')).save(case/f'{name}.png')
    Image.new('RGBA',(128,128),(90,150,210,255)).save(case/f'{name}_basecolor.png')
resources=case/'blender-runtime';resources.mkdir(exist_ok=True)
env=dict(os.environ,BLENDER_USER_RESOURCES=str(resources))
blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
subprocess.run([str(blender),'--background','--factory-startup','--python-exit-code','1','--python',str(root/'backend'/'verify_round_export.py'),'--',str(case)],env=env,check=True)
