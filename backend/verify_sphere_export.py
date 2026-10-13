import sys,json
from pathlib import Path
import bpy,bmesh,numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from round_shading import load_round_surfaces
folder=Path(sys.argv[sys.argv.index('--')+1]);report=json.loads((folder/'export-summary.json').read_text())
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(folder/'mesh.fbx'))
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
s=load_round_surfaces(folder/'round_surfaces.json',report['normalization']['origin'],report['normalization']['height'])[0]
p=np.array([tuple(w.co) for w in mesh.data.vertices]);d=p-s['center'];r=np.linalg.norm(d,axis=1)
assert max(abs(r-s['radius']))<1e-5,'Export changed spherical radius'
bm=bmesh.new();bm.from_mesh(mesh.data)
assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True)>0
bm.free()
assert report['renderVertices']<=report['vertexLimit']
assert report['edgeOptimization']['initialCylinderCaps']['caps']==0
assert report['edgeOptimization']['finalCylinderCaps']['caps']==0
for loop in mesh.data.loops:
    radial=d[loop.vertex_index]/r[loop.vertex_index]
    assert np.asarray(mesh.data.corner_normals[loop.index].vector)@radial>.9999,'Sphere shading is faceted'
mesh.color=(.6,.6,.6,1)
camera=bpy.data.objects.new('Sphere camera',bpy.data.cameras.new('Sphere camera'));bpy.context.collection.objects.link(camera)
center=Vector(s['center']);camera.location=center+Vector((3,-5,2));camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=s['radius']*2.5
scene=bpy.context.scene;scene.camera=camera;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='OBJECT';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=False;scene.display.shading.background_type='WORLD';scene.world.color=(.05,.05,.05)
scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.filepath=str(folder/'sphere-preview.png');bpy.ops.render.render(write_still=True)
(folder/'sphere-checks.json').write_text(json.dumps(dict(checks='PASS',maximumRadiusError=float(max(abs(r-s['radius']))),renderVertices=report['renderVertices']),indent=2))
print('PASS: exported sphere has constant radius, smooth radial normals, closed topology, and respects the vertex limit')
