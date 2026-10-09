"""Real Blender roundtrip preserves custom drawing planes and smooth geometry."""
from pathlib import Path
import sys,tempfile,json
import bpy
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'BlenderPlugin'))
import meshdraw_io
meshdraw_io.register();bridge=meshdraw_io.bridge;bundle=bridge.bundle
out=root/'outputs'/'extra-face-export-checks';(out/'temp').mkdir(exist_ok=True);tempfile.tempdir=str(out/'temp')
for mode in ('static','skeletal'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.open_mainfile(filepath=str(out/mode/'mesh.blend'))
    original_mesh=next(o for o in bpy.data.objects if o.type=='MESH')
    assert all(p.use_smooth for p in original_mesh.data.polygons),'Flat shaded mesh exported'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source=out/mode/'asset.meshdrawasset';original,original_files=bundle.read(source)
    bridge.import_asset(bpy.context,str(source));collection=bridge.asset_collection(bpy.context)
    mesh=next(o for o in collection.all_objects if o.type=='MESH')
    assert bool([o for o in collection.all_objects if o.type=='ARMATURE'])==(mode=='skeletal')
    destination=out/mode/'blender-roundtrip.meshdrawasset';bridge.export_asset(bpy.context,str(destination),collection)
    document,files=bundle.read(destination)
    assert json.loads(files['project.meshdraw'])==json.loads(original_files['project.meshdraw'])
    faces=json.loads(files['project.meshdraw'])['ExtraFaces']
    for face in faces:
        for suffix in ('.png','_basecolor.png','_roughness.png','_metallic.png','_emission.png','_height.png'):
            name=face['Id']+suffix;assert files[name]==original_files[name],name+' lost in Blender'
    print('Verified '+mode+' extra faces, property layers and smooth polygon flags.',flush=True)
(out/'blender-checks.txt').write_text('PASS: static/skeletal smooth polygon flags; custom plane definitions; all extra drawing/property bytes preserved through Blender roundtrip.\n')
