"""Rebuild a previous drawing-hull result with the improved surface extractor.

Preserves the original generation folder; only supported for drawing-hull results.
"""
import argparse
import json
import os
import shutil
from pathlib import Path
from generate import ROOT,silhouette,finish_generation
from silhouette_fit import drawing_fit


def main():
    parser=argparse.ArgumentParser();parser.add_argument('source');parser.add_argument('destination');args=parser.parse_args()
    source=Path(args.source).resolve();destination=Path(args.destination).resolve()
    if destination.exists():raise ValueError('Destination already exists; keep previous results intact.')
    if json.loads((source/'result.json').read_text())['generationMethod']!='drawing-guided visual hull':
        raise ValueError('This rebuild requires an original drawing-hull result. Regenerate AI results in the editor.')
    destination.mkdir(parents=True)
    for file in source.iterdir():
        if file.suffix=='.png' or file.name in ('project.meshdraw','skeleton.json','job.json','manifest.json'):shutil.copy2(file,destination/file.name)
    project=json.loads((destination/'project.meshdraw').read_text(encoding='utf-8-sig'))
    faces=project.get('ExtraFaces') or []
    masks={name:silhouette(destination/(name+'.png'))[1] for name in ('front','back','left','right','top','bottom')}
    extras={face['Id']:silhouette(destination/(face['Id']+'.png'))[1] for face in faces}
    mesh=drawing_fit(None,masks,192,faces,extras,project.get('EdgeGuides'));mesh.export(destination/'shape.obj')
    resources=destination/'blender-runtime';resources.mkdir();os.environ['BLENDER_USER_RESOURCES']=str(resources)
    blender=next(Path('C:/Program Files/Blender Foundation').glob('*/blender.exe'))
    bundle=finish_generation(destination/'job.json',str(blender),mesh,dict(masks,**extras))
    print('Rebuilt smooth asset:',bundle,flush=True)


if __name__=='__main__':main()
