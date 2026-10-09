"""CPU projection bake from six independently painted property maps into a UV atlas."""
from pathlib import Path
import json
import math
import bpy
import numpy as np
from mathutils import Vector

VIEWS = ('front','back','left','right','top','bottom')
DEFAULTS = {'basecolor': (.65,.65,.65), 'roughness': (.5,.5,.5), 'metallic': (0,0,0),
            'emission': (0,0,0), 'height': (.5,.5,.5)}

def read_pixels(path):
    image = bpy.data.images.load(str(path), check_existing=False)
    image.colorspace_settings.name = 'Non-Color'
    values = np.empty(image.size[0]*image.size[1]*4, dtype=np.float32)
    image.pixels.foreach_get(values)
    pixels = values.reshape(image.size[1],image.size[0],4)[::-1].copy()
    bpy.data.images.remove(image)
    return pixels

def prepare_uv(mesh):
    triangulate = mesh.modifiers.new('Texture triangles','TRIANGULATE')
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.02)
    bpy.ops.object.mode_set(mode='OBJECT')


def paint_material(mesh, folder, size=1024, prepared=False):
    folder = Path(folder)
    project_path=folder/'project.meshdraw'
    extras=(json.loads(project_path.read_text(encoding='utf-8')).get('ExtraFaces') or []) if project_path.exists() else []
    views=VIEWS+tuple(face['Id'] for face in extras)
    maps = {}
    for kind in DEFAULTS:
        channels = {}
        for view in views:
            path = folder / f'{view}_{kind}.png'
            if path.exists():
                pixels = read_pixels(path)
                if np.any(pixels[...,3] > 0): channels[view] = pixels
        if channels: maps[kind] = channels
    if not maps:
        return {}
    size = max(128,min(2048,int(size)))
    bounds_path = folder / 'paint_bounds.json'
    if bounds_path.exists():
        bounds = json.loads(bounds_path.read_text())
    else:
        bounds = {}
        for view in VIEWS:
            path = folder / f'{view}.png'
            if path.exists():
                artwork = read_pixels(path)
                ys,xs = np.nonzero(np.min(artwork[...,:3],axis=2)<.86)
                if len(xs): bounds[view] = [int(xs.min()),int(ys.min()),int(xs.max()),int(ys.max())]
    if not prepared:prepare_uv(mesh)
    uv = mesh.data.uv_layers.active
    points = np.array([tuple(vertex.co) for vertex in mesh.data.vertices])
    low, high = points.min(axis=0), points.max(axis=0)
    unit = (points-low) / np.maximum(high-low,1e-8)
    # OBJ import maps source X,Y,Z into Blender X,-Z,Y.
    u, y, z = unit[:,0], unit[:,2], 1-unit[:,1]
    projections = {'front': np.column_stack((u,1-y)), 'back': np.column_stack((1-u,1-y)),
        'left': np.column_stack((z,1-y)), 'right': np.column_stack((1-z,1-y)),
        'top': np.column_stack((u,z)), 'bottom': np.column_stack((u,1-z))}
    source_axes=np.column_stack((u,y,z))
    extra_space=bounds.get('_extra')
    source_unit=source_axes*2-1
    if extra_space:
        original=np.asarray(extra_space['low'])+source_axes*(np.asarray(extra_space['high'])-extra_space['low'])
        source_unit=original*2-np.asarray(extra_space['extent'])
    extra_projections={};extra_normals={}
    for face in extras:
        yaw,pitch=np.deg2rad([face['Yaw'],face['Pitch']])
        horizontal=np.array([np.cos(yaw),0,-np.sin(yaw)])
        vertical=np.array([-np.sin(yaw)*np.sin(pitch),np.cos(pitch),-np.cos(yaw)*np.sin(pitch)])
        normal=np.array([np.sin(yaw)*np.cos(pitch),np.sin(pitch),np.cos(yaw)*np.cos(pitch)])
        extra_projections[face['Id']]=np.column_stack((source_unit@horizontal,source_unit@vertical))
        # Source X,Y,Z -> Blender X,-Z,Y.
        extra_normals[face['Id']]=normal[[0,2,1]]*np.array([1,-1,1])
    atlases = {kind: np.broadcast_to(np.array((*DEFAULTS[kind],1),dtype=np.float32),(size,size,4)).copy() for kind in maps}
    covered = np.zeros((size,size),dtype=bool)
    for polygon in mesh.data.polygons:
        indices = np.array(polygon.vertices)
        coords = np.array([tuple(uv.data[loop].uv) for loop in polygon.loop_indices])*size
        a,b,c = coords
        x0,y0 = np.maximum(np.floor(coords.min(axis=0)).astype(int),0)
        x1,y1 = np.minimum(np.ceil(coords.max(axis=0)).astype(int),size-1)
        if x1<x0 or y1<y0: continue
        denominator = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(denominator)<1e-8: continue
        yy,xx = np.mgrid[y0:y1+1,x0:x1+1]
        px,py = xx+.5, yy+.5
        w0 = ((b[1]-c[1])*(px-c[0])+(c[0]-b[0])*(py-c[1]))/denominator
        w1 = ((c[1]-a[1])*(px-c[0])+(a[0]-c[0])*(py-c[1]))/denominator
        w2 = 1-w0-w1
        inside = (w0>=-1e-5)&(w1>=-1e-5)&(w2>=-1e-5)
        if not inside.any(): continue
        xx,yy = xx[inside],yy[inside]
        weights = np.column_stack((w0[inside],w1[inside],w2[inside]))
        normal = np.array(polygon.normal)
        axis = int(np.argmax(np.abs(normal)))
        view = ('right' if normal[0]>=0 else 'left') if axis==0 else (
            ('back' if normal[1]>=0 else 'front') if axis==1 else ('top' if normal[2]>=0 else 'bottom'))
        projected = weights @ projections[view][indices]
        for kind,channels in maps.items():
            painted_view=view;best_alignment=float(abs(normal[axis]));selected_projection=projected
            for face in extras:
                identifier=face['Id']
                if identifier not in channels or face.get('IsSection',False):continue
                alignment=float(np.dot(normal,extra_normals[identifier]))
                if alignment>best_alignment:
                    painted_view=identifier;best_alignment=alignment;selected_projection=weights@extra_projections[identifier][indices]
            if painted_view not in channels: continue
            pixels = channels[painted_view]; box = bounds.get(painted_view,[0,0,pixels.shape[1]-1,pixels.shape[0]-1])
            if painted_view in extra_projections:
                scale=extra_space['pixelScale'] if extra_space else max(pixels.shape[:2])
                selected_projection=np.column_stack((.5+selected_projection[:,0]*scale/(2*(pixels.shape[1]-1)),.5-selected_projection[:,1]*scale/(2*(pixels.shape[0]-1))))
            selected_projection=np.clip(selected_projection,0,1)
            sx = np.clip(np.rint(box[0]+selected_projection[:,0]*(box[2]-box[0])).astype(int),0,pixels.shape[1]-1)
            sy = np.clip(np.rint(box[1]+selected_projection[:,1]*(box[3]-box[1])).astype(int),0,pixels.shape[0]-1)
            sample = pixels[sy,sx]; alpha = sample[:,3:4]
            atlases[kind][yy,xx,:3] = sample[:,:3]*alpha + np.array(DEFAULTS[kind])*(1-alpha)
        covered[yy,xx] = True
    # Two pixels of island padding prevent dark seams with bilinear filtering.
    for _ in range(2):
        before = covered.copy()
        for dy,dx in ((0,1),(0,-1),(1,0),(-1,0)):
            neighbors = np.roll(before,(dy,dx),(0,1))
            if dy==1: neighbors[0]=False
            if dy==-1: neighbors[-1]=False
            if dx==1: neighbors[:,0]=False
            if dx==-1: neighbors[:,-1]=False
            fill = neighbors & ~covered
            for atlas in atlases.values(): atlas[fill] = np.roll(atlas,(dy,dx),(0,1))[fill]
            covered |= fill
    material = bpy.data.materials.new('MeshDrawPaint')
    material.use_nodes = True
    nodes,links = material.node_tree.nodes,material.node_tree.links
    shader = nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*DEFAULTS['basecolor'],1)
    shader.inputs['Roughness'].default_value = .5
    outputs = {}
    for kind,atlas in atlases.items():
        image = bpy.data.images.new(f'MeshDraw_{kind}',width=size,height=size,alpha=True)
        image.colorspace_settings.name = 'Non-Color'
        image.pixels.foreach_set(atlas.ravel())
        image.filepath_raw = str(folder / f'mesh_{kind}.png')
        image.file_format='PNG'; image.save()
        # Interpret color maps in sRGB; scalar properties remain linear.
        if kind in ('basecolor','emission'): image.colorspace_settings.name='sRGB'
        texture = nodes.new('ShaderNodeTexImage'); texture.image=image; texture.label=kind
        if kind=='height':
            bump=nodes.new('ShaderNodeBump'); bump.inputs['Distance'].default_value=.04
            links.new(texture.outputs['Color'],bump.inputs['Height']); links.new(bump.outputs['Normal'],shader.inputs['Normal'])
        else:
            input_name={'basecolor':'Base Color','roughness':'Roughness','metallic':'Metallic','emission':'Emission Color'}[kind]
            links.new(texture.outputs['Color'],shader.inputs[input_name])
            if kind=='emission': shader.inputs['Emission Strength'].default_value=1
        outputs[kind]=str(Path(image.filepath_raw).name)
        image.filepath='//'+Path(image.filepath_raw).name
    mesh.data.materials.clear(); mesh.data.materials.append(material)
    (folder/'unreal-material.json').write_text(json.dumps({
        'textures': outputs, 'textureSize':size,
        'sRGB': {'basecolor':True,'emission':True,'roughness':False,'metallic':False,'height':False},
        'heightUsage':'Height is a grayscale bump source; convert to normals or use a height-to-normal material function in Unreal. It does not displace geometry.',
        'import':'Import texture maps and connect them to the corresponding Unreal material inputs; FBX does not preserve all Blender shader nodes.'},indent=2))
    print('Baked painted material channels:', ', '.join(outputs),flush=True)
    return outputs
