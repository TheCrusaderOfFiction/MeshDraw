"""Local CPU export of a Niagara recipe and its transparent particle sprite."""
import argparse,json,sys
from pathlib import Path
import numpy as np
from PIL import Image
from package_asset import package

def validate_effect(effect):
    if not isinstance(effect,dict) or effect.get('version')!=1 or effect.get('preset') not in ('Fire','Smoke','Sparks','Magic'):raise ValueError('Invalid VFX preset')
    for name,low,high in [('spawnRate',1,500),('lifetime',.1,10),('size',1,200),('speed',0,1000),('spread',0,180)]:
        value=effect.get(name)
        if isinstance(value,bool) or not isinstance(value,(int,float)) or not np.isfinite(value) or not low<=value<=high:raise ValueError('Invalid VFX '+name)
    import re
    if not isinstance(effect.get('color'),str) or not re.fullmatch('#[A-Fa-f0-9]{6}',effect['color']):raise ValueError('Invalid VFX color')
    if effect.get('blend') not in ('Additive','Translucent') or not isinstance(effect.get('loop'),bool):raise ValueError('Invalid VFX blend or loop setting')
    return effect

def procedural_sprite(preset,size=512):
    yy,xx=np.mgrid[-1:1:complex(size),-1:1:complex(size)];r=np.hypot(xx,yy)
    if preset=='Fire':
        width=.2+.35*(yy+1)/2;alpha=np.exp(-3*(xx/width)**2-3*((yy-.2)/.85)**2)*np.clip((1-abs(yy))*5,0,1)
    elif preset=='Smoke':alpha=np.exp(-4*r*r)*(.85+.15*np.sin(xx*9+yy*5)*np.sin(yy*11))
    elif preset=='Sparks':alpha=np.exp(-45*xx*xx-8*yy*yy)
    else:alpha=np.maximum(np.exp(-80*(r-.45)**2)*.5,np.exp(-20*r*r))
    alpha*=np.clip((1-r)*8,0,1)
    rgba=np.full((size,size,4),255,np.uint8);rgba[:,:,3]=np.uint8(np.clip(alpha,0,1)*255)
    return Image.fromarray(rgba)

def generate(folder):
    folder=Path(folder);project=json.loads((folder/'project.meshdraw').read_text(encoding='utf-8-sig'));job=json.loads((folder/'job.json').read_text())
    if project.get('OutputType')!=2 or job.get('outputType')!='vfx':raise ValueError('Choose VFX mode before exporting')
    settings=project.get('Vfx') or {};effect=validate_effect(dict(version=1,preset=settings.get('Preset'),spawnRate=settings.get('SpawnRate'),lifetime=settings.get('Lifetime'),size=settings.get('Size'),speed=settings.get('Speed'),spread=settings.get('Spread'),color=settings.get('ColorHex'),blend=settings.get('Blend'),loop=settings.get('Loop')))
    print('Creating local Niagara sprite and effect settings...',flush=True)
    if settings.get('UseDrawing'):
        sprite=Image.open(folder/'vfx_drawing.png').convert('RGBA');sprite.thumbnail((1024,1024))
        if not sprite.getchannel('A').getbbox():raise ValueError('The particle drawing is empty. Paint a sprite on the Front face or use the preset sprite.')
    else:sprite=procedural_sprite(effect['preset'])
    sprite.save(folder/'vfx_sprite.png');(folder/'effect.json').write_text(json.dumps(effect,indent=2))
    destination=package(folder)
    result=dict(outputType='vfx',effect='effect.json',sprite='vfx_sprite.png',asset=destination.name,preset=effect['preset'])
    (folder/'result.json').write_text(json.dumps(result,indent=2));print('Saved asset.meshdrawasset for Niagara import.',flush=True);return result

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--job',required=True);args=parser.parse_args()
    try:generate(Path(args.job).parent)
    except Exception as error:print('Generation failed: '+str(error),file=sys.stderr,flush=True);sys.exit(1)
