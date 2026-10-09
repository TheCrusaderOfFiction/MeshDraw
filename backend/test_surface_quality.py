"""Measure actual surface stair removal and preserve thin/disconnected features."""
import json
from pathlib import Path
import numpy as np
import trimesh
from silhouette_fit import reference_volume,reconstruct_surface,smooth_surface


def main():
    n=96;y,x=np.mgrid[:256,:256]
    circle=(x-127.5)**2+(y-127.5)**2<100**2
    volume,_,_=reference_volume({name:circle for name in ('front','back','left','right','top','bottom')},n)
    old=trimesh.voxel.ops.matrix_to_marching_cubes(volume,pitch=1/n)
    old.apply_translation(np.full(3,.5/n));smooth_surface(old,n)
    new=reconstruct_surface(volume,n)
    old_steps=float(np.mean(old.face_adjacency_angles>np.deg2rad(10)))
    new_steps=float(np.mean(new.face_adjacency_angles>np.deg2rad(10)))
    assert new_steps<old_steps*.2,'Grid steps were not materially reduced'
    assert new.is_watertight and new.is_winding_consistent and new.volume>0
    assert abs(new.volume/old.volume-1)<.02,'Smoothing shrank the drawn shape'
    assert np.max(abs(new.extents-old.extents))<2/n,'Smoothing changed drawing proportions'
    thin=np.zeros((32,32,32),bool);thin[5:27,5:27,15:17]=True
    plate=reconstruct_surface(thin,32)
    assert plate.is_watertight and plate.volume>0,'Thin feature vanished'
    separated=np.zeros((32,32,32),bool);separated[4:12,4:12,4:12]=True;separated[20:28,20:28,20:28]=True
    assert len(reconstruct_surface(separated,32).split())==2,'Separate parts merged'
    out=Path('outputs/surface-quality-checks');out.mkdir(parents=True,exist_ok=True)
    old.export(out/'previous-surface.obj');new.export(out/'smooth-surface.obj')
    report={'previousSharpEdgeFraction':old_steps,'newSharpEdgeFraction':new_steps,'gridStepReductionPercent':round((1-new_steps/old_steps)*100,1),'volumeChangePercent':round((new.volume/old.volume-1)*100,3),'watertight':bool(new.is_watertight)}
    (out/'surface-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('PASS:',json.dumps(report))


if __name__=='__main__':main()
