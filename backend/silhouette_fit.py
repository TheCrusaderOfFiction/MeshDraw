"""Drawing-space visual hull and six-view projection scoring."""
import numpy as np
import trimesh
from scipy.ndimage import distance_transform_edt, map_coordinates, gaussian_filter, maximum_filter, label, maximum
from skimage.measure import marching_cubes
import re
NAMES=('front','back','left','right','top','bottom')
AXES={'front':(0,1),'back':(0,1),'left':(2,1),'right':(2,1),'top':(0,2),'bottom':(0,2)}

def reference_volume(masks,resolution,vertical_center=None):
    boxes={}; spans=np.zeros(3)
    for name in NAMES:
        yy,xx=np.nonzero(masks[name])
        if not len(xx):raise ValueError('Empty silhouette: '+name)
        boxes[name]=(xx.min(),yy.min(),xx.max(),yy.max())
        a,b=AXES[name];spans[a]=max(spans[a],xx.max()-xx.min()+1);spans[b]=max(spans[b],yy.max()-yy.min()+1)
    positions=(np.arange(resolution)+.5)/resolution-.5
    targets={};volume=np.ones((resolution,)*3,dtype=bool)
    for name in NAMES:
        a,b=AXES[name];box=boxes[name];mask=masks[name]
        u=positions*spans[a];v=positions*spans[b]
        if name in ('back','right'):u=-u
        if name in ('front','back','left','right','bottom'):v=-v
        columns=np.rint((box[0]+box[2])/2+u).astype(int)
        row_center=vertical_center if vertical_center is not None and b==1 else (box[1]+box[3])/2
        rows=np.rint(row_center+v).astype(int)
        valid=(rows[:,None]>=0)&(rows[:,None]<mask.shape[0])&(columns[None,:]>=0)&(columns[None,:]<mask.shape[1])
        target=mask[np.clip(rows,0,mask.shape[0]-1)[:,None],np.clip(columns,0,mask.shape[1]-1)[None,:]]&valid
        # Targets are indexed in increasing world axes; orientation was applied during sampling.
        targets[name]=target
        plane=target.T
        if a==0 and b==1:volume &= plane[:,:,None]
        elif a==2 and b==1:volume &= plane.T[None,:,:]
        else:volume &= plane[:,None,:]
    return volume,spans/spans.max(),targets

def score_volume(volume,targets):
    result={}
    for name in NAMES:
        a,b=AXES[name];other=next(i for i in range(3) if i not in (a,b))
        projection=volume.any(axis=other)
        remaining=[i for i in range(3) if i!=other]
        if remaining==[a,b]:projection=projection.T
        target=targets[name];union=(projection|target).sum()
        result[name]=float((projection&target).sum()/union) if union else 1.
    return result

from face_math import face_basis

def validate_faces(faces):
    if len(faces)>8:raise ValueError('At most eight extra faces are supported.')
    ids=set()
    for face in faces:
        identifier=face.get('Id','')
        if not re.fullmatch(r'custom_[a-f0-9]{32}',identifier) or identifier in ids:raise ValueError('Invalid extra face identifier.')
        ids.add(identifier)
        for key,limit in [('Yaw',180),('Pitch',90),('Offset',.95),('MoveX',2),('MoveY',2),('MoveZ',2),('Roll',180)]:
            value=face.get(key,0)
            if not isinstance(value,(int,float)) or not np.isfinite(value) or abs(value)>limit:raise ValueError('Invalid extra face '+key)
        if not isinstance(face.get('IsSection',False),bool):raise ValueError('Invalid extra face type.')
        if face.get('SectionMode','Whole') not in ('Whole','Sections','Blend') or not isinstance(face.get('ReverseSection',False),bool):raise ValueError('Invalid section behavior.')

def constrain_extra_faces(volume,faces,masks,extent=None,pixel_scale=None,vertical_center=None):
    """Orthographic silhouettes cut the hull; slices blend into adjacent volume.

    Extra canvases share the original drawing pixel scale, centered at the origin.
    Slice positions are fractions of the hull bounds along the plane normal.
    """
    validate_faces(faces)
    n=volume.shape[0];q=(np.arange(n)+.5)/n*2-1
    extent=np.ones(3) if extent is None else np.asarray(extent)
    x,y,z=q[:,None,None]*extent[0],q[None,:,None]*extent[1],q[None,None,:]*extent[2]
    for face in faces:
        if face.get('IsSection',False) and face.get('SectionMode','Whole')!='Whole':continue
        mask=masks[face['Id']]
        if not mask.any():raise ValueError('Empty extra face: '+face.get('Name',face['Id']))
        u,v,normal=face_basis(face)
        shift=np.array([face.get(k,0) for k in ('MoveX','MoveY','MoveZ')])*extent
        horizontal=x*u[0]+y*u[1]+z*u[2]-shift@u;vertical=x*v[0]+y*v[1]+z*v[2]-shift@v
        scale=pixel_scale or max(mask.shape)
        from face_math import section_image_center
        center=section_image_center(face,mask.shape,vertical_center)
        rows=center[1]-vertical*scale/2;columns=center[0]+horizontal*scale/2
        outside=distance_transform_edt(~mask)*2/scale
        # Negative distance also keeps thin contours through interpolation.
        field=outside-distance_transform_edt(mask)*2/scale
        sampled=map_coordinates(field,[rows.ravel(),columns.ravel()],order=1,mode='constant',cval=4).reshape(volume.shape)
        allowance=0
        if face.get('IsSection',False):
            depth=x*normal[0]+y*normal[1]+z*normal[2]-shift@normal
            radius=np.dot(np.abs(normal),extent)
            distance=np.abs(depth-face.get('Offset',0)*radius)
            # A gradual loft around the slice, rather than a one-voxel slab/notch.
            allowance=1.5*distance
        volume &= sampled<=allowance
    return volume

def score_extra_faces(volume,faces,masks,extent,pixel_scale,vertical_center=None):
    """Compare both angled projections and interior sections before retaining AI detail."""
    points=(np.argwhere(volume)+.5)/volume.shape[0]*2-1;points*=extent
    size=volume.shape[0];result={}
    for face in faces:
        u,v,normal=face_basis(face)
        shifted=points-np.array([face.get(k,0) for k in ('MoveX','MoveY','MoveZ')])*extent
        selected=shifted
        if face.get('IsSection',False):
            radius=np.dot(np.abs(normal),extent)
            selected=shifted[np.abs(shifted@normal-face.get('Offset',0)*radius)<=2/size]
        mask=masks[face['Id']]
        from face_math import section_image_center
        center=section_image_center(face,mask.shape,vertical_center)
        rows=center[1]-(selected@v)*pixel_scale/2
        columns=center[0]+(selected@u)*pixel_scale/2
        rows=np.rint(rows/(mask.shape[0]-1)*(size-1)).astype(int);columns=np.rint(columns/(mask.shape[1]-1)*(size-1)).astype(int)
        valid=(rows>=0)&(rows<size)&(columns>=0)&(columns<size)
        projection=np.zeros((size,size),bool);projection[rows[valid],columns[valid]]=True
        yy,xx=np.meshgrid(np.linspace(0,mask.shape[0]-1,size),np.linspace(0,mask.shape[1]-1,size),indexing='ij')
        target=map_coordinates(mask.astype(float),[yy,xx],order=0)>0
        union=(projection|target).sum();result[face['Id']]=float((projection&target).sum()/union) if union else 1.
    return result

SURFACE_SMOOTHING_ITERATIONS=480

def smooth_surface(mesh,resolution):
    """High-quality non-shrinking relaxation with a sub-voxel movement budget.

    Extra alternating passes remove small normal ripples without increasing the
    displacement limit, so additional quality does not erase thin drawing detail.
    """
    original=mesh.vertices.copy();bounds=mesh.bounds.copy()
    # Preserve actual creases while allowing broad curved surfaces to relax.
    creases=mesh.face_adjacency_edges[mesh.face_adjacency_angles>np.deg2rad(35)]
    pinned=np.unique(creases) if len(creases) else np.empty(0,dtype=int)
    operator=trimesh.smoothing.laplacian_calculation(mesh,pinned_vertices=pinned)
    trimesh.smoothing.filter_taubin(mesh,lamb=.5,nu=.5,iterations=SURFACE_SMOOTHING_ITERATIONS,laplacian_operator=operator)
    delta=mesh.vertices-original;length=np.linalg.norm(delta,axis=1)
    limit=.75/resolution
    mesh.vertices=original+delta*np.minimum(1,limit/np.maximum(length,1e-12))[:,None]
    mesh.vertices=np.clip(mesh.vertices,bounds[0],bounds[1])
    return mesh

def reconstruct_surface(volume,resolution,guidance=None,extent=None,cylinders=None):
    """Extract a rounded sub-voxel surface, rather than marching a binary grid.

    Padding closes faces on the drawing bounds. A smoothed signed distance field
    suppresses grid stairs before triangulation without merging disconnected parts.
    Work in float32 to keep the laptop's memory budget bounded.
    """
    padded=np.pad(volume,4)
    field=distance_transform_edt(padded).astype(np.float32)
    thin=padded & (maximum_filter(field,size=7)<=2)
    components,count=label(padded)
    radii=maximum(field,components,index=np.arange(count+1))
    thin |= padded & (radii[components]<=6)
    del components,radii
    field-=distance_transform_edt(~padded).astype(np.float32)
    raw=field.copy() if guidance else None
    gaussian_filter(field,sigma=3,output=field,mode='nearest')
    # Thin features must not disappear after filtering. Restore their center cells.
    field[thin]=np.maximum(field[thin],.05)
    if guidance:
        from edge_guidance import guide_distance_field
        guide_distance_field(field,raw,guidance,extent,resolution)
    if cylinders:
        from round_profiles import reconstruct_cylinders
        reconstruct_cylinders(field,cylinders,extent,resolution,volume)
    vertices,faces,_,_=marching_cubes(field,level=0,spacing=(1/resolution,)*3,allow_degenerate=False)
    vertices+=(.5-4)/resolution
    mesh=trimesh.Trimesh(vertices=vertices,faces=faces,process=True)
    # The field's gradient points inward; explicitly keep outward winding.
    if mesh.volume<0:mesh.invert()
    smooth_surface(mesh,resolution)
    return retain_volume_components(mesh,volume,resolution)


def retain_volume_components(mesh,volume,resolution,extent=None):
    """Keep one principal surface per original drawing component."""
    extent=np.ones(3) if extent is None else np.asarray(extent)
    parts=mesh.split(only_watertight=False)
    if len(parts)>1:
        labels,_=label(volume);owners={}
        for part in parts:
            sample=np.clip(np.rint(part.vertices/extent*resolution-.5).astype(int),0,resolution-1)
            ids=labels[tuple(sample.T)];ids=ids[ids>0]
            if not len(ids):continue
            owner=int(np.bincount(ids).argmax())
            if owner not in owners or len(part.faces)>len(owners[owner].faces):owners[owner]=part
        if owners:mesh=trimesh.util.concatenate(list(owners.values()))
    return mesh

def drawing_fit(ai_mesh,masks,resolution=192,extra_faces=None,extra_masks=None,edge_guides=None):
    from section_regions import active_sections,section_volume
    from face_math import section_vertical_center,section_image_center
    from outline_guidance import straighten_outlines
    combined=dict(masks,**(extra_masks or {}))
    corrected,edge_guides,outline_report=straighten_outlines(combined,edge_guides)
    masks={name:corrected[name] for name in masks}
    extra_masks={name:corrected[name] for name in (extra_masks or {})}
    from round_profiles import refine_round_profiles,refine_round_surface
    original_masks=masks
    masks,circles=refine_round_profiles(masks)
    # Explicit straight/corner annotations take priority over inferred circles.
    marked={(name.lower() if name.lower() in AXES else name) for name,marks in (edge_guides or {}).items() if marks}
    circles={name:profiles for name,profiles in circles.items() if name not in marked}
    for name in marked:
        if name in original_masks:masks[name]=original_masks[name]
    vertical_center=section_vertical_center(masks) if active_sections(extra_faces) else None
    volume,extent,targets=reference_volume(masks,resolution,vertical_center)
    from face_math import register_section_profiles
    extra_faces,registration=register_section_profiles(extra_faces,extra_masks,extent)
    pixel_scale=max(max(np.ptp(np.nonzero(mask)[0])+1,np.ptp(np.nonzero(mask)[1])+1) for mask in masks.values())
    if active_sections(extra_faces):
        validate_faces(extra_faces)
        volume=section_volume(targets,extra_faces,extra_masks or {},extent,pixel_scale,resolution,vertical_center)
    if extra_faces:volume=constrain_extra_faces(volume,extra_faces,extra_masks or {},extent,pixel_scale,vertical_center)
    from round_profiles import preserve_round_sections
    volume,round_sections,cylinders=preserve_round_sections(volume,circles,masks,extent,extra_faces,vertical_center,pixel_scale,return_cylinders=True)
    if volume.sum()<32:raise ValueError('The drawings have no usable shared volume. Check opposite views and proportions.')
    drawing_scores=score_volume(volume,targets)
    if extra_faces:drawing_scores.update(score_extra_faces(volume,extra_faces,extra_masks,extent,pixel_scale,vertical_center))
    # An AI shape cannot add volume that changes the silhouettes. Only retain it if its projections fit equally well.
    candidates=[(volume,'drawing-guided visual hull',drawing_scores)]
    if ai_mesh is not None and len(ai_mesh.faces)>0:
        bounds=ai_mesh.bounds;size=bounds[1]-bounds[0]
        if np.min(size)>0:
            normalized=ai_mesh.copy();normalized.apply_translation(-bounds[0]);normalized.apply_scale(1/size)
            voxels=normalized.voxelized(1/resolution).fill();indices=np.floor(voxels.points*resolution).astype(int)
            valid=((indices>=0)&(indices<resolution)).all(axis=1);ai=np.zeros_like(volume)
            ai[tuple(indices[valid].T)]=True;ai &= volume
            scores=score_volume(ai,targets)
            if extra_faces:scores.update(score_extra_faces(ai,extra_faces,extra_masks,extent,pixel_scale,vertical_center))
            if ai.sum()>=32 and all(scores[name]>=drawing_scores[name]-1e-6 for name in drawing_scores):
                candidates.append((ai,'AI refined with six-view drawing constraints',scores))
    selected,method,scores=candidates[-1]
    from edge_guidance import prepare_guidance,pin_corners
    guidance,guide_report=prepare_guidance(edge_guides,dict(masks,**(extra_masks or {})),extent,extra_faces,vertical_center)
    mesh=reconstruct_surface(selected,resolution,guidance,extent,cylinders)
    mesh.apply_scale(extent);mesh.remove_unreferenced_vertices()
    if guidance:
        from outline_guidance import straighten_surface
        mesh=straighten_surface(mesh,guidance,resolution)
        mesh=pin_corners(mesh,guidance,extent,resolution)
    # Preserve marked corner detail through the outline field rather than
    # collapsing nearby 3D vertices toward a potentially inset marker.
    if circles:
        mesh,round_vertices=refine_round_surface(mesh,circles,masks,extent,extra_faces,vertical_center,pixel_scale,resolution)
    else:round_vertices=0
    from planar_surfaces import flatten_supported_planes
    mesh,planar_report=flatten_supported_planes(mesh,guidance,resolution,cylinders)
    metadata=dict(mesh.metadata)
    mesh=retain_volume_components(mesh,selected,resolution,extent)
    mesh.metadata.update(metadata)
    mesh.metadata['planarSurfaces']=planar_report
    mesh.metadata['roundProfiles']={'circles':sum(len(p) for p in circles.values()),'correctedVertices':round_vertices,'preservedSections':round_sections,'analyticCylinders':len(cylinders)}
    mesh.metadata['edgeGuides']=[c['meta'] for c in guidance]
    mesh.metadata['edgeGuidance']=dict(guide_report,outlineCorrection=outline_report)
    pixel_scale=max(max(np.ptp(np.nonzero(mask)[0])+1,np.ptp(np.nonzero(mask)[1])+1) for mask in masks.values())
    paint_bounds={}
    unit_bounds=mesh.bounds/extent
    for name in NAMES:
        yy,xx=np.nonzero(masks[name]);a,b=AXES[name]
        u=unit_bounds[:,a]-.5;v=unit_bounds[:,b]-.5
        if name in ('back','right'):u=-u
        if name in ('front','back','left','right','bottom'):v=-v
        columns=(xx.min()+xx.max())/2+u*extent[a]*pixel_scale
        row_center=vertical_center if vertical_center is not None and b==1 else (yy.min()+yy.max())/2
        rows=row_center+v*extent[b]*pixel_scale
        paint_bounds[name]=[float(columns.min()),float(rows.min()),float(columns.max()),float(rows.max())]
    if extra_faces:
        paint_bounds['_extra']={'extent':extent.tolist(),'pixelScale':int(pixel_scale),'low':mesh.bounds[0].tolist(),'high':mesh.bounds[1].tolist()}
        paint_bounds['_extra']['profileMirrors']={f['Id']:f.get('_MirrorProfile',False) for f in extra_faces}
        paint_bounds['_extra']['imageCenters']={f['Id']:section_image_center(f,extra_masks[f['Id']].shape,vertical_center).tolist() for f in extra_faces}
    mesh.metadata['paintBounds']=paint_bounds
    mesh.metadata['generationMethod']=method
    mesh.metadata['profileRegistration']=registration
    mesh.metadata['drawingFit']={'resolution':resolution,'silhouetteIoU':scores,'meanIoU':float(np.mean(list(scores.values()))),'centering':('sectioned profiles share the full-object vertical anchor; shared axis scale preserves proportions' if vertical_center is not None else 'each outline centered; shared axis scale preserves relative view proportions'),'extraFaceCount':len(extra_faces or []),'smoothing':{'method':'signed-distance reconstruction and crease-preserving Taubin relaxation','creaseAngleDegrees':35,'fieldSigmaInVoxels':3,'iterations':SURFACE_SMOOTHING_ITERATIONS,'maximumRelaxationDisplacementInVoxels':.75},'note':'Score measures constrained voxel projections before distance-field smoothing/export; hidden concavities are not determined by outlines'}
    return mesh
