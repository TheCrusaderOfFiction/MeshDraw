import sys,json
from pathlib import Path
import bpy,bmesh
from mathutils import Vector
folder=Path(sys.argv[sys.argv.index('--')+1])
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH');bm=bmesh.new();bm.from_mesh(mesh.data)
assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True)>0
bm.free();report=json.loads((folder/'export-summary.json').read_text());assert report['renderVertices']<=report['vertexLimit']
assert report['edgeOptimization']['flattenedEdgeVertices']==0,'Curve was treated as a flat straight edge'
assert json.loads((folder/'edge_guides.json').read_text())[0]['Kind']=='Curve'
mesh.color=(.6,.6,.6,1);camera=bpy.data.objects.new('Curve camera',bpy.data.cameras.new('Curve camera'));bpy.context.collection.objects.link(camera)
target=Vector((0,0,.9));camera.location=target+Vector((2,-5,1.5));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=3.5
scene=bpy.context.scene;scene.camera=camera;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='OBJECT';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=False;scene.display.shading.background_type='WORLD';scene.world.color=(.05,.05,.05);scene.render.resolution_x=640;scene.render.resolution_y=480;scene.render.resolution_percentage=100;scene.render.filepath=str(folder/'curve-mesh-preview.png');bpy.ops.render.render(write_still=True)
(folder/'curve-export-check.txt').write_text('PASS: closed curved mesh, preserved curve guide, no straight-plane flattening, vertex budget respected.')
print('PASS: closed curved FBX, curve guide retained, vertex limit respected')
