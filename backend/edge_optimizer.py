"""Blender cleanup of annotated planar regions before UVs and skinning."""
import json
import math
import bpy
import bmesh
import numpy as np
from mathutils import Vector
from round_shading import load_round_surfaces,round_region_mask,round_protected_points


def clean_annotated_topology(data,prepared,round_surfaces=None):
    """Weld tiny pointed caps and remove collinear subdivision along marked ridges."""
    bm=bmesh.new();bm.from_mesh(data);bm.normal_update()
    if not bm.verts:
        bm.free();return {'weldedCornerVertices':0,'straightVerticesRemoved':0}
    scale=max(max(v.co[i] for v in bm.verts)-min(v.co[i] for v in bm.verts) for i in range(3))
    tolerance=max(scale*1e-7,1e-8);cap_limit=scale/192*1.5
    cylinder_points=round_protected_points(bm,round_surfaces)
    def cylinder_protected(w):return tuple(w.co) in cylinder_points
    welded=0;removed=0
    degenerate_limit=sum(f.calc_area()<=1e-12 for f in bm.faces)
    def valid(candidate,volume):
        candidate.normal_update()
        return (all(e.is_manifold for e in candidate.edges) and sum(f.calc_area()<=1e-12 for f in candidate.faces)<=degenerate_limit
                and candidate.calc_volume(signed=True)>0 and abs(candidate.calc_volume(signed=True)-volume)<=max(abs(volume)*.01,1e-10))
    def tidy(candidate):
        bmesh.ops.dissolve_degenerate(candidate,dist=tolerance,edges=list(candidate.edges))
        empty=[f for f in candidate.faces if f.calc_area()<=1e-12]
        if empty:bmesh.ops.delete(candidate,geom=empty,context='FACES')
        wire=[e for e in candidate.edges if not e.link_faces]
        if wire:bmesh.ops.delete(candidate,geom=wire,context='EDGES')
    flattened=0
    print('Refining marked edge strips...',flush=True)
    for g in prepared:
        if g['kind']!='Straight':continue
        bm.normal_update();bm.verts.ensure_lookup_table()
        coordinates=np.array([tuple(v.co) for v in bm.verts]);normals=np.array([tuple(v.normal) for v in bm.verts])
        relative=coordinates-g['point'];offset=relative@g['normal'];along=relative@g['direction']
        near=(abs(offset)<=g['radius'])&(along>=0)&(along<=g['length'])&(abs(normals@g['normal'])>.55)
        near &= ~round_region_mask(coordinates,round_surfaces)
        near &= np.array([tuple(w.co) not in cylinder_points for w in bm.verts])
        if 'section_normal' in g:near &= abs(relative@g['section_normal'])<=g['radius']
        for rounded in prepared:
            if rounded['kind']=='Corner' and rounded['corner_style']=='Rounded':
                r=coordinates-rounded['point'];near &= (r@rounded['u'])**2+(r@rounded['v'])**2>rounded['radius']**2
        if not near.any():continue
        # Re-triangulated reconstruction strips may not be perfectly coplanar.
        # Simplify only this marked strip, leaving curved regions outside it alone.
        indices=set(int(i) for i in np.flatnonzero(near));bm.verts.index_update()
        candidate=bm.copy();candidate.verts.ensure_lookup_table();volume=bm.calc_volume(signed=True)
        selected=[candidate.verts[i] for i in indices]
        eligible=set(selected);edges=[e for e in candidate.edges if all(v in eligible for v in e.verts)]
        bmesh.ops.dissolve_limit(candidate,angle_limit=math.radians(3),use_dissolve_boundaries=True,verts=selected,edges=edges,delimit=set())
        if valid(candidate,volume) and len(candidate.verts)<len(bm.verts):
            flattened+=len(bm.verts)-len(candidate.verts);bm.free();bm=candidate
        else:candidate.free()
        bm.normal_update();bm.verts.ensure_lookup_table()
        coordinates=np.array([tuple(v.co) for v in bm.verts]);normals=np.array([tuple(v.normal) for v in bm.verts])
        relative=coordinates-g['point'];offset=relative@g['normal'];along=relative@g['direction']
        near=(abs(offset)<=g['radius'])&(along>=0)&(along<=g['length'])&(abs(normals@g['normal'])>.55)
        near &= ~round_region_mask(coordinates,round_surfaces)
        near &= np.array([tuple(w.co) not in cylinder_points for w in bm.verts])
        if 'section_normal' in g:near &= abs(relative@g['section_normal'])<=g['radius']
        for rounded in prepared:
            if rounded['kind']=='Corner' and rounded['corner_style']=='Rounded':
                r=coordinates-rounded['point'];near &= (r@rounded['u'])**2+(r@rounded['v'])**2>rounded['radius']**2
        if not near.any():continue
        candidate=bm.copy();candidate.verts.ensure_lookup_table();volume=bm.calc_volume(signed=True)
        for index in np.flatnonzero(near):candidate.verts[int(index)].co=Vector(coordinates[index]-g['normal']*offset[index])
        bmesh.ops.remove_doubles(candidate,verts=[candidate.verts[int(i)] for i in np.flatnonzero(near)],dist=tolerance);tidy(candidate)
        if valid(candidate,volume):flattened+=int(near.sum());bm.free();bm=candidate
        else:candidate.free()
    corners=[g for g in prepared if g['kind']=='Corner' and g['corner_style']=='Pointed']
    print('Refining marked corners...',flush=True)
    groups={}
    for g in corners:
        if g.get('corner_group'):groups.setdefault(g['corner_group'],[]).append(g)
    apex_groups=set();apex_points=[]
    for group,members in groups.items():
        if len(members)<2:continue
        matrix=np.zeros((3,3));rhs=np.zeros(3);mean=np.mean([g['point'] for g in members],axis=0)
        for g in members:
            projection=np.outer(g['u'],g['u'])+np.outer(g['v'],g['v']);matrix+=projection;rhs+=projection@g['point']
        point=np.linalg.solve(matrix+np.eye(3)*1e-8,rhs+mean*1e-8)
        bounds=np.array([[min(v.co[i] for v in bm.verts) for i in range(3)],[max(v.co[i] for v in bm.verts) for i in range(3)]])
        axis=int(np.argmax(bounds[1]-bounds[0]));radius=max(g['radius'] for g in members);side=1 if abs(point[axis]-bounds[1,axis])<abs(point[axis]-bounds[0,axis]) else -1
        extreme=bounds[1 if side>0 else 0,axis]
        if abs(point[axis]-extreme)>max(radius*2,scale/192*4):continue
        nearby={v for v in bm.verts if side*(v.co[axis]-extreme)>=-radius*1.5}
        if not nearby:continue
        root=min(nearby,key=lambda v:(v.co-Vector(point)).length_squared);cluster={root};pending=[root]
        while pending:
            for edge in pending.pop().link_edges:
                for vertex in edge.verts:
                    if vertex in nearby and vertex not in cluster:cluster.add(vertex);pending.append(vertex)
        candidate=bm.copy();volume=bm.calc_volume(signed=True);before_tip=len(bm.verts)
        plane=Vector((0,0,0));plane[axis]=extreme-side*radius*1.5;plane_normal=Vector((0,0,0));plane_normal[axis]=side
        bmesh.ops.bisect_plane(candidate,geom=list(candidate.verts)+list(candidate.edges)+list(candidate.faces),dist=tolerance,plane_co=plane,plane_no=plane_normal,clear_inner=False,clear_outer=False)
        cap={f for f in candidate.faces if side*(f.calc_center_median()[axis]-plane[axis])>tolerance}
        boundary=[]
        for edge in candidate.edges:
            if len(edge.link_faces)==2 and sum(f in cap for f in edge.link_faces)==1:
                outside=next(f for f in edge.link_faces if f not in cap);loop=next(l for l in outside.loops if l.edge==edge);boundary.append((loop.link_loop_next.vert,loop.vert))
        degrees={}
        for a,b in boundary:degrees[a]=degrees.get(a,0)+1;degrees[b]=degrees.get(b,0)+1
        if len(boundary)<3 or any(n!=2 for n in degrees.values()):
            candidate.free();continue
        bmesh.ops.delete(candidate,geom=list(cap),context='FACES_ONLY')
        wire=[edge for edge in candidate.edges if not edge.link_faces]
        if wire:bmesh.ops.delete(candidate,geom=wire,context='EDGES')
        unused=[v for v in candidate.verts if not v.link_faces and v not in degrees]
        if unused:bmesh.ops.delete(candidate,geom=unused,context='VERTS')
        apex=candidate.verts.new(Vector(point))
        for a,b in boundary:candidate.faces.new((a,b,apex))
        if valid(candidate,volume):welded+=max(0,before_tip-len(candidate.verts));apex_groups.add(group);apex_points.append(np.asarray(point));bm.free();bm=candidate
        else:candidate.free()
    for g in corners:
        if g.get('corner_group') in apex_groups:continue
        bm.verts.ensure_lookup_table();bm.verts.index_update()
        point=Vector(g['point']);nearest=min((v.co-point).length for v in bm.verts)
        radius=min(g['radius'],max(cap_limit,min(scale*.035,nearest*1.5)))
        nearby={v for v in bm.verts if (v.co-point).length<=radius and not cylinder_protected(v)}
        if not nearby:continue
        root=min(nearby,key=lambda v:(v.co-point).length_squared);cluster={root};pending=[root]
        while pending:
            for edge in pending.pop().link_edges:
                # Walk the connected cap, never weld another disconnected part.
                for vertex in edge.verts:
                    if vertex in nearby and vertex not in cluster:cluster.add(vertex);pending.append(vertex)
        if len(cluster)<2 and (root.co-point).length<=tolerance:continue
        indices=[v.index for v in cluster];candidate=bm.copy();candidate.verts.ensure_lookup_table()
        volume=bm.calc_volume(signed=True)
        bmesh.ops.pointmerge(candidate,verts=[candidate.verts[i] for i in indices],merge_co=point)
        bmesh.ops.dissolve_degenerate(candidate,dist=tolerance,edges=list(candidate.edges))
        empty=[f for f in candidate.faces if f.calc_area()<=1e-12]
        if empty:bmesh.ops.delete(candidate,geom=empty,context='FACES')
        wire=[e for e in candidate.edges if not e.link_faces]
        if wire:bmesh.ops.delete(candidate,geom=wire,context='EDGES')
        if valid(candidate,volume):welded+=len(cluster)-1;bm.free();bm=candidate
        else:candidate.free()
    from corner_ridges import clean_corner_ridges
    rounded_protection=[g for g in prepared if g['kind']=='Corner' and g['corner_style']=='Rounded']
    bm,corner_removed,projected_corners,corner_ridges=clean_corner_ridges(bm,corners,rounded_protection,valid,scale,tolerance,cylinder_protected)
    welded+=corner_removed
    print(f'Corner ridges: {len(projected_corners)} refined; {corner_removed} redundant vertices removed',flush=True)
    fixed_points=[g['point'] for g in corners if g.get('id') not in projected_corners]+apex_points
    for g in prepared:
        if g['kind']=='Straight':fixed_points.extend([g['point'],g['point']+g['direction']*g['length']])
    fixed_points=np.asarray(fixed_points,dtype=float).reshape(-1,3)
    def protected(vertex):
        if cylinder_protected(vertex):return True
        p=np.array(tuple(vertex.co))
        if len(fixed_points) and np.any(np.sum((fixed_points-p)**2,axis=1)<=tolerance*tolerance):return True
        return any(((p-g['point'])@g['u'])**2+((p-g['point'])@g['v'])**2<=g['radius']**2 for g in rounded_protection)
    from straight_ridges import simplify_straight_ridges
    print('Removing straight ridge subdivisions...',flush=True)
    for g in prepared:
        if g['kind']=='Straight':
            bm,count=simplify_straight_ridges(bm,g,protected,valid,scale);removed+=count
    for g in corner_ridges:
        bm,count=simplify_straight_ridges(bm,g,protected,valid,scale);removed+=count
    # Preserve explicit junctions while dissolving flat-face triangulation.
    protected_vertices={v for v in bm.verts if protected(v)}
    bmesh.ops.dissolve_limit(bm,angle_limit=math.radians(.05),use_dissolve_boundaries=False,verts=[v for v in bm.verts if v not in protected_vertices],edges=[e for e in bm.edges if not any(v in protected_vertices for v in e.verts)],delimit={'NORMAL','SHARP'})
    from straight_ridges import simplify_collinear_vertices
    for g in prepared+corner_ridges:
        if g['kind']!='Straight':continue
        bm,count=simplify_collinear_vertices(bm,g,protected,valid,scale,tolerance);removed+=count
    bm.to_mesh(data);bm.free();data.update()
    return {'weldedCornerVertices':welded,'straightVerticesRemoved':removed,'flattenedEdgeVertices':flattened,'singlePointTips':len(apex_groups),'projectedCornerRidges':len(projected_corners),'ridgeGuideCoverage':[{'id':g.get('id'),'removedVertices':len(g['ridge_positions']),'quarters':np.histogram(g['ridge_positions'],bins=np.linspace(0,1,5))[0].tolist()} for g in prepared+corner_ridges if g.get('ridge_positions')]}


def optimize_marked_edges(mesh,path):
    if not path.exists():return {'marks':0,'removedVertices':0,'sharpEdges':0}
    guides=json.loads(path.read_text(encoding='utf-8'));before=len(mesh.data.vertices)
    if not guides:return {'marks':0,'removedVertices':0,'sharpEdges':0,'weldedCornerVertices':0,'straightVerticesRemoved':0}
    convert=lambda p:np.asarray(p,dtype=float)[[0,2,1]]*np.array([1,-1,1])
    prepared=[]
    for g in guides:
        item=dict(kind=g['Kind'],point=convert(g['Point']),radius=g['Radius'],u=convert(g['U']),v=convert(g['V']),corner_style=g.get('CornerStyle','Pointed'),corner_group=g.get('CornerGroup'),id=g.get('Id'))
        if 'SectionNormal' in g:item['section_normal']=convert(g['SectionNormal'])
        if g['Kind']=='Straight':
            direction=convert(g['End'])-item['point'];length=np.linalg.norm(direction)
            item.update(normal=convert(g['Normal']),direction=direction/length,length=length)
        prepared.append(item)
    round_surfaces=load_round_surfaces(path.parent/'round_surfaces.json',(0,0,0),1.8)
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
    if round_surfaces:
        circular=round_region_mask(coordinates,round_surfaces)
        selected[polygon_ids[circular[triangles].any(axis=1)]]=False
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
    topology=clean_annotated_topology(mesh.data,prepared,round_surfaces)
    bm=bmesh.new();bm.from_mesh(mesh.data);bm.normal_update();sharp=0
    for edge in bm.edges:
        if len(edge.link_faces)!=2 or edge.link_faces[0].normal.dot(edge.link_faces[1].normal)>math.cos(math.radians(25)):continue
        if round_region_mask([tuple(w.co) for w in edge.verts],round_surfaces).any():continue
        midpoint=np.array(tuple((edge.verts[0].co+edge.verts[1].co)/2))
        for g in prepared:
            if g['kind']=='Curve':continue
            if g['kind']=='Corner' and g['corner_style']=='Rounded':continue
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
    report={'marks':len(prepared),'removedVertices':before-len(mesh.data.vertices),'sharpEdges':sharp,**topology}
    print('Marked edge cleanup:',report,flush=True)
    return report


def dissolve_flat_surfaces(mesh):
    """Remove redundant coplanar topology while retaining curved and sharp edges."""
    before=len(mesh.data.vertices)
    bm=bmesh.new();bm.from_mesh(mesh.data);bm.normal_update()
    bmesh.ops.dissolve_limit(bm,angle_limit=math.radians(.05),use_dissolve_boundaries=False,verts=list(bm.verts),edges=list(bm.edges),delimit={'NORMAL','SHARP'})
    bm.to_mesh(mesh.data);bm.free();mesh.data.update()
    removed=before-len(mesh.data.vertices)
    print(f'Flat surface cleanup: {removed:,} redundant vertices removed',flush=True)
    return removed
