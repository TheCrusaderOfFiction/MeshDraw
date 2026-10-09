"""Checks proportion-preserving drawing fit and projection-based AI acceptance."""
import numpy as np
import trimesh
from silhouette_fit import drawing_fit

def main():
    front=np.zeros((160,160),bool);front[30:130,60:100]=True
    side=np.zeros_like(front);side[30:130,70:90]=True
    top=np.zeros_like(front);top[70:90,60:100]=True
    masks={'front':front,'back':front,'left':side,'right':side,'top':top,'bottom':top}
    mesh=drawing_fit(trimesh.creation.icosphere(),masks,64)
    assert mesh.is_watertight and mesh.metadata['drawingFit']['meanIoU']>.98
    assert mesh.metadata['generationMethod']=='drawing-guided visual hull'
    assert np.max(abs(mesh.extents/mesh.extents[1]-np.array([.4,1,.2])))<.03
    narrower=dict(masks);thin=np.zeros_like(top);thin[70:90,70:90]=True;narrower['top']=thin;narrower['bottom']=thin
    narrow=drawing_fit(None,narrower,64)
    assert narrow.extents[0]<mesh.extents[0]*.6
    assert narrow.metadata['paintBounds']['front'][2]-narrow.metadata['paintBounds']['front'][0]<25
    cube=drawing_fit(trimesh.creation.box(),masks,64)
    assert cube.metadata['generationMethod']=='AI refined with six-view drawing constraints'
    assert cube.metadata['drawingFit']['meanIoU']>=mesh.metadata['drawingFit']['meanIoU']-1e-6
    try:drawing_fit(None,dict(masks,top=np.zeros_like(top)),64);raise AssertionError('Empty top accepted')
    except ValueError:pass
    print('PASS: proportions; closed geometry; narrower top constrains width; paint alignment; AI retained only without silhouette loss; empty view rejection.')

if __name__=='__main__':main()
