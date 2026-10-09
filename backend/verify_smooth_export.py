"""Verify FBX shading/topology and render before/after output geometry."""
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector

before,after=sys.argv[sys.argv.index('--')+1:]
before=str(Path(before).resolve());after=str(Path(after).resolve())
report={}
scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='SINGLE'
scene.display.shading.single_color=(.58,.66,.75)
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH'
scene.display.shading.background_type='WORLD'
scene.world.color=(.04,.05,.07)
scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
for label,folder in [('before',Path(before)),('after',Path(after))]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
    meshes=[obj for obj in scene.objects if obj.type=='MESH'];assert len(meshes)==1
    obj=meshes[0];assert len(obj.data.polygons)<=100000
    assert all(poly.use_smooth for poly in obj.data.polygons),'FBX lost smooth shading'
    low=Vector(tuple(min((obj.matrix_world@v.co)[i] for v in obj.data.vertices) for i in range(3)))
    high=Vector(tuple(max((obj.matrix_world@v.co)[i] for v in obj.data.vertices) for i in range(3)))
    center=(low+high)/2;span=max(high-low)
    camera=bpy.data.objects.new('Comparison camera',bpy.data.cameras.new('Comparison camera'))
    scene.collection.objects.link(camera);camera.location=center+Vector((1.2,-2.8,1))*span
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO';camera.data.ortho_scale=span*1.3;scene.camera=camera
    scene.render.filepath=str(Path(after)/(label+'-surface.png'));bpy.ops.render.render(write_still=True)
    report[label]={'vertices':len(obj.data.vertices),'polygons':len(obj.data.polygons),'smoothFaces':sum(p.use_smooth for p in obj.data.polygons)}
(Path(after)/'smooth-export-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('PASS: smooth FBX normals, bounded polygon count, before/after renders.',flush=True)
