"""CPU regression checks for a valid drawing hull when AI occupancy cannot be carved."""
import numpy as np
from types import SimpleNamespace
from generate import constrain, silhouette_mesh

def main():
    y,x=np.mgrid[:128,:128]
    circle=(x-64)**2+(y-64)**2<45**2
    names=('front','back','left','right','top','bottom')
    masks={name:circle for name in names}
    corners=np.array([[x,y,z] for x in (0,1) for y in (0,1) for z in (0,1)])
    voxels=SimpleNamespace(points=corners);voxels.fill=lambda:voxels
    incompatible_ai=SimpleNamespace(bounds=np.array([[0.,0.,0.],[1.,1.,1.]]),voxelized=lambda pitch:voxels)
    mesh=constrain(incompatible_ai,masks,32)
    assert mesh.is_watertight and mesh.volume>0
    assert mesh.metadata['generationMethod']=='six-view silhouette fallback'
    narrower=dict(masks)
    narrower['top']=circle & (abs(x-64)>12)
    narrowed=silhouette_mesh(narrower,32)
    assert narrowed.is_watertight and 0<narrowed.volume<mesh.volume
    try:silhouette_mesh(dict(masks,top=np.zeros_like(circle)));raise AssertionError('Empty drawing accepted')
    except ValueError:pass
    print('PASS: AI mismatch fallback; closed usable mesh; top-view constraint; empty drawings rejected.')

if __name__=='__main__':main()
