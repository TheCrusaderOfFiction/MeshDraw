import sys,runpy,bmesh
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'backend'))
import edge_optimizer,vertex_budget,cylinder_caps
for module,name in [(edge_optimizer,'dissolve_flat_surfaces'),(edge_optimizer,'optimize_marked_edges'),(vertex_budget,'enforce_vertex_budget'),(cylinder_caps,'finish_cylinder_caps')]:
    original=getattr(module,name)
    def wrapped(*args,_original=original,_name=name,**kwargs):
        result=_original(*args,**kwargs)
        bm=bmesh.new();bm.from_mesh(args[0].data)
        bad=[e for e in bm.edges if not e.is_manifold]
        print('STAGE',_name,'invalid edges',len(bad),[(tuple(e.verts[0].co),len(e.link_faces)) for e in bad[:3]],flush=True)
        bm.free();return result
    setattr(module,name,wrapped)
folder=Path(sys.argv[sys.argv.index('--')+1])
sys.argv=['blender','--',str(folder/'shape.obj'),'static']
runpy.run_path(str(Path(__file__).resolve().parents[1]/'backend/export_fbx.py'),run_name='__main__')
