using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckCrossFaceEdges()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        var panel=new[]{new Point3D(-.25,-1,.5),new Point3D(.25,-1,.5),new Point3D(.25,1,.5),new Point3D(-.25,1,.5)};
        var edges=Enumerable.Range(0,4).Select(i=>(panel[i],panel[(i+1)%4])).ToArray();var surfaces=BuildEdgeSurfaces(edges);
        Check(surfaces.Count==2,"Closed edge surface not triangulated");
        var a=new Point3D(-1,0,0);var b=new Point3D(1,0,0);var toward=new Vector3D(0,0,1);
        var visible=VisibleEdgeIntervals(a,b,toward,surfaces);
        Check(visible.Count==2 && Math.Abs(visible[0].high-.375)<1e-6 && Math.Abs(visible[1].low-.625)<1e-6,"Partially blocked edge was not clipped at the surface boundary");
        Check(VisibleEdgeIntervals(a,b,-toward,surfaces).Single()==(0d,1d),"A surface behind the edge hid it");
        Check(VisibleEdgeIntervals(new(-1,0,.5),new(1,0,.5),toward,surfaces).Single()==(0d,1d),"A surface hid its own edge");
        Check(BuildEdgeSurfaces(edges.Take(3).ToArray()).Count==0,"An open outline invented a blocking surface");
        var cube=new[]{new Point3D(-1,-1,-1),new Point3D(1,-1,-1),new Point3D(1,1,-1),new Point3D(-1,1,-1),new Point3D(-1,-1,1),new Point3D(1,-1,1),new Point3D(1,1,1),new Point3D(-1,1,1)};
        var cubeEdges=new[]{(0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7)}.Select(p=>(cube[p.Item1],cube[p.Item2])).ToArray();
        Check(BuildEdgeSurfaces(cubeEdges).Count==12,"Connected edge network did not create the six blocking faces");

        var previous=Document();bool oldVisible=ShowCrossFaceEdges.IsChecked==true;var oldUndo=undo.ToList();var oldRedo=redo.ToArray();
        try
        {
            ApplyDocument(previous with {EdgeGuides=new(),Connections=[],LineEqualities=[],CanvasWidth=640,CanvasHeight=640});
            var line=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,200,500,200,Depth1:1,Depth2:1);edgeGuides["Back"]=[line];
            ShowCrossFaceEdges.IsChecked=true;RenderRig();
            Check(overlays["Front"].Children.OfType<Line>().Any(l=>(string?)l.Tag=="CrossFaceEdge"),"Interior edge not visible on another face");
            var before=Snapshot();ShowCrossFaceEdges.IsChecked=false;
            Check(!overlays["Front"].Children.OfType<Line>().Any(l=>(string?)l.Tag=="CrossFaceEdge") && Snapshot()==before,"Overlay visibility changed saved artwork");
            ShowCrossFaceEdges.IsChecked=true;RenderRig();var cached=crossFaceProjected["Front"];RenderRig();Check(ReferenceEquals(cached,crossFaceProjected["Front"]),"Visibility rebuilt on unchanged brush frames");
            edgeGuides["Back"][0]=line with {Y1=240,Y2=240};RenderRig();Check(!ReferenceEquals(cached,crossFaceProjected["Front"]),"Moved edge did not refresh its projections");
        }
        finally
        {
            ShowCrossFaceEdges.IsChecked=oldVisible;ApplyDocument(previous);undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);
        }
    }
}
