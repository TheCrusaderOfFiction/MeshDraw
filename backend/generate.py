"""Local-only GPU shape generation followed by six-view constraints and FBX export."""
import argparse
import gc
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
os.environ['HF_HOME'] = str(ROOT / 'local-ai' / 'cache')
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['TRANSFORMERS_OFFLINE'] = '1'
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
sys.path.insert(0, str(ROOT / 'local-ai' / 'Hunyuan3D-2'))

def status(message):
    print(message, flush=True)

def silhouette(path):
    import numpy as np
    from PIL import Image
    from scipy import ndimage
    rgb = np.asarray(Image.open(path).convert('RGB'))
    ink = np.min(rgb, axis=2) < 220
    # Closing tiny gaps helps tablet strokes; larger gaps must be fixed by the artist.
    ink = ndimage.binary_closing(ink, iterations=2)
    mask = ndimage.binary_fill_holes(ink)
    # Only closed components define object dimensions. Isolated dots and open
    # strokes otherwise shift bounding-box centers and can erase the shared hull.
    components,count=ndimage.label(mask)
    enclosed=mask & ~ink
    filled_counts=np.bincount(components[enclosed],minlength=count+1)
    ink_counts=np.bincount(components[ink],minlength=count+1)
    keep=filled_counts>=np.maximum(16,ink_counts*.05);keep[0]=False
    mask=keep[components]
    ink &= mask
    if int(mask.sum()) < 100 or int(mask.sum()) < int(ink.sum()) * 1.2:
        raise ValueError(f'{path.stem.title()}: draw a closed outer outline before generating.')
    return rgb, mask

def silhouette_mesh(masks, resolution=64):
    """Build a closed visual hull from all six silhouettes when AI occupancy disagrees."""
    import numpy as np
    import trimesh
    coordinates=(np.arange(resolution)+.5)/resolution
    x,y,z=np.meshgrid(coordinates,coordinates,coordinates,indexing='ij')
    projections={'front':(x,1-y),'back':(1-x,1-y),'left':(z,1-y),
                 'right':(1-z,1-y),'top':(x,z),'bottom':(x,1-z)}
    keep=np.ones(x.shape,dtype=bool); boxes={}
    for name,(u,v) in projections.items():
        mask=masks[name];yy,xx=np.nonzero(mask)
        if not len(xx):raise ValueError('Empty silhouette: '+name)
        boxes[name]=(xx.max()-xx.min()+1,yy.max()-yy.min()+1)
        columns=np.clip(np.rint(xx.min()+u*(xx.max()-xx.min())).astype(int),0,mask.shape[1]-1)
        rows=np.clip(np.rint(yy.min()+v*(yy.max()-yy.min())).astype(int),0,mask.shape[0]-1)
        keep &= mask[rows,columns]
    if keep.sum()<32:
        raise ValueError('The six drawings have no usable shared volume. Check opposite-side orientation and close the outlines.')
    hull=trimesh.voxel.ops.matrix_to_marching_cubes(keep,pitch=1/resolution)
    width=np.mean([boxes[n][0]/boxes[n][1] for n in ('front','back')])
    depth=np.mean([boxes[n][0]/boxes[n][1] for n in ('left','right')])
    hull.apply_scale([width,1,depth]);hull.remove_unreferenced_vertices()
    hull.metadata['generationMethod']='six-view silhouette fallback'
    return hull

def constrain(mesh, masks, resolution=64):
    """Carve occupied AI voxels against the six normalized silhouettes, keeping a closed surface."""
    import numpy as np
    import trimesh
    bounds = mesh.bounds.copy()
    extent = bounds[1] - bounds[0]
    if np.min(extent) <= 0:
        raise ValueError('AI returned a flat or empty mesh.')
    voxels = mesh.voxelized(float(np.max(extent)) / resolution).fill()
    points = voxels.points
    unit = (points - bounds[0]) / extent
    projections = {
        'front': (unit[:, 0], 1 - unit[:, 1]),
        'back': (1 - unit[:, 0], 1 - unit[:, 1]),
        'left': (unit[:, 2], 1 - unit[:, 1]),
        'right': (1 - unit[:, 2], 1 - unit[:, 1]),
        'top': (unit[:, 0], unit[:, 2]),
        'bottom': (unit[:, 0], 1 - unit[:, 2]),
    }
    keep = np.ones(len(points), dtype=bool)
    for name, (u, v) in projections.items():
        mask = masks[name]
        yy, xx = np.nonzero(mask)
        columns = np.clip(np.rint(xx.min() + u * (xx.max() - xx.min())).astype(int), 0, mask.shape[1] - 1)
        rows = np.clip(np.rint(yy.min() + v * (yy.max() - yy.min())).astype(int), 0, mask.shape[0] - 1)
        keep &= mask[rows, columns]
    if keep.sum() < 32:
        status('AI shape does not fit the drawings. Building a shape directly from all six silhouettes…')
        return silhouette_mesh(masks,resolution)
    matrix = np.zeros(voxels.shape, dtype=bool)
    matrix[tuple(voxels.points_to_indices(points[keep]).T)] = True
    result = trimesh.voxel.ops.matrix_to_marching_cubes(matrix, pitch=voxels.pitch[0])
    result.apply_translation(voxels.transform[:3, 3])
    result.remove_unreferenced_vertices()
    return result

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--job', required=True)
    parser.add_argument('--blender', required=True)
    args = parser.parse_args()
    job_path = Path(args.job).resolve()
    job = json.loads(job_path.read_text())
    import numpy as np
    from PIL import Image
    status('Checking closed outlines in all six views…')
    images, masks = {}, {}
    for name in ['front', 'back', 'left', 'right', 'top', 'bottom']:
        rgb, mask = silhouette(job_path.parent / f'{name}.png')
        masks[name] = mask
        rgba = np.concatenate([rgb, (mask.astype(np.uint8) * 255)[..., None]], axis=2)
        if name in ['front', 'back', 'left', 'right']:
            images[name] = Image.fromarray(rgba)
    project=json.loads((job_path.parent/'project.meshdraw').read_text(encoding='utf-8'))
    extra_faces=project.get('ExtraFaces') or []
    from silhouette_fit import validate_faces
    validate_faces(extra_faces)
    extra_masks={face['Id']:silhouette(job_path.parent/(face['Id']+'.png'))[1] for face in extra_faces}
    import torch
    if not torch.cuda.is_available():
        raise RuntimeError('CUDA is unavailable. Install the NVIDIA driver and GPU-enabled PyTorch with setup-local.ps1.')
    free, total = torch.cuda.mem_get_info()
    if free < 3 * 1024**3:
        raise RuntimeError('Less than 3 GB of GPU memory is free. Close games and Unreal Editor, then retry.')
    status('Loading the local multiview model with CPU offloading…')
    from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline
    model = ROOT / 'local-ai' / 'models' / 'Hunyuan3D-2mv'
    previous_dtype = torch.get_default_dtype()
    torch.set_default_dtype(torch.float16)
    try:
        pipeline = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(
            str(model), subfolder='hunyuan3d-dit-v2-mv', device='cpu', dtype=torch.float16, variant='fp16')
    finally:
        torch.set_default_dtype(previous_dtype)
    # Some upstream releases expose offloading but omit the component registry it reads.
    if not hasattr(pipeline, 'components'):
        pipeline.components = {'conditioner': pipeline.conditioner, 'model': pipeline.model, 'vae': pipeline.vae}
    pipeline.enable_model_cpu_offload(device='cuda')
    # The upstream sampler allocates tensors using .device; the hooks move each component separately.
    pipeline.device = torch.device('cuda')
    steps = 20
    def progress(i, t, output):
        status(f'Generating geometry: {i + 1}/{steps} steps…')
    status('Generating from front, back, left, and right…')
    mesh = pipeline(image=images, num_inference_steps=steps, octree_resolution=192,
        num_chunks=4000, generator=torch.Generator(device='cuda').manual_seed(12345),
        callback=progress, callback_steps=1, output_type='trimesh')[0]
    if mesh is None or len(mesh.faces) == 0:
        raise RuntimeError('AI did not produce a mesh. Try clearer outlines.')
    del pipeline
    gc.collect()
    torch.cuda.empty_cache()
    status('Applying all six silhouette constraints…')
    status("Matching geometry to the drawings at high detail…")
    from silhouette_fit import drawing_fit
    status('Applying extra angled faces and interior cross-sections…' if extra_faces else 'Smoothing the drawing-guided surface…')
    mesh = drawing_fit(mesh, masks, resolution=192,extra_faces=extra_faces,extra_masks=extra_masks,edge_guides=project.get('EdgeGuides'))
    status("Drawing silhouette match: "+str(round(mesh.metadata["drawingFit"]["meanIoU"]*100,1))+"% (before export)")
    paint_bounds={}
    for name,mask in masks.items():
        yy,xx=np.nonzero(mask)
        paint_bounds[name]=[int(xx.min()),int(yy.min()),int(xx.max()),int(yy.max())]
    paint_bounds=mesh.metadata.get('paintBounds',paint_bounds)
    (job_path.parent/'paint_bounds.json').write_text(json.dumps(paint_bounds))
    obj_path = job_path.parent / 'shape.obj'
    mesh.export(obj_path)
    finish_generation(job_path, args.blender, mesh, dict(masks,**extra_masks))

def finish_generation(job_path, blender, mesh, masks):
    job_path=Path(job_path).resolve()
    job=json.loads(job_path.read_text())
    obj_path=job_path.parent/'shape.obj'
    if mesh.metadata.get('edgeGuides'):
        (job_path.parent/'edge_guides.json').write_text(json.dumps(mesh.metadata['edgeGuides']),encoding='utf-8')
    else:
        (job_path.parent/'edge_guides.json').unlink(missing_ok=True)
    if mesh.metadata.get('edgeGuidance',{}).get('marked'):
        report=mesh.metadata['edgeGuidance'];status(f"Edge marks: {report['applied']} applied; {len(report['ignored'])} outside usable boundaries.")
    if mesh.metadata.get('paintBounds'):
        (job_path.parent/'paint_bounds.json').write_text(json.dumps(mesh.metadata['paintBounds']))
    status('Exporting FBX with Blender…')
    subprocess.run([blender, '--background', '--factory-startup', '--python-exit-code', '1', '--python',
        str(ROOT / 'backend' / 'export_fbx.py'), '--', str(obj_path), job['outputType']], check=True)
    result = job_path.parent / 'mesh.fbx'
    if not result.is_file():
        raise RuntimeError('Blender finished without producing an FBX.')
    summary_path=job_path.parent/'export-summary.json'
    summary=json.loads(summary_path.read_text()) if summary_path.exists() else {}
    status('Packaging mesh, materials, skeleton, and layered project for Unreal…')
    from package_asset import package, unpack_checked
    if mesh.metadata.get('drawingFit'):
        (job_path.parent/'drawing-fit.json').write_text(json.dumps(mesh.metadata['drawingFit'],indent=2))
    bundle=package(job_path.parent)
    unpack_checked(bundle)
    # Publish success only once the complete, verified interchange file exists.
    (job_path.parent / 'result.json').write_text(json.dumps({
        'fbx': str(result), 'asset': str(bundle), 'outputType': job['outputType'], 'vertices': summary.get('renderVertices',len(mesh.vertices)),
        'vertexLimit':summary.get('vertexLimit',job.get('vertexLimit',50000)), 'geometryVertices':summary.get('geometryVertices',len(mesh.vertices)),
        'faces': summary.get('triangles',len(mesh.faces)), 'drawingFit': mesh.metadata.get('drawingFit'), 'generationMethod': mesh.metadata.get('generationMethod','AI with six-view constraints'), 'textures': summary.get('textures',{}), 'rig':summary.get('rig'),
        'roundProfiles':mesh.metadata.get('roundProfiles'),'edgeGuidance':mesh.metadata.get('edgeGuidance'),'edgeOptimization':summary.get('edgeOptimization'),
        'aiViews': ['front', 'back', 'left', 'right'], 'constraintViews': list(masks)}))
    status(f'Complete: {bundle}')
    return bundle

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        import traceback
        traceback.print_exc()
        status(f'Generation failed: {error}')
        sys.exit(1)
