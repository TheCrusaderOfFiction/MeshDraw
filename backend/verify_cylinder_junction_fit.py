"""Regenerate a saved drawing without loading the GPU model, for geometry checks."""
import json,sys
from pathlib import Path
from generate import silhouette
from silhouette_fit import drawing_fit
folder=Path(sys.argv[1])
project=json.loads((folder/'project.meshdraw').read_text(encoding='utf-8-sig'))
masks={n:silhouette(folder/(n+'.png'))[1] for n in ('front','back','left','right','top','bottom')}
extra=project.get('ExtraFaces') or []
extra_masks={f['Id']:silhouette(folder/(f['Id']+'.png'))[1] for f in extra}
mesh=drawing_fit(None,masks,resolution=192,extra_faces=extra,extra_masks=extra_masks,edge_guides=project.get('EdgeGuides'))
assert mesh.is_watertight and mesh.is_winding_consistent and mesh.volume>0
mesh.export(folder/'shape.obj')
for name,key in [('round_surfaces.json','roundSurfaces'),('edge_guides.json','edgeGuides'),('paint_bounds.json','paintBounds'),('drawing-fit.json','drawingFit')]:
    (folder/name).write_text(json.dumps(mesh.metadata[key],indent=2),encoding='utf-8')
print('PASS: regenerated closed drawing surface',len(mesh.vertices),mesh.metadata['roundSurfaces'],flush=True)
