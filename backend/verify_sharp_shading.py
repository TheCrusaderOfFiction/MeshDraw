"""Check crease shading and smooth curves after vertex budgeting and FBX roundtrip."""
import sys,math
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vertex_budget import enforce_vertex_budget
folder=Path(sys.argv[sys.argv.index('--')+1]);folder.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_cube_add();cube=bpy.context.object;cube.name='SharpCube'
for p in cube.data.polygons:p.use_smooth=True
modifier=cube.modifiers.new('Planar test subdivision','SUBSURF');modifier.subdivision_type='SIMPLE';modifier.levels=3
bpy.ops.object.modifier_apply(modifier=modifier.name)
from edge_optimizer import dissolve_flat_surfaces
assert dissolve_flat_surfaces(cube)>100,'Planar interior vertices were retained'
count,_=enforce_vertex_budget(cube,256)
assert count<=256
assert sum(e.use_edge_sharp for e in cube.data.edges)==12
assert all(abs(n.vector.length-1)<1e-5 for n in cube.data.corner_normals)
bpy.ops.mesh.primitive_uv_sphere_add(segments=64,ring_count=32,location=(3,0,0));sphere=bpy.context.object;sphere.name='SmoothSphere'
for p in sphere.data.polygons:p.use_smooth=True
count,_=enforce_vertex_budget(sphere,100000)
assert count<=100000
assert not any(e.use_edge_sharp for e in sphere.data.edges)
# Deliberately uneven cylinder: analytic normals must survive triangulation/FBX.
from round_shading import smooth_round_normals
import numpy as np
bpy.ops.mesh.primitive_cylinder_add(vertices=64,radius=1,depth=2)
cylinder=bpy.context.object;cylinder.name='RoundCylinder'
for vertex in cylinder.data.vertices:
    angle=math.atan2(vertex.co.y,vertex.co.x)
    vertex.co.x*=1+.01*math.sin(angle*7);vertex.co.y*=1+.01*math.sin(angle*7)
for polygon in cylinder.data.polygons:polygon.use_smooth=True
surfaces=[dict(center=np.zeros(3),u=np.array([1,0,0]),v=np.array([0,1,0]),radius=1,tolerance=.05)]
count,_=enforce_vertex_budget(cylinder,100000,surfaces)
assert count<=100000
cylinder.location=(6,0,0)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=str(folder/'sharp-and-round.fbx'),use_selection=True,mesh_smooth_type='FACE',add_leaf_bones=False)
bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(folder/'sharp-and-round.fbx'))
cube=bpy.data.objects['SharpCube'];sphere=bpy.data.objects['SmoothSphere']
assert sum(e.use_edge_sharp for e in cube.data.edges)==12
assert all(p.use_smooth for p in sphere.data.polygons)
assert not any(e.use_edge_sharp for e in sphere.data.edges)
cylinder=bpy.data.objects['RoundCylinder']
checked=0
for polygon in cylinder.data.polygons:
    if abs(polygon.normal.z)>.2:continue
    for index in polygon.loop_indices:
        loop=cylinder.data.loops[index];p=cylinder.data.vertices[loop.vertex_index].co
        radial=np.array([p.x,p.y,0]);radial/=np.linalg.norm(radial)
        normal=np.array(tuple(cylinder.data.corner_normals[index].vector))
        assert abs(np.hypot(p.x,p.y)-1)<1e-5,'Cylinder wall moved off radius during export'
        assert normal@radial>.99999,'Circular wall shading lost its analytic normal'
        checked+=1
assert checked>100
print('PASS: analytic cylinder normals survive FBX; sharp cube edges and smooth sphere survive budget enforcement and FBX import.')
