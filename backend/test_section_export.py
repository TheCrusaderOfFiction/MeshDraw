"""Export real custom-view editor data with Blender for both mesh types."""
from pathlib import Path
import json,os,shutil,sys
from PIL import Image
from generate import ROOT,silhouette,finish_generation
from silhouette_fit import drawing_fit
from package_asset import unpack_checked
source=ROOT/'outputs'/'extra-face-editor-checks'/'extra-face-export'
out=ROOT/'outputs'/'section-region-export-checks';out.mkdir(exist_ok=True)
resources=out/'blender-runtime';resources.mkdir(exist_ok=True)
os.environ['BLENDER_USER_RESOURCES']=str(resources)
blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
project=json.loads((source/'project.meshdraw').read_text(encoding='utf-8'))
faces=project['ExtraFaces'];faces[0].update(MoveX=.1,MoveY=-.1,MoveZ=.2,Roll=25);faces[1].update(MoveZ=.15,Roll=-15,SectionMode="Sections",ReverseSection=False);masks={name:silhouette(source/(name+'.png'))[1] for name in ('front','back','left','right','top','bottom')}
extras={face['Id']:silhouette(source/(face['Id']+'.png'))[1] for face in faces}
mesh=drawing_fit(None,masks,64,faces,extras)
for mode in ('static','skeletal'):
    folder=out/mode;folder.mkdir(exist_ok=True)
    for file in source.iterdir():
        if file.is_file():shutil.copy2(file,folder/file.name)
    project['OutputType']=int(mode=='skeletal');(folder/'project.meshdraw').write_text(json.dumps(project),encoding='utf-8')
    # Ensure an angled face's own property painting takes part in the bake.
    Image.new('RGBA',Image.open(folder/(faces[0]['Id']+'.png')).size,(255,0,0,255)).save(folder/(faces[0]['Id']+'_basecolor.png'))
    (folder/'job.json').write_text(json.dumps(dict(version=2,outputType=mode,textureSize=128)))
    mesh.export(folder/'shape.obj')
    bundle=finish_generation(folder/'job.json',str(blender),mesh,dict(masks,**extras))
    manifest,files=unpack_checked(bundle)
    assert manifest['outputType']==mode and len(manifest['textures'])==5
    assert json.loads(files['project.meshdraw'])['ExtraFaces']==faces
    for face in faces:
        assert files[face['Id']+'.png']==(folder/(face['Id']+'.png')).read_bytes()
        assert face['Id']+'_basecolor.png' in files
    result=json.loads((folder/'result.json').read_text());assert len(result['constraintViews'])==8
    assert result['drawingFit']['smoothing']['iterations']==480
    print('Verified '+mode+' mesh, smooth export, five maps, two extra faces, and complete asset bundle.',flush=True)
print('PASS: extra face Blender static/skeletal export and checked bundle preservation.')
