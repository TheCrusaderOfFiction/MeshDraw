import numpy as np

def section_vertical_center(masks):
    """Keep partial side profiles at their drawn height in a sectioned object."""
    candidates=[]
    for name in ('front','back','left','right'):
        yy,xx=np.nonzero(masks[name])
        if len(yy):candidates.append((np.ptp(yy)+1,(yy.min()+yy.max())/2))
    longest=max(c[0] for c in candidates)
    return float(np.mean([center for span,center in candidates if span>=longest*.98]))

def section_image_center(face,shape,vertical_center):
    center=np.array([(shape[1]-1)/2,(shape[0]-1)/2])
    if vertical_center is not None:
        _,up,_=face_basis(face)
        center[1]+=(vertical_center-center[1])*up[1]
    return center

def face_basis(face):
    yaw,pitch=np.deg2rad([face['Yaw'],face['Pitch']])
    u=np.array([np.cos(yaw),0,-np.sin(yaw)])
    v=np.array([-np.sin(yaw)*np.sin(pitch),np.cos(pitch),-np.cos(yaw)*np.sin(pitch)])
    normal=np.array([np.sin(yaw)*np.cos(pitch),np.sin(pitch),np.cos(yaw)*np.cos(pitch)])
    roll=np.deg2rad(face.get('Roll',0))
    return u*np.cos(roll)+v*np.sin(roll),v*np.cos(roll)-u*np.sin(roll),normal


