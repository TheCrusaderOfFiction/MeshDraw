"""Check that verified FBX budgets and actual counts reach the finished asset."""
import base64
import json
from pathlib import Path
from unittest.mock import patch
from uuid import uuid4
import trimesh
from generate import ROOT,finish_generation
from package_asset import unpack_checked

folder=ROOT/'outputs'/'vertex-budget-checks'
mesh=trimesh.creation.icosphere(subdivisions=4)
for mode,limit in [('static',2000),('skeletal',1000)]:
    case=folder/f'{mode}-{limit}';layers={}
    for face in ('Front','Back','Left','Right','Top','Bottom'):
        layers[face]=[dict(Id=uuid4().hex,Name=name,Kind=kind,Visible=True,Locked=False,Opacity=1,Strokes='AAYCAA8AHwA=',Image=base64.b64encode((case/file).read_bytes()).decode('ascii'))
            for name,kind,file in [('Outline',0,face.lower()+'.png'),('Color',1,face.lower()+'_basecolor.png')]]
    rig={'Joints':[],'Bones':[]}
    (case/'skeleton.json').write_text(json.dumps(rig),encoding='utf-8')
    project=dict(Version=2,OutputType=int(mode=='skeletal'),CanvasWidth=128,CanvasHeight=128,SkeletonType='Humanoid' if mode=='skeletal' else 'Custom',Skeleton=rig,Layers=layers,VertexLimit=limit,ExtraFaces=[],Selections={})
    (case/'project.meshdraw').write_text(json.dumps(project),encoding='utf-8')
    # test_vertex_budget already performed real Blender export and FBX reimport.
    # Run only its completion/publication stage here, reusing those verified files.
    with patch('generate.subprocess.run'):
        bundle=finish_generation(case/'job.json','already-verified-Blender-export',mesh,{f:None for f in layers})
    result=json.loads((case/'result.json').read_text());summary=json.loads((case/'export-summary.json').read_text())
    assert result['vertices']==summary['renderVertices']<=limit
    assert result['faces']==summary['triangles'] and result['vertexLimit']==limit
    manifest,files=unpack_checked(bundle)
    assert manifest['outputType']==mode and len(manifest['textures'])==5
    assert json.loads(files['job.json'])['vertexLimit']==limit and json.loads(files['project.meshdraw'])['VertexLimit']==limit
print('PASS: finished static/skeletal assets retain vertex limits and publish actual exported counts.')
