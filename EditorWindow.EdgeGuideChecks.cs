using System.IO;
using System.Windows;
namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckEdgeGuides(string folder)
    {
        static void Check(bool value,string message) {if(!value)throw new Exception(message);}
        var previous=Document();var oldUndo=undo.ToList();var oldRedo=redo.ToArray();var oldTool=Tool.SelectedIndex;
        try
        {
            var doc=Document() with {EdgeGuides=new()};ApplyDocument(doc);Views.SelectedIndex=0;
            var paint=DrawingDocument.Encode(ActiveLayer!.Ink.Strokes);
            Tool.SelectedIndex=9;EdgeGuideMode.SelectedIndex=0;
            var canvas=overlays[ActiveView];
            BeginEdgeGuide(canvas,new Point(20,20));ContinueEdgeGuide(new Point(100,20));EndEdgeGuide(new Point(100,20));
            EdgeGuideMode.SelectedIndex=1;BeginEdgeGuide(canvas,new Point(20,20));
            Check(edgeGuides[ActiveView].Count==2,"Edge gestures did not create line and corner");
            Check(DrawingDocument.Encode(ActiveLayer!.Ink.Strokes)==paint,"Edge marks changed painted layer");
            var saved=Snapshot();File.WriteAllText(Path.Combine(folder,"edge-marks.meshdraw"),saved);ApplyDocument(DrawingDocument.Read(saved));
            Check(edgeGuides[ActiveView].Count==2,"Edge marks lost on load");
            CopyFace("Front",["Back"],false,false,true);
            Check(edgeGuides["Back"].Count==2 && edgeGuides["Back"][0].X1==canvasWidth-20 && edgeGuides["Back"][0].Id!=edgeGuides["Front"][0].Id,"Mirrored independent edge copy failed");
            EdgeGuideMode.SelectedIndex=2;BeginEdgeGuide(canvas,new Point(50,20));Check(edgeGuides["Front"].Count==1,"Edge eraser failed");
            Undo(this,new RoutedEventArgs());Check(edgeGuides["Front"].Count==2,"Edge erase undo failed");
            EdgeGuideBehavior.SelectedIndex=1;EdgeGuideMode.SelectedIndex=3;BeginEdgeGuide(canvas,new Point(50,20));
            Check(edgeGuides["Front"][0].Mode=="Absolute" && edgeGuides["Front"][1].Mode=="Relative","Per-mark mode switching failed");
            Check(overlays["Front"].Children.OfType<System.Windows.Shapes.Line>().Any(l=>l.Stroke==System.Windows.Media.Brushes.LimeGreen),"Absolute line is not green");
            ApplyEdgeGuideBehavior(this,new RoutedEventArgs());Check(edgeGuides["Front"].All(g=>g.Mode=="Absolute"),"Whole-face mode switch failed");
            Check(overlays["Front"].Children.OfType<System.Windows.Shapes.Rectangle>().Any(r=>r.Stroke==System.Windows.Media.Brushes.LimeGreen),"Absolute corner is not green");
            var modesSaved=Snapshot();ApplyDocument(DrawingDocument.Read(modesSaved));Check(edgeGuides["Front"].All(g=>g.Mode=="Absolute"),"Modes lost on load");
            CopyFace("Front",["Right"],false,false,true);Check(edgeGuides["Right"].All(g=>g.Mode=="Absolute"),"Copy lost mark modes");
            EdgeGuideBehavior.SelectedIndex=0;ApplyEdgeGuideBehavior(this,new RoutedEventArgs());Check(edgeGuides["Front"].All(g=>g.Mode=="Relative"),"Relative switch failed");
            Undo(this,new RoutedEventArgs());Check(edgeGuides["Front"].All(g=>g.Mode=="Absolute"),"Mode undo failed");
            var invalid=Document();invalid.EdgeGuides!["Front"][0]=invalid.EdgeGuides["Front"][0] with {X1=double.NaN};
            bool rejected=false;try {DrawingDocument.Validate(invalid);}catch(InvalidDataException) {rejected=true;}Check(rejected,"Invalid guide coordinate accepted");
            ApplyDocument(previous with {EdgeGuides=new()});Views.SelectedIndex=0;Tool.SelectedIndex=9;
            EdgeGuideMode.SelectedIndex=0;ConnectEdgeLines.IsChecked=true;EdgeCornerStyle.SelectedIndex=1;canvas=overlays[ActiveView];
            BeginEdgeGuide(canvas,new Point(30,30));EndEdgeGuide(new Point(100,30));
            BeginEdgeGuide(canvas,new Point(105,32));EndEdgeGuide(new Point(100,100));
            var connected=edgeGuides[ActiveView];Check(connected.Count==3,"Connected lines did not create a joint");
            Check(connected[1].X1==100 && connected[1].Y1==30,"Endpoints failed to snap");
            Check(connected.Single(g=>g.Kind=="Corner").CornerStyle=="Rounded","Connected joint lost rounded style");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(edgeGuides[ActiveView].Single(g=>g.Kind=="Corner").CornerStyle=="Rounded","Corner shape lost on save");
            EdgeCornerStyle.SelectedIndex=0;EdgeGuideMode.SelectedIndex=4;BeginEdgeGuide(overlays[ActiveView],new Point(100,30));
            Check(edgeGuides[ActiveView].Single(g=>g.Kind=="Corner").CornerStyle=="Pointed","Corner shape tap switch failed");
            var firstLine=edgeGuides["Front"].First(g=>g.Kind=="Straight");
            Check(MoveRelatedGuide("Front",firstLine with {Y1=40,Y2=40}),"Connected line movement rejected");
            Check(edgeGuides["Front"][1].Y1==40 && edgeGuides["Front"][1].Y2==100,"Neighbor did not stretch around shared corner");
            Check(MoveRelatedGuide("Front",edgeGuides["Front"][1] with {X1=110,X2=110}),"Reverse connected movement rejected");
            Check(edgeGuides["Front"][0].X2==110,"Reverse movement broke joint");
            EdgeCornerStyle.SelectedIndex=0;
            var group=Guid.NewGuid().ToString("N");var sourceLine=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,100,200,100,8,"Relative","Pointed",group,true);
            var targetLine=sourceLine with {Id=Guid.NewGuid().ToString("N")};edgeGuides["Front"]=[sourceLine];edgeGuides["Back"]=[targetLine];
            Check(SyncLockedLine("Front",sourceLine),"Parallel lock rejected original faces");
            Check(Math.Abs(edgeGuides["Back"][0].X1-(canvasWidth-100))<1e-6 && Math.Abs(edgeGuides["Back"][0].Y1-100)<1e-6,"Opposite view lock mapping failed");
            var moved=edgeGuides["Back"][0] with {Y1=140,Y2=140};edgeGuides["Back"][0]=moved;Check(SyncLockedLine("Back",moved) && Math.Abs(edgeGuides["Front"][0].Y1-140)<1e-6,"Reverse lock update failed");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(edgeGuides["Front"][0].LinkGroup==group && edgeGuides["Back"][0].PositionLocked,"Line connection lost on save");
            Check(!ParallelLines("Front","Left"),"Nonparallel faces accepted");
            var scene=new System.Windows.Media.Media3D.Model3DGroup();RenderPreviewEdgeLinks(scene);Check(previewEdgeModels.Count==2 && scene.Children.Count>2 && scene.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>().Skip(2).All(m=>((System.Windows.Media.SolidColorBrush)((System.Windows.Media.Media3D.EmissiveMaterial)m.Material).Brush).Color==System.Windows.Media.Colors.Red),"Locked edges must show red dotted center markers");
            edgeGuides["Front"][0]=edgeGuides["Front"][0] with {LinkGroup=null,PositionLocked=false};
            edgeGuides["Back"][0]=edgeGuides["Back"][0] with {LinkGroup=null,PositionLocked=false,Y1=180,Y2=180};
            highlightedGuides.Clear();highlightedGuides.Add(("Front",edgeGuides["Front"][0].Id));highlightedGuides.Add(("Back",edgeGuides["Back"][0].Id));
            LockSelectedGuides(this,new RoutedEventArgs());
            Check(edgeGuides["Front"][0].LockGroup!=null && edgeGuides["Front"][0].LinkGroup==null && edgeGuides["Back"][0].Y1==140,"Explicit lock did not align positions independently");
            Check(MoveRelatedGuide("Back",edgeGuides["Back"][0] with {Y1=190,Y2=190}) && edgeGuides["Front"][0].Y1==190,"New lock did not move bidirectionally");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(edgeGuides["Front"][0].LockGroup!=null,"Separate lock group lost on save");
            UnlockSelectedGuides(this,new RoutedEventArgs());
            Check(edgeGuides["Front"][0].LockGroup==null && !edgeGuides["Back"][0].PositionLocked,"Unlock failed");
            highlightedGuides.Clear();
            var ca=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",100,100,100,100);var cb=ca with {Id=Guid.NewGuid().ToString("N"),Y1=110,Y2=110};
            edgeGuides["Front"]=[ca];edgeGuides["Left"]=[cb];edgeGuides.Remove("Back");
            connectionEdges.Clear();highlightedGuides.Add(("Front",ca.Id));highlightedGuides.Add(("Left",cb.Id));
            ConnectSelectedGuides(this,new RoutedEventArgs());
            Check(connectionEdges.Count==1 && edgeGuides["Front"][0]==ca && edgeGuides["Left"][0]==cb,"Connect merged or repositioned its endpoints");
            var connection=connectionEdges[0];var endpointBefore=ConnectionPoint("Left",cb.Id,0);
            Check(MoveRelatedGuide("Left",cb with {Y1=130,Y2=130}),"Connected endpoint move failed");
            Check(ConnectionPoint("Left",cb.Id,0)!=endpointBefore && edgeGuides["Front"][0]==ca,"Connection did not follow its endpoint independently");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(connectionEdges.Single().Id==connection.Id,"3D edge lost on save");
            scene=new();RenderPreviewEdgeLinks(scene);Check(previewConnectionModels.Values.Any(c=>c.key==connection.Id && c.color==System.Windows.Media.Colors.Orange),"3D connection is not a solid ordinary edge");
            highlightedConnections.Add(connection.Id);RefreshGuideHighlights();Check(previewConnectionModels.Where(p=>p.Value.key==connection.Id).All(p=>((System.Windows.Media.SolidColorBrush)((System.Windows.Media.Media3D.EmissiveMaterial)p.Key.Material).Brush).Color==System.Windows.Media.Colors.DeepPink),"Connection highlight color failed");
            highlightedGuides.Clear();ClearPreviewLineConnection(this,new RoutedEventArgs());Check(connectionEdges.Count==0 && edgeGuides["Front"][0]==ca,"Dissolving a connection changed its endpoint");highlightedConnections.Clear();
            cb=edgeGuides["Left"][0];
            Check(MergePreviewCorners("Front",ca,"Left",cb),"Cross-face corner merge failed");
            Check((GuideWorldPoint("Front",edgeGuides["Front"][0],0)-GuideWorldPoint("Left",edgeGuides["Left"][0],0)).Length<1e-6,"Merged corners still differ in 3D depth");
            var cornerGroup=edgeGuides["Front"][0].CornerGroup;Check(cornerGroup!=null && edgeGuides["Left"][0].CornerGroup==cornerGroup,"Corners did not share a group");
            var cc=ca with {Id=Guid.NewGuid().ToString("N")};edgeGuides["Top"]=[cc];Check(MergePreviewCorners("Front",edgeGuides["Front"][0],"Top",cc),"Third face merge failed");
            var movedCorner=edgeGuides["Front"][0] with {Y1=160,Y2=160};
            Check(MoveRelatedGuide("Front",movedCorner),"Merged corner move rejected");
            Check(Math.Abs(edgeGuides["Left"][0].Y1-160)<1e-6,"Merged corner partner did not follow");
            var mergedPoint=GuideWorldPoint("Front",edgeGuides["Front"][0],0);
            Check(new[]{"Left","Top"}.All(f=>(GuideWorldPoint(f,edgeGuides[f][0],0)-mergedPoint).Length<1e-6),"Merged corner movement disconnected 3D positions");

            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(new[]{"Front","Left","Top"}.All(f=>edgeGuides[f][0].CornerGroup==cornerGroup),"Merged corners lost on save");
            var cornerBefore=edgeGuides["Left"][0];var countBefore=edgeGuides.Values.Sum(g=>g.Count);
            highlightedConnections.Add("merge|"+cornerGroup+"|"+cornerBefore.Id);
            DeleteSelectedMergeMarkers(this,new RoutedEventArgs());
            Check(edgeGuides["Left"][0]==cornerBefore with {CornerGroup=null} && edgeGuides.Values.Sum(g=>g.Count)==countBefore,"Delete merge marker removed or moved a corner");
            Check(edgeGuides["Front"][0].CornerGroup==cornerGroup && edgeGuides["Top"][0].CornerGroup==cornerGroup,"Delete marker dissolved unrelated merge");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(edgeGuides["Left"][0].CornerGroup==null,"Deleted merge returned after save");

        }
        finally {ApplyDocument(previous);Tool.SelectedIndex=oldTool;undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);}
    }
}
