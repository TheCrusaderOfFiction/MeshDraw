"""Blender vertex budgeting, including UV/normal/tangent seam duplication."""
import bpy
import numpy as np
from paint_materials import prepare_uv


def read_vertex_limit(job):
    value=job.get('vertexLimit',50000)
    if isinstance(value,bool) or not isinstance(value,int) or not 256<=value<=100000:
        raise ValueError('Maximum vertices must be an integer from 256 to 100,000.')
    return value


def render_vertex_count(mesh):
    data=mesh.data
    if not data.loops:return len(data.vertices)
    data.calc_tangents(uvmap=data.uv_layers.active.name)
    count=len(data.loops)
    index=np.empty(count,np.int32);data.loops.foreach_get('vertex_index',index)
    uv=np.empty(count*2,np.float32);data.uv_layers.active.data.foreach_get('uv',uv)
    normal=np.empty(count*3,np.float32);data.corner_normals.foreach_get('vector',normal)
    tangent=np.empty(count*3,np.float32);data.loops.foreach_get('tangent',tangent)
    sign=np.empty(count,np.float32);data.loops.foreach_get('bitangent_sign',sign)
    attributes=np.column_stack((index,uv.reshape(-1,2),normal.reshape(-1,3),tangent.reshape(-1,3),sign))
    return len(np.unique(attributes,axis=0))


def simplify(mesh,ratio):
    bpy.context.view_layer.objects.active=mesh
    modifier=mesh.modifiers.new('Vertex limit','DECIMATE');modifier.ratio=max(.00001,min(.99,ratio));modifier.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    if len(mesh.data.polygons)<4 or len(mesh.data.vertices)<4:
        raise ValueError('The vertex limit is too low for this shape. Increase Maximum vertices.')
    for polygon in mesh.data.polygons:polygon.use_smooth=True


def enforce_vertex_budget(mesh,limit):
    # Keep the existing laptop triangle budget. Preserve geometry below the cap;
    # only reduce it further if the measured seam count exceeds the user's limit.
    faces=len(mesh.data.polygons);vertices=len(mesh.data.vertices);passes=0
    ratio=min(1,100000/max(1,faces),limit*.95/max(1,vertices) if vertices>limit else 1)
    if ratio<1:simplify(mesh,ratio);passes+=1
    for _ in range(8):
        prepare_uv(mesh)
        count=render_vertex_count(mesh)
        if count<=limit and len(mesh.data.polygons)<=100000:return count,passes
        before=len(mesh.data.polygons)
        simplify(mesh,min(.9,.85*limit/count,95000/max(1,len(mesh.data.polygons))));passes+=1
        if len(mesh.data.polygons)>=before:break
    # A triangle has at most three exported vertices even when every corner splits.
    simplify(mesh,(limit//3)/max(1,len(mesh.data.polygons))*.95);passes+=1
    prepare_uv(mesh);count=render_vertex_count(mesh)
    if count>limit:raise ValueError('Could not meet the vertex limit. Increase Maximum vertices.')
    return count,passes
