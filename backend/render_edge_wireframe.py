"""Consistent before/after FBX wireframe inspection of the blade."""
import sys
from pathlib import Path
import bpy
from mathutils import Vector
before,after,folder=sys.argv[sys.argv.index('--')+1:]
folder=Path(folder);folder.mkdir(parents=True,exist_ok=True)
for name,path in [('before',before),('after',after)]:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=path)
    mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh.color=(.55,.55,.55,1)
    wire=mesh.copy();wire.data=mesh.data.copy();bpy.context.collection.objects.link(wire);wire.color=(.01,.01,.01,1)
    modifier=wire.modifiers.new('Inspection wire','WIREFRAME');modifier.thickness=.0008;modifier.offset=1;modifier.use_replace=True;modifier.use_boundary=True
    camera=bpy.data.objects.new('Inspection camera',bpy.data.cameras.new('Inspection camera'));bpy.context.collection.objects.link(camera)
    target=Vector((0,0,1.25));camera.location=target+Vector((2,-5,1.3));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=1.35
    scene=bpy.context.scene;scene.camera=camera;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='OBJECT';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=False;scene.display.shading.background_type='WORLD';scene.world.color=(.05,.05,.05)
    scene.render.resolution_x=640;scene.render.resolution_y=640;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(folder/(name+'.png'));bpy.ops.render.render(write_still=True)
print('Wireframe comparison rendered',flush=True)
