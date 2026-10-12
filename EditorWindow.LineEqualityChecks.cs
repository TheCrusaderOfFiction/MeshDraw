using System.Windows;
namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckLineEquality()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        var previous=Document();var oldUndo=undo.ToList();var oldRedo=redo.ToArray();
        try
        {
            ApplyDocument(previous with {EdgeGuides=new(),Connections=[],LineEqualities=[],CanvasWidth=640,CanvasHeight=640});Views.SelectedIndex=0;
            var face=new FaceDefinition("custom_"+Guid.NewGuid().ToString("N"),"Equality angled face",Yaw:25,IsSection:true);extraFaces.Add(face);CreateFaceSurface(face.Id,face.Name);
            var whole=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,200,500,200);
            var first=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",540,200,340,200);
            var second=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",300,200,500,200);
            edgeGuides["Front"]=[whole];edgeGuides["Back"]=[first];edgeGuides[face.Id]=[second];
            highlightedGuides.Clear();highlightedConnections.Clear();highlightedGuides.Add(("Front",whole.Id));SetEqualityReference(this,new RoutedEventArgs());
            highlightedGuides.Add(("Back",first.Id));highlightedGuides.Add((face.Id,second.Id));
            undo.Clear();undo.Add(Snapshot());redo.Clear();AssignLineEquality(this,new RoutedEventArgs());
            Check(lineEqualities.Count==1,"Projected 1-to-2 relationship was not assigned: "+PreviewLineSelection.Text);
            void CheckProjection()
            {
                var relation=lineEqualities.Single();var a=EqualityPoints(relation.First,relation.ReverseFirst);var b=EqualityPoints(relation.Second,relation.ReverseSecond);var w=EqualityGuide(relation.Whole);
                var start=ProjectLinePoint("Front",a.a);var joint=ProjectLinePoint("Front",a.b);var otherJoint=ProjectLinePoint("Front",b.a);var end=ProjectLinePoint("Front",b.b);
                Check((start-new Point(w.X1,w.Y1)).Length<1e-6 && (joint-otherJoint).Length<1e-6 && (end-new Point(w.X2,w.Y2)).Length<1e-6,"Partner lines do not match the reference face projection");
            }
            CheckProjection();Check(Math.Abs(EqualityPoints(new("Back",first.Id)).a.Z+1)<1e-6,"Matching flattened a partner onto the reference face");
            Undo(this,new RoutedEventArgs());Check(lineEqualities.Count==0 && edgeGuides[face.Id][0]==second,"1-to-2 assignment undo failed");
            Redo(this,new RoutedEventArgs());Check(lineEqualities.Count==1,"1-to-2 assignment redo failed");CheckProjection();
            Check(MoveRelatedGuide("Front",edgeGuides["Front"][0] with {Y1=230,Y2=230}),"Whole-line movement failed");CheckProjection();
            Check(MoveRelatedGuide("Back",edgeGuides["Back"][0] with {Y1=240,Y2=240}),"Partner-line movement failed");CheckProjection();
            var before=Snapshot();Check(!MoveRelatedGuide("Front",edgeGuides["Front"][0] with {Y1=-100,Y2=-100}) && Snapshot()==before,"Invalid 1-to-2 movement did not roll back atomically");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(lineEqualities.Count==1,"Projected relationship lost on save");CheckProjection();
            RecordChange();var relationToDelete=lineEqualities.Single();highlightedGuides.Clear();highlightedConnections.Add("equality|"+relationToDelete.Id+"|"+relationToDelete.First.Id);
            var guideCount=edgeGuides.Values.Sum(g=>g.Count);DeleteSelectedPreviewItems(this,new RoutedEventArgs());Check(lineEqualities.Count==0 && edgeGuides.Values.Sum(g=>g.Count)==guideCount,"Deleting 1-to-2 lock removed its edges");
            Undo(this,new RoutedEventArgs());Check(lineEqualities.Count==1,"Deleting 1-to-2 lock undo failed");CheckProjection();
            highlightedGuides.Clear();highlightedGuides.Add(("Back",first.Id));ClearLineEqualities(this,new RoutedEventArgs());Check(lineEqualities.Count==0,"Clear 1-to-2 relationship failed");
            var inside=face with {Yaw=90};extraFaces[extraFaces.FindIndex(f=>f.Id==face.Id)]=inside;
            whole=whole with {X1=320,Y1=100,X2=320,Y2=500};first=first with {X1=100,Y1=100,X2=100,Y2=300};second=second with {X1=500,Y1=300,X2=500,Y2=500};
            var insideCorner=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",320,100,320,100);
            edgeGuides.Clear();edgeGuides[face.Id]=[whole,insideCorner];edgeGuides["Front"]=[first,second];
            highlightedGuides.Clear();highlightedConnections.Clear();highlightedGuides.Add((face.Id,whole.Id));SetEqualityReference(this,new RoutedEventArgs());highlightedGuides.Add(("Front",first.Id));highlightedGuides.Add(("Front",second.Id));AssignLineEquality(this,new RoutedEventArgs());
            Check(lineEqualities.Count==1,"Perpendicular inside-face match rejected: "+PreviewLineSelection.Text);
            Check(edgeGuides[face.Id][0]==whole && edgeGuides[face.Id][1]==insideCorner,"Matching moved the inside reference or its corner");
            var insideRelation=lineEqualities.Single();var ip=EqualityPoints(insideRelation.Whole);var fp=EqualityPoints(insideRelation.First,insideRelation.ReverseFirst);var sp=EqualityPoints(insideRelation.Second,insideRelation.ReverseSecond);
            Check((ProjectLinePoint(face.Id,ip.a)-ProjectLinePoint(face.Id,fp.a)).Length<1e-6 && (ProjectLinePoint(face.Id,ip.b)-ProjectLinePoint(face.Id,sp.b)).Length<1e-6,"Inside-face projected endpoints did not align");
            Check(Math.Abs(edgeGuides["Front"][0].X1-100)<1e-6 && Math.Abs(edgeGuides["Front"][1].X1-500)<1e-6,"Perpendicular match moved the front-face edges across the blade");
            Check(Math.Abs(fp.a.Z-ip.a.Z)<1e-6 && Math.Abs(sp.b.Z-ip.b.Z)<1e-6 && Math.Abs(edgeGuides["Front"][0].Depth1)>0.5,"Front edges did not take the inside reference depth");
            Check(MoveRelatedGuide(face.Id,edgeGuides[face.Id][0] with {Y1=120,Y2=520}),"Perpendicular match movement failed");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(lineEqualities.Count==1,"Perpendicular relationship lost on save");

        }
        finally
        {
            highlightedGuides.Clear();highlightedConnections.Clear();equalityReference=null;ApplyDocument(previous);undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);
        }
    }
}
