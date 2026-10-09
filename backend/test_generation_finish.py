"""Test the generation finish stages using a real AI-produced shape and real Blender."""
from pathlib import Path
import json, shutil, subprocess
from unittest.mock import patch
import trimesh
from generate import ROOT, finish_generation
from package_asset import unpack_checked
base=ROOT/'outputs'/'generation-diagnosis'
blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
mesh=trimesh.load(base/'shape.obj',force='mesh')
checks=ROOT/'outputs'/'generation-finish-checks';checks.mkdir(exist_ok=True)
for mode in ('static','skeletal'):
    folder=checks/mode;folder.mkdir(exist_ok=True)
    for file in base.iterdir():
        if file.suffix=='.png' or file.name in ('shape.obj','paint_bounds.json','skeleton.json','project.meshdraw'):shutil.copy2(file,folder/file.name)
    project=json.loads((folder/'project.meshdraw').read_text());project['OutputType']=int(mode=='skeletal')
    (folder/'project.meshdraw').write_text(json.dumps(project),encoding='utf-8')
    (folder/'job.json').write_text(json.dumps({'version':2,'outputType':mode,'textureSize':512}))
    bundle=finish_generation(folder/'job.json',str(blender),mesh,{name:None for name in ('front','back','left','right','top','bottom')})
    manifest,files=unpack_checked(bundle)
    result=json.loads((folder/'result.json').read_text())
    assert result['asset']==str(bundle) and len(manifest['textures'])==5 and manifest['outputType']==mode
# Reject an export failure before claiming success or packaging.
failed=checks/'failed';failed.mkdir(exist_ok=True);(failed/'job.json').write_text('{"outputType":"static"}')
with patch('generate.subprocess.run',side_effect=subprocess.CalledProcessError(1,'blender')) as run:
    try:finish_generation(failed/'job.json',str(blender),mesh,{});raise AssertionError('Failed export accepted')
    except subprocess.CalledProcessError:pass
    assert '--python-exit-code' in run.call_args[0][0]
assert not (failed/'result.json').exists() and not (failed/'asset.meshdrawasset').exists()
# Missing source drawing must not publish a successful result.
with patch('generate.subprocess.run'):
    (failed/'mesh.fbx').write_bytes(b'fake FBX')
    try:finish_generation(failed/'job.json',str(blender),mesh,{});raise AssertionError('Incomplete package accepted')
    except ValueError as error:assert 'project.meshdraw' in str(error)
assert not (failed/'result.json').exists()
(checks/'checks.txt').write_text('PASS: real AI shape; static/skeletal Blender export; five maps; verified asset packaging; result published after asset; Blender failure rejected; incomplete packaging rejected.\n')
print((checks/'checks.txt').read_text())
