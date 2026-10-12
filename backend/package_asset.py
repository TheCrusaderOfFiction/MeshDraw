"""Self-contained MeshDraw/Unreal interchange file. Version 1 uses UTF-8 JSON and checked base64 payloads."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import sys

FORMAT = 'MeshDrawAsset'
MAX_TOTAL = 256 * 1024 * 1024
SAFE_NAME = re.compile(r'^[A-Za-z0-9_-]+\.(?:fbx|png|json|meshdraw|blend|obj)$')

def package(folder, destination=None):
    folder=Path(folder).resolve()
    destination=Path(destination) if destination else folder/'asset.meshdrawasset'
    required=['mesh.fbx','project.meshdraw','skeleton.json','job.json',
              'front.png','back.png','left.png','right.png','top.png','bottom.png']
    for name in required:
        if not (folder/name).is_file(): raise ValueError(f'Missing {name}. Generate from the layered app before packaging.')
    job=json.loads((folder/'job.json').read_text())
    mode=job.get('outputType')
    if mode not in ('static','skeletal'): raise ValueError('Invalid output type')
    project=json.loads((folder/'project.meshdraw').read_text(encoding='utf-8'))
    if project.get('Version')!=2 or project.get('OutputType')!=(1 if mode=='skeletal' else 0):
        raise ValueError('The layered project and generated mesh type disagree. Regenerate the object first.')
    summary=json.loads((folder/'export-summary.json').read_text()) if (folder/'export-summary.json').is_file() else {}
    textures=summary.get('textures',{})
    if any(kind not in ('basecolor','roughness','metallic','emission','height') or name!=f'mesh_{kind}.png' for kind,name in textures.items()):
        raise ValueError('Invalid material map manifest')
    for name in textures.values():
        if not (folder/name).is_file(): raise ValueError(f'Missing texture {name}')
    files=[]; total=0
    channels=('basecolor','roughness','metallic','emission','height')
    views=('front','back','left','right','top','bottom')
    known=set(required+['drawing-fit.json','mesh.blend','shape.obj','paint_bounds.json','manifest.json','unreal-material.json','export-summary.json','result.json','edge_guides.json','round_surfaces.json'])
    known.update(f'{view}_{kind}.png' for view in views for kind in channels)
    extras=project.get('ExtraFaces') or []
    if len(extras)>8:raise ValueError('Too many extra faces')
    for face in extras:
        identifier=face.get('Id','')
        if not re.fullmatch(r'custom_[a-f0-9]{32}',identifier):raise ValueError('Invalid extra face identifier')
        if not (folder/(identifier+'.png')).is_file():raise ValueError('Missing extra face drawing: '+identifier)
        known.add(identifier+'.png')
        known.update(f'{identifier}_{kind}.png' for kind in channels)
    known.update(textures.values())
    for path in sorted(folder.iterdir()):
        if not path.is_file() or path.name not in known or not SAFE_NAME.fullmatch(path.name): continue
        payload=path.read_bytes(); total+=len(payload)
        if total>MAX_TOTAL or len(payload)>96*1024*1024: raise ValueError('Project is too large for the interchange format')
        files.append({'name':path.name,'size':len(payload),'sha1':hashlib.sha1(payload).hexdigest(),'data':base64.b64encode(payload).decode('ascii')})
    if len(files)>128: raise ValueError('Too many files')
    document={'format':FORMAT,'version':1,'outputType':mode,'unrealVersion':'5.8',
        'mesh':'mesh.fbx','project':'project.meshdraw','skeleton':'skeleton.json','textures':textures,'files':files}
    destination.parent.mkdir(parents=True,exist_ok=True)
    # A partial write must not replace a previously exported asset.
    temporary=destination.with_suffix(destination.suffix+'.tmp')
    temporary.write_text(json.dumps(document,separators=(',',':')),encoding='utf-8')
    temporary.replace(destination)
    return destination

def unpack_checked(path):
    document=json.loads(Path(path).read_text(encoding='utf-8'))
    if document.get('format')!=FORMAT or document.get('version')!=1: raise ValueError('Unsupported MeshDraw asset')
    files={}; total=0
    for entry in document['files']:
        name=entry['name']
        if not SAFE_NAME.fullmatch(name) or name in files: raise ValueError('Unsafe or duplicate file name')
        payload=base64.b64decode(entry['data'],validate=True); total+=len(payload)
        if total>MAX_TOTAL or len(payload)>96*1024*1024 or len(payload)!=entry['size'] or hashlib.sha1(payload).hexdigest()!=entry['sha1']:
            raise ValueError('Damaged payload')
        files[name]=payload
    return document,files

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--folder',required=True); parser.add_argument('--output')
    args=parser.parse_args()
    try: print(package(args.folder,args.output))
    except Exception as error: print(str(error),file=sys.stderr);sys.exit(1)
