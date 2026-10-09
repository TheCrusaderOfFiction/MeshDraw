"""Custom face geometry and bounded surface relaxation regression checks."""
import numpy as np
from unittest.mock import patch
from silhouette_fit import drawing_fit,constrain_extra_faces,validate_faces

def face(yaw=0,offset=0,section=False,identifier='a'):
    return dict(Id='custom_'+identifier*32,Name='Test face',Yaw=yaw,Pitch=0,Offset=offset,IsSection=section)

def main():
    n=64;full=np.ones((n,)*3,bool);mask=np.zeros((128,128),bool);mask[:,48:80]=True
    a=constrain_extra_faces(full.copy(),[face()],{face()['Id']:mask})
    b=constrain_extra_faces(full.copy(),[face(90)],{face()['Id']:mask})
    assert a.sum()<full.sum()*.35 and b.sum()==a.sum()
    assert a.any(axis=(1,2)).sum()<a.any(axis=(0,1)).sum()
    assert b.any(axis=(0,1)).sum()<b.any(axis=(1,2)).sum()
    diagonal=constrain_extra_faces(full.copy(),[face(45)],{face()['Id']:mask})
    assert diagonal.sum()<full.sum()*.5 and diagonal.sum()>0
    yy,xx=np.mgrid[:128,:128];circle=(xx-63.5)**2+(yy-63.5)**2<24**2
    center=constrain_extra_faces(full.copy(),[face(section=True)],{face()['Id']:circle})
    assert center[:,:,32].sum()<center[:,:,0].sum()*.3
    shifted=constrain_extra_faces(full.copy(),[face(offset=.5,section=True)],{face()['Id']:circle})
    assert shifted[:,:,48].sum()<shifted[:,:,32].sum()*.4
    f1,f2=face(offset=-.4,section=True),face(offset=.4,section=True,identifier='b')
    sandwich=constrain_extra_faces(full.copy(),[f1,f2],{f1['Id']:circle,f2['Id']:circle})
    assert sandwich[:,:,19].sum()<sandwich[:,:,32].sum() and sandwich[:,:,44].sum()<sandwich[:,:,32].sum()
    masks={name:np.ones((128,128),bool) for name in ('front','back','left','right','top','bottom')}
    smooth=drawing_fit(None,masks,n)
    with patch('silhouette_fit.smooth_surface',lambda mesh,resolution:mesh):rough=drawing_fit(None,masks,n)
    assert smooth.is_watertight and np.isfinite(smooth.vertices).all() and smooth.area>0
    assert np.linalg.norm(smooth.vertices-rough.vertices,axis=1).max()<=.75/n+1e-8
    # Both meshes now already have distance-field smoothing. The extra bounded
    # relaxation should improve that surface, rather than remove binary stairs.
    assert np.mean(smooth.face_adjacency_angles**2)<np.mean(rough.face_adjacency_angles**2)
    guided=drawing_fit(None,masks,n,[face(section=True)],{face()['Id']:circle})
    assert guided.is_watertight and guided.volume<smooth.volume*.95
    assert guided.metadata['drawingFit']['extraFaceCount']==1 and '_extra' in guided.metadata['paintBounds']
    try:validate_faces([face(yaw=float('nan'))]);raise AssertionError('NaN angle accepted')
    except ValueError:pass
    try:validate_faces([face(),face()]);raise AssertionError('Duplicate accepted')
    except ValueError:pass
    try:constrain_extra_faces(full.copy(),[face()],{face()['Id']:np.zeros_like(mask)});raise AssertionError('Empty extra face accepted')
    except ValueError:pass
    print('PASS: arbitrary yaw constraints; diagonal silhouette; centered and offset slices; multiple sandwich slices; bounded smoother closed mesh; extra face metadata; invalid and empty face rejection.')
if __name__=='__main__':main()
