"""Measure actual surface stair removal and preserve thin/disconnected features."""
import json
from pathlib import Path
import numpy as np
import trimesh
from unittest.mock import patch
from silhouette_fit import reference_volume,reconstruct_surface,smooth_surface


def main():
    n=96;y,x=np.mgrid[:256,:256]
    circle=(x-127.5)**2+(y-127.5)**2<100**2
    volume,_,_=reference_volume({name:circle for name in ('front','back','left','right','top','bottom')},n)
    old=trimesh.voxel.ops.matrix_to_marching_cubes(volume,pitch=1/n)
    old.apply_translation(np.full(3,.5/n));smooth_surface(old,n)
    # Compare against the previous 240-pass smoothing on exactly the same surface.
    with patch('silhouette_fit.smooth_surface',lambda mesh,resolution:mesh):
        raw=reconstruct_surface(volume,n)
    previous=raw.copy();original=previous.vertices.copy();bounds=previous.bounds.copy()
    trimesh.smoothing.filter_taubin(previous,lamb=.5,nu=.5,iterations=240)
    delta=previous.vertices-original;length=np.linalg.norm(delta,axis=1)
    previous.vertices=np.clip(original+delta*np.minimum(1,.75/n/np.maximum(length,1e-12))[:,None],bounds[0],bounds[1])
    new=smooth_surface(raw.copy(),n)
    previous_ripples=float(np.mean(previous.face_adjacency_angles**2))
    new_ripples=float(np.mean(new.face_adjacency_angles**2))
    assert new_ripples<previous_ripples*.97,'Extra passes did not improve surface smoothness'
    assert abs(new.volume/previous.volume-1)<.005,'Higher quality changed the shape'
    assert np.max(np.linalg.norm(new.vertices-raw.vertices,axis=1))<=.75/n+1e-10

    old_steps=float(np.mean(old.face_adjacency_angles>np.deg2rad(10)))
    new_steps=float(np.mean(new.face_adjacency_angles>np.deg2rad(10)))
    assert new_steps<old_steps*.2,'Grid steps were not materially reduced'
    assert new.is_watertight and new.is_winding_consistent and new.volume>0
    assert abs(new.volume/old.volume-1)<.02,'Smoothing shrank the drawn shape'
    assert np.max(abs(new.extents-old.extents))<2/n,'Smoothing changed drawing proportions'
    box=trimesh.creation.box()
    box_original=box.vertices.copy();smooth_surface(box,n)
    assert np.array_equal(box.vertices,box_original),'Hard corner vertices moved'
    assert box.is_watertight and box.is_winding_consistent
    thin=np.zeros((32,32,32),bool);thin[5:27,5:27,15:17]=True
    plate=reconstruct_surface(thin,32)
    assert plate.is_watertight and plate.volume>0,'Thin feature vanished'
    separated=np.zeros((32,32,32),bool);separated[4:12,4:12,4:12]=True;separated[20:28,20:28,20:28]=True
    assert len(reconstruct_surface(separated,32).split())==2,'Separate parts merged'
    out=Path('outputs/surface-quality-checks');out.mkdir(parents=True,exist_ok=True)
    old.export(out/'previous-surface.obj');new.export(out/'smooth-surface.obj')
    report={'normalRippleReductionPercent':round((1-new_ripples/previous_ripples)*100,1),'previousSharpEdgeFraction':old_steps,'newSharpEdgeFraction':new_steps,'gridStepReductionPercent':round((1-new_steps/old_steps)*100,1),'volumeChangePercent':round((new.volume/old.volume-1)*100,3),'watertight':bool(new.is_watertight)}
    (out/'surface-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('PASS:',json.dumps(report))


if __name__=='__main__':main()
