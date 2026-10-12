using System.Windows;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckReferenceMerge()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        var previous=Document();var oldUndo=undo.ToList();var oldRedo=redo.ToArray();
        try
        {
            ApplyDocument(previous with {EdgeGuides=new(),Connections=[],LineEqualities=[],CanvasWidth=640,CanvasHeight=640});
            var face=new FaceDefinition("custom_"+Guid.NewGuid().ToString("N"),"Merge reference inside",Yaw:90,IsSection:true);extraFaces.Add(face);CreateFaceSurface(face.Id,face.Name);
            var reference=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",300,100,340,500);edgeGuides[face.Id]=[reference];
            var partners=new[]{new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,100,100,200),new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,200,100,350),new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,350,100,500),new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",500,100,500,500)};
            edgeGuides["Front"]=partners.ToList();var joint=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",100,200,100,200);edgeGuides["Front"].Add(joint);
            highlightedGuides.Clear();highlightedConnections.Clear();highlightedGuides.Add((face.Id,reference.Id));SetEqualityReference(this,new RoutedEventArgs());foreach(var p in partners)highlightedGuides.Add(("Front",p.Id));
            undo.Clear();undo.Add(Snapshot());redo.Clear();MergeSelectedGuides(this,new RoutedEventArgs());
            Check(edgeGuides[face.Id].All(g=>g.Id!=reference.Id),"Multi-edge merge did not remove its reference: "+PreviewLineSelection.Text);
            var start=GuideWorldPoint(face.Id,reference,0);var end=GuideWorldPoint(face.Id,reference,1);
            foreach(var partner in partners)
            {
                var g=edgeGuides["Front"].First(g=>g.Id==partner.Id);
                Check(Math.Abs(g.X1-partner.X1)<1e-6 && Math.Abs(g.X2-partner.X2)<1e-6,"Reference merge moved unrelated-axis coordinates");
                foreach(int endpoint in new[]{0,1})
                {
                    var world=GuideWorldPoint("Front",g,endpoint);double y=endpoint==0?g.Y1:g.Y2;double t=(y-100)/400;
                    Check(Math.Abs(world.Z-(start.Z+(end.Z-start.Z)*t))<1e-6,"Partner did not inherit reference depth and angle");
                }
            }
            Check(Math.Abs(edgeGuides["Front"].First(g=>g.Id==joint.Id).Depth1-edgeGuides["Front"][0].Depth2)<1e-6,"Reference merge detached its local corner");
            Undo(this,new RoutedEventArgs());Check(edgeGuides[face.Id].Contains(reference) && partners.All(edgeGuides["Front"].Contains),"Reference merge undo did not restore original edges");
            Redo(this,new RoutedEventArgs());Check(edgeGuides[face.Id].Count==0,"Reference deletion redo failed");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(edgeGuides[face.Id].Count==0 && edgeGuides["Front"].Count(g=>g.Kind=="Straight")==4,"Reference merge save/load lost survivors or restored reference");
            edgeGuides.Clear();lineEqualities.Clear();connectionEdges.Clear();
            var corners=new[]{new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",100,100,100,100),new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",120,110,120,110),new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",130,120,130,120)};
            edgeGuides["Front"]=[corners[0]];edgeGuides["Left"]=[corners[1]];edgeGuides["Top"]=[corners[2]];
            highlightedGuides.Clear();foreach(var p in new[]{("Front",corners[0].Id),("Left",corners[1].Id),("Top",corners[2].Id)})highlightedGuides.Add(p);MergeSelectedGuides(this,new RoutedEventArgs());
            var groups=edgeGuides.Values.SelectMany(g=>g).Select(g=>g.CornerGroup).Distinct().ToArray();Check(groups.Length==1 && groups[0]!=null,"Three-corner merge did not form one point");
            var point=GuideWorldPoint("Front",edgeGuides["Front"][0],0);Check(new[]{"Left","Top"}.All(f=>(GuideWorldPoint(f,edgeGuides[f][0],0)-point).Length<1e-6),"Multi-corner merge positions differ");
            var scene=new Model3DGroup();RenderPreviewEdgeLinks(scene);Check(previewEdgeModels.Count==3,"Merged point rendered duplicate corner glyphs");
        }
        finally
        {
            highlightedGuides.Clear();highlightedConnections.Clear();equalityReference=null;ApplyDocument(previous);undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);
        }
    }
}
