from pathlib import Path
import json
import tempfile
from package_asset import package,unpack_checked

def main():
    root=Path(__file__).resolve().parents[1]
    for name in ('layer-checks','layer-checks-static'):
        folder=root/'outputs'/name
        output=package(folder)
        manifest,files=unpack_checked(output)
        for filename,payload in files.items(): assert payload==(folder/filename).read_bytes()
        assert {'mesh.fbx','mesh.blend','project.meshdraw','skeleton.json','mesh_height.png'}<=files.keys()
        assert len(manifest['textures'])==5
        with tempfile.TemporaryDirectory(dir=root/'outputs') as temp:
            test=Path(temp)/'corrupt.meshdrawasset'
            changed=json.loads(output.read_text()); changed['files'][0]['sha1']='0'*40
            test.write_text(json.dumps(changed))
            try: unpack_checked(test);raise AssertionError('Corrupt payload accepted')
            except ValueError: pass
            changed=json.loads(output.read_text());changed['files'][0]['name']='../escape.json'
            test.write_text(json.dumps(changed))
            try: unpack_checked(test);raise AssertionError('Unsafe path accepted')
            except ValueError: pass
    print('PASS: self-contained static/skeletal bundles, exact payload roundtrip, five map references, checksum rejection, path rejection.')

if __name__=='__main__':main()
