"""Blender cleanup of annotated planar regions before UVs and skinning."""
import json
import math
import bpy
import bmesh
import numpy as np


def optimize_marked_edges(mesh,path):
    if not path.exists():return {'marks':0,'removedVertices':0,'sharpEdges':0}
    guides=json.loads(path.read_text(encoding='utf-8'));before=len(mesh.data.vertices)
    convert=lambda p:np.asarray(p,dtype=float)[[0,2,1]]*np.array([1,-1,1])
    prepared=[]
    for g in guides:
        item=dict(kind=g['Kind'],point=convert(g['Point']),radius=g['Radius'],u=convert(g['U']),v=convert(g['V']))
        if 'SectionNormal' in g:item['section_normal']=convert(g['SectionNormal'])
        if g['Kind']=='Straight':
            direction=convert(g['End'])-item['point'];length=np.linalg.norm(direction)
            item.update(normal=convert(g['Normal']),direction=direction/length,length=length)
        prepared.append(item)
    data=mesh.data;data.calc_loop_triangles()
    coordinates=np.empty(len(data.vertices)*3,np.float32);data.vertices.foreach_get('co',coordinates);coordinates=coordinates.reshape(-1,3)
    triangles=np.empty(len(data.loop_triangles)*3,np.int32);data.loop_triangles.foreach_get('vertices',triangles);triangles=triangles.reshape(-1,3)
    polygon_ids=np.empty(len(data.loop_triangles),np.int32);data.loop_triangles.foreach_get('polygon_index',polygon_ids)
    selected=np.zeros(len(data.polygons),bool)
    for g in prepared:
        if g['kind']!='Straight':continue
        relative=coordinates-g['point'];offset=abs(relative@g['normal']);along=relative@g['direction']
        flat=(offset<1e-5)&(along>=-g['radius'])&(along<=g['length']+g['radius'])
        if 'section_normal' in g:flat &= abs(relative@g['section_normal'])<=g['radius']
        selected[polygon_ids[flat[triangles].all(axis=1)]]=True
    if selected.any():
        bm=bmesh.new();bm.from_mesh(data);bm.faces.ensure_lookup_table()
        for vertex in bm.verts:vertex.select_set(False)
        for edge in bm.edges:edge.select_set(False)
        for face in bm.faces:face.select_set(False)
        for i in np.flatnonzero(selected):bm.faces[int(i)].select_set(True)
        bm.to_mesh(data);bm.free();data.update()
        bpy.context.view_layer.objects.active=mesh;bpy.context.tool_settings.mesh_select_mode=(False,False,True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.dissolve_limited(angle_limit=math.radians(.1),use_dissolve_boundaries=True,delimit={'NORMAL','SHARP'})
        bpy.ops.object.mode_set(mode='OBJECT')
    bm=bmesh.new();bm.from_mesh(mesh.data);bm.normal_update();sharp=0
    for edge in bm.edges:
        if len(edge.link_faces)!=2 or edge.link_faces[0].normal.dot(edge.link_faces[1].normal)>math.cos(math.radians(25)):continue
        midpoint=np.array(tuple((edge.verts[0].co+edge.verts[1].co)/2))
        for g in prepared:
            relative=midpoint-g['point']
            if g['kind']=='Straight':
                near=abs(relative@g['normal'])<=g['radius'] and -g['radius']<=relative@g['direction']<=g['length']+g['radius']
            else:near=(relative@g['u'])**2+(relative@g['v'])**2<=g['radius']**2
            if 'section_normal' in g:near=near and abs(relative@g['section_normal'])<=g['radius']
            if near:edge.smooth=False;sharp+=1;break
    bm.to_mesh(mesh.data);bm.free();mesh.data.update()
    if sharp:
        # Zero custom normals ask Blender to compute split normals from sharp flags.
        mesh.data.normals_split_custom_set([(0,0,0)]*len(mesh.data.loops))
    report={'marks':len(prepared),'removedVertices':before-len(mesh.data.vertices),'sharpEdges':sharp}
    print('Marked edge cleanup:',report,flush=True)
    return report
