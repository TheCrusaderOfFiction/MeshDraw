"""Validation and Blender conversion of the shared normalized drawing skeleton."""
import json
import math
from pathlib import Path
import bpy
from mathutils import Vector

def load_rig(path):
    path=Path(path)
    if not path.exists(): return None
    data=json.loads(path.read_text())
    joints,bones=data.get('Joints',[]),data.get('Bones',[])
    if not bones: return None
    if len(joints)>256 or len(bones)>128: raise ValueError('Skeleton exceeds the supported size')
    joint_map={joint['Id']:joint for joint in joints}
    bone_map={bone['Id']:bone for bone in bones}
    if len(joint_map)!=len(joints) or len(bone_map)!=len(bones): raise ValueError('Duplicate skeleton IDs')
    names={'root'}; tails=set()
    for joint in joints:
        if any(not isinstance(joint.get(axis),(int,float)) or not math.isfinite(joint[axis]) or not 0<=joint[axis]<=1 for axis in ('X','Y','Z')):
            raise ValueError('Invalid joint coordinates')
    for bone in bones:
        name=bone['Name'].strip()
        if not name or len(name)>64 or name.casefold() in names: raise ValueError('Bone names must be unique and may not be root')
        names.add(name.casefold())
        if bone['Head'] not in joint_map or bone['Tail'] not in joint_map or bone['Tail'] in tails: raise ValueError('Invalid joint references')
        tails.add(bone['Tail'])
        head,tail=joint_map[bone['Head']],joint_map[bone['Tail']]
        if sum((head[axis]-tail[axis])**2 for axis in ('X','Y','Z'))<1e-8: raise ValueError('Zero-length bone')
        cursor=bone; visited={bone['Id']}
        while cursor.get('Parent') is not None:
            parent=bone_map.get(cursor['Parent'])
            if parent is None or parent['Tail']!=cursor['Head'] or parent['Id'] in visited: raise ValueError('Invalid bone hierarchy')
            visited.add(parent['Id']); cursor=parent
    return data

def populate(armature,data,mesh):
    coords=[vertex.co for vertex in mesh.data.vertices]
    low=Vector(tuple(min(point[i] for point in coords) for i in range(3)))
    high=Vector(tuple(max(point[i] for point in coords) for i in range(3)))
    def position(joint):
        return Vector((low.x+joint['X']*(high.x-low.x),high.y-joint['Z']*(high.y-low.y),low.z+joint['Y']*(high.z-low.z)))
    joints={joint['Id']:joint for joint in data['Joints']}
    root=armature.edit_bones.new('root'); root.head=(0,0,0); root.tail=(0,0,.08)
    created={}
    for definition in data['Bones']:
        bone=armature.edit_bones.new(definition['Name'].strip())
        bone.head=position(joints[definition['Head']]); bone.tail=position(joints[definition['Tail']])
        created[definition['Id']]=bone
    for definition in data['Bones']:
        bone=created[definition['Id']]
        bone.parent=created[definition['Parent']] if definition.get('Parent') else root
        bone.use_connect=bool(definition.get('Parent'))
    print('Using drawn skeleton:',len(data['Bones']),'bones plus root',flush=True)
