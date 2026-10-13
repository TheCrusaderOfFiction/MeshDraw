"""Inspect the cylinder and blade junction using identical shaded FBX views."""
import sys
from pathlib import Path
import bpy
from mathutils import Vector
before,after,folder=sys.argv[sys.argv.index('--')+1:];folder=Path(folder);folder.mkdir(parents=True,exist_ok=True)
for name,path in [('before',before),('after',after)]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    if Path(path).suffix=='.obj':
        bpy.ops.wm.obj_import(filepath=path,forward_axis='NEGATIVE_Z',up_axis='Y')
        obj=bpy.context.object;bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
        low=Vector(tuple(min(w.co[i] for w in obj.data.vertices) for i in range(3)));high=Vector(tuple(max(w.co[i] for w in obj.data.vertices) for i in range(3)))
        center=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
        for w in obj.data.vertices:w.co=(w.co-center)*(1.8/(high.z-low.z))
        for face in obj.data.polygons:face.use_smooth=True
    else:bpy.ops.import_scene.fbx(filepath=path)
    for obj in bpy.context.scene.objects:
        if obj.type=='MESH':obj.color=(.6,.6,.6,1)
    camera=bpy.data.objects.new('Cylinder camera',bpy.data.cameras.new('Cylinder camera'));bpy.context.collection.objects.link(camera)
    target=Vector((0,0,.37));camera.location=target+Vector((2,-5,2));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=1.65
    scene=bpy.context.scene;scene.camera=camera;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='OBJECT';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=False;scene.display.shading.background_type='WORLD';scene.world.color=(.05,.05,.05)
    scene.render.resolution_x=900;scene.render.resolution_y=500;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(folder/(name+'.png'));bpy.ops.render.render(write_still=True)
print('Cylinder shading comparison rendered',flush=True)
