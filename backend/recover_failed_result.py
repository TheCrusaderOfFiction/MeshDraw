"""Recover saved drawing inputs into a fresh output folder without rerunning AI."""
import json,os,shutil,sys
from pathlib import Path
from generate import silhouette,finish_generation
from silhouette_fit import drawing_fit
source=Path(sys.argv[1]).resolve();destination=Path(sys.argv[2]).resolve()
destination.mkdir(parents=True,exist_ok=False)
for file in source.iterdir():
    if file.suffix=='.png' or file.name in ('project.meshdraw','skeleton.json','job.json','manifest.json'):shutil.copy2(file,destination/file.name)
project=json.loads((destination/'project.meshdraw').read_text(encoding='utf-8-sig'))
masks={n:silhouette(destination/f'{n}.png')[1] for n in ('front','back','left','right','top','bottom')}
faces=project.get('ExtraFaces') or [];extras={f['Id']:silhouette(destination/(f['Id']+'.png'))[1] for f in faces}
mesh=drawing_fit(None,masks,192,faces,extras,project.get('EdgeGuides'));mesh.export(destination/'shape.obj')
resources=destination/'blender-runtime';resources.mkdir();os.environ['BLENDER_USER_RESOURCES']=str(resources)
blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
print(finish_generation(destination/'job.json',str(blender),mesh,dict(masks,**extras)),flush=True)
