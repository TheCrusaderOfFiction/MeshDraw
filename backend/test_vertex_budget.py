"""Create independent fixtures and verify actual Blender/FBX vertex limits."""
import json
import os
import subprocess
from pathlib import Path
import numpy as np
import trimesh
from PIL import Image,ImageDraw
from generate import ROOT

folder=ROOT/'outputs'/'vertex-budget-checks';folder.mkdir(parents=True,exist_ok=True)
for mode,limit in [('static',256),('static',2000),('static',3000),('static',100000),('skeletal',1000)]:
    case=folder/f'{mode}-{limit}';case.mkdir(exist_ok=True)
    trimesh.creation.icosphere(subdivisions=4).export(case/'shape.obj')
    (case/'job.json').write_text(json.dumps({'outputType':mode,'textureSize':128,'vertexLimit':limit}),encoding='utf-8')
    for name in ('front','back','left','right','top','bottom'):
        image=Image.new('RGB',(128,128),'white');ImageDraw.Draw(image).ellipse((10,10,118,118),outline='black',width=3);image.save(case/(name+'.png'))
        for kind,c in [('basecolor',(80,150,210)),('roughness',(140,)*3),('metallic',(0,)*3),('emission',(0,)*3),('height',(128,)*3)]:
            Image.new('RGBA',(128,128),(*c,255)).save(case/f'{name}_{kind}.png')
resources=folder/'blender-runtime';resources.mkdir(exist_ok=True);os.environ['BLENDER_USER_RESOURCES']=str(resources)
blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
subprocess.run([str(blender),'--background','--factory-startup','--python-exit-code','1','--python',str(ROOT/'backend'/'verify_vertex_budget.py'),'--',str(folder)],check=True)
print((folder/'checks.json').read_text())
