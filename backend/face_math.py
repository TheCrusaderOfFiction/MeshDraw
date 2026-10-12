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
    center+=np.asarray(face.get('_ImageCenterShift',[0,0]))
    return center

def face_basis(face):
    yaw,pitch=np.deg2rad([face['Yaw'],face['Pitch']])
    u=np.array([np.cos(yaw),0,-np.sin(yaw)])
    v=np.array([-np.sin(yaw)*np.sin(pitch),np.cos(pitch),-np.cos(yaw)*np.sin(pitch)])
    normal=np.array([np.sin(yaw)*np.cos(pitch),np.sin(pitch),np.cos(yaw)*np.cos(pitch)])
    roll=np.deg2rad(face.get('Roll',0))
    horizontal=u*np.cos(roll)+v*np.sin(roll)
    if face.get('_MirrorProfile',False):horizontal=-horizontal
    return horizontal,v*np.cos(roll)-u*np.sin(roll),normal




def register_section_profiles(faces,masks,extent):
    """Register near-identical mirrored profiles on opposite interior planes.

    Work on copies; stored drawings and deliberate plane transforms stay intact.
    Require the same orientation, opposite plane positions, matching dimensions,
    small drawing offsets and strong evidence that mirroring is intended.
    """
    result=[dict(face) for face in (faces or [])];report=[];used=set()
    for i,a in enumerate(result):
        if i in used or not a.get('IsSection') or a.get('SectionMode','Whole')=='Whole':continue
        au,av,an=face_basis(a)
        apos=a.get('Offset',0)*(abs(an)@extent)+(np.array([a.get(k,0) for k in ('MoveX','MoveY','MoveZ')])*extent)@an
        for j in range(i+1,len(result)):
            b=result[j]
            if j in used or not b.get('IsSection') or b.get('SectionMode','Whole')=='Whole':continue
            bu,bv,bn=face_basis(b)
            bpos=b.get('Offset',0)*(abs(bn)@extent)+(np.array([b.get(k,0) for k in ('MoveX','MoveY','MoveZ')])*extent)@bn
            if apos*bpos>=0 or au@bu<.999 or av@bv<.999:continue
            def crop(face):
                mask=masks[face['Id']];y,x=np.nonzero(mask)
                if not len(x):return None,None
                return mask[y.min():y.max()+1,x.min():x.max()+1],np.array([(x.min()+x.max())/2,(y.min()+y.max())/2])
            am,ac=crop(a);bm,bc=crop(b)
            if am is None or bm is None or am.shape!=bm.shape or min(am.shape)<8 or np.max(abs(ac-bc))>8:continue
            direct=(am&bm).sum()/max(1,(am|bm).sum())
            mirrored=(am&bm[:,::-1]).sum()/max(1,(am|bm[:,::-1]).sum())
            if mirrored<.97 or mirrored<direct+.02:continue
            common=(ac+bc)/2
            canvas_a=(masks[a['Id']].shape[1]-1)/2;canvas_b=(masks[b['Id']].shape[1]-1)/2
            if abs(common[0]-(canvas_a+canvas_b)/2)>8:continue
            a['_ImageCenterShift']=[float(ac[0]-canvas_a),float(ac[1]-common[1])]
            b['_ImageCenterShift']=[float(bc[0]-canvas_b),float(bc[1]-common[1])]
            b['_MirrorProfile']=True
            # Near-identical opposite sections must share exactly one boundary;
            # independent pixel contours otherwise leave a step at the region seam.
            target=masks[b['Id']].copy();target[:]=False
            rows,columns=np.nonzero(masks[b['Id']]);top=int(rows.min());left=int(columns.min())
            target[top:top+am.shape[0],left:left+am.shape[1]]=am[:,::-1]
            masks[b['Id']]=target
            report.append(dict(first=a['Id'],second=b['Id'],mirroredIoU=float(mirrored),pixelOffset=(bc-ac).tolist()))
            used.update((i,j));break
    return result,report
