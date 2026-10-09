"""Integration checks for silhouette carving and Blender static/skeletal export."""
from pathlib import Path
import json
import subprocess
import tempfile
import numpy as np
import trimesh
from PIL import Image, ImageDraw
from generate import silhouette, constrain, ROOT

def main():
    destination = ROOT / 'outputs' / 'verification'
    destination.mkdir(parents=True, exist_ok=True)
    masks = {}
    for name in ['front','back','left','right','top','bottom']:
        image = Image.new('RGB', (640,640), 'white')
        ImageDraw.Draw(image).ellipse((100,100,540,540), outline='black', width=5)
        image.save(destination / f'{name}.png')
        masks[name] = silhouette(destination / f'{name}.png')[1]
    blank = destination / 'blank.png'
    Image.new('RGB', (640,640), 'white').save(blank)
    try:
        silhouette(blank)
        raise AssertionError('Blank outline accepted')
    except ValueError:
        pass
    original = trimesh.creation.icosphere(subdivisions=3)
    shape = constrain(original, masks, resolution=24)
    assert len(shape.faces) > 0 and shape.is_watertight
    # A narrow top silhouette must change the final geometry.
    narrowed = dict(masks)
    narrowed['top'] = masks['top'].copy()
    narrowed['top'][:, 200:440] = False
    narrower = constrain(original, narrowed, resolution=24)
    assert abs(narrower.volume) < abs(shape.volume)
    blender = next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
    for mode in ['static','skeletal']:
        folder = destination / mode
        folder.mkdir(exist_ok=True)
        obj = folder / 'shape.obj'
        shape.export(obj)
        subprocess.run([str(blender),'--background','--factory-startup','--python',
            str(ROOT / 'backend' / 'export_fbx.py'),'--',str(obj),mode],check=True)
        assert (folder/'mesh.fbx').stat().st_size > 1000
        subprocess.run([str(blender),'--background','--factory-startup','--python',
            str(ROOT / 'backend' / 'verify_fbx.py'),'--',str(folder/'mesh.fbx'),mode],check=True)
    print('PASS: outline validation, closed carving, top-view influence, static FBX, skeletal FBX, normalized skin weights.')

if __name__ == '__main__': main()
