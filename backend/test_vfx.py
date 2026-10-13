import json
from pathlib import Path
import numpy as np
from PIL import Image
from generate_vfx import generate,validate_effect
from package_asset import unpack_checked
root=Path('verification/vfx-presets');root.mkdir(parents=True,exist_ok=True)
source=json.loads(Path('verification/vfx-editor/vfx-export/project.meshdraw').read_text())
presets={
    'Fire':dict(SpawnRate=80,Lifetime=1.2,Size=45,Speed=120,Spread=30,ColorHex='#FF6A19',Blend='Additive',Loop=True),
    'Smoke':dict(SpawnRate=35,Lifetime=3,Size=80,Speed=45,Spread=45,ColorHex='#A1A6B0',Blend='Translucent',Loop=True),
    'Sparks':dict(SpawnRate=120,Lifetime=.8,Size=8,Speed=450,Spread=90,ColorHex='#FFE8A6',Blend='Additive',Loop=False),
    'Magic':dict(SpawnRate=50,Lifetime=2,Size=25,Speed=100,Spread=80,ColorHex='#8D5CFF',Blend='Additive',Loop=True)}
for preset,settings in presets.items():
    folder=root/preset;folder.mkdir(exist_ok=True);project=dict(source,Vfx=dict(settings,Preset=preset,UseDrawing=False))
    (folder/'project.meshdraw').write_text(json.dumps(project));(folder/'skeleton.json').write_text(json.dumps(project['Skeleton']));(folder/'job.json').write_text(json.dumps(dict(version=2,outputType='vfx')))
    generate(folder);manifest,files=unpack_checked(folder/'asset.meshdrawasset')
    assert manifest['outputType']=='vfx' and 'mesh' not in manifest and 'mesh.fbx' not in files
    for name,content in files.items():assert content==(folder/name).read_bytes()
    image=np.array(Image.open(folder/'vfx_sprite.png'))
    assert image.shape==(512,512,4) and image[0,0,3]==0 and image[:,:,3].max()>100
    assert json.loads(files['effect.json'])['preset']==preset
folder=root/'Drawn';folder.mkdir(exist_ok=True)
project=dict(source,Vfx=dict(presets['Magic'],Preset='Magic',UseDrawing=True));(folder/'project.meshdraw').write_text(json.dumps(project));(folder/'skeleton.json').write_text(json.dumps(project['Skeleton']));(folder/'job.json').write_text(json.dumps(dict(version=2,outputType='vfx')))
pixels=np.zeros((32,32,4),np.uint8);pixels[8:24,8:24]=[20,90,220,180];Image.fromarray(pixels).save(folder/'vfx_drawing.png');generate(folder)
assert np.array_equal(np.array(Image.open(folder/'vfx_sprite.png')),pixels),'Drawn sprite colors or transparency changed'
valid=json.loads((folder/'effect.json').read_text())
for key,bad in [('lifetime',float('nan')),('speed',1001),('spawnRate',True),('color','#FFF'),('preset','invalid'),('loop',1)]:
    try:validate_effect(dict(valid,**{key:bad}))
    except ValueError:pass
    else:raise AssertionError('Invalid VFX setting accepted')
broken=json.loads((folder/'asset.meshdrawasset').read_text());broken['files'][0]['sha1']='0'*40;path=root/'broken.meshdrawasset';path.write_text(json.dumps(broken))
try:unpack_checked(path)
except ValueError:pass
else:raise AssertionError('Corrupt VFX bundle accepted')
print('PASS: four local VFX presets, transparent sprites, exact bundle recovery, drawn colors/alpha, invalid settings and checksum rejection')
