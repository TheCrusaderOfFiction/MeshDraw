using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckPreviewDelete()
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        var previous=Document();var oldUndo=undo.ToList();var oldRedo=redo.ToArray();int oldMode=PreviewSelectionMode.SelectedIndex;
        try
        {
            ApplyDocument(previous with {EdgeGuides=new(),Connections=[],LineEqualities=[]});
            var lockGroup=Guid.NewGuid().ToString("N");var mergeGroup=Guid.NewGuid().ToString("N");
            var a=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Straight",100,100,200,100,LockGroup:lockGroup,PositionLocked:true);
            var b=a with {Id=Guid.NewGuid().ToString("N"),X1=canvasWidth-100,X2=canvasWidth-200};
            var ca=new EdgeGuideData(Guid.NewGuid().ToString("N"),"Corner",100,100,100,100,CornerGroup:mergeGroup);var cb=ca with {Id=Guid.NewGuid().ToString("N")};
            edgeGuides["Front"]=[a,ca];edgeGuides["Back"]=[b];edgeGuides["Left"]=[cb];
            var connection=new ConnectionEdgeData(Guid.NewGuid().ToString("N"),"Front",a.Id,0,"Back",b.Id,0);connectionEdges.Add(connection);
            undo.Clear();undo.Add(Snapshot());redo.Clear();highlightedGuides.Clear();highlightedConnections.Clear();
            highlightedGuides.Add(("Front",ca.Id));ClearPreviewLineConnection(this,new RoutedEventArgs());
            Check(edgeGuides["Front"][0].PositionLocked && edgeGuides["Back"][0].PositionLocked,"Dissolve unlocked unrelated edges");
            Undo(this,new RoutedEventArgs());Check(edgeGuides["Front"].Any(g=>g.Id==ca.Id),"Dissolve undo lost corner");
            PreviewSelectionMode.SelectedIndex=4;
            var key="lock|"+lockGroup+"|"+b.Id;
            Check(SelectionAllowsConnection(key) && !SelectionAllowsConnection(connection.Id) && !SelectionAllowsConnection("merge|"+mergeGroup+"|"+cb.Id),"Lock selection mode accepted other types");
            var scene=new Model3DGroup();RenderPreviewEdgeLinks(scene);highlightedConnections.Add(key);RefreshGuideHighlights();
            Check(previewConnectionModels.Any(p=>p.Value.key==key) && previewConnectionModels.Where(p=>p.Value.key==key).All(p=>((SolidColorBrush)((EmissiveMaterial)p.Key.Material).Brush).Color==Colors.DeepPink),"Lock markers not selectable/highlighted");
            DeleteSelectedPreviewItems(this,new RoutedEventArgs());
            Check(!edgeGuides["Front"][0].PositionLocked && !edgeGuides["Back"][0].PositionLocked && edgeGuides["Front"].Contains(ca) && edgeGuides["Left"].Contains(cb) && connectionEdges.Contains(connection),"Delete lock removed geometry, merges, or connections");
            Undo(this,new RoutedEventArgs());Check(edgeGuides["Front"][0].PositionLocked,"Delete lock undo failed");
            highlightedGuides.Add(("Back",b.Id));UnlockSelectedGuides(this,new RoutedEventArgs());Check(connectionEdges.Contains(connection) && edgeGuides["Front"].Contains(ca),"Unlock removed connection or corner");
            Undo(this,new RoutedEventArgs());
            highlightedGuides.Clear();highlightedConnections.Clear();highlightedGuides.Add(("Front",a.Id));DeleteSelectedPreviewItems(this,new RoutedEventArgs());
            Check(!edgeGuides["Front"].Any(g=>g.Id==a.Id) && connectionEdges.Count==0 && !edgeGuides["Back"][0].PositionLocked && edgeGuides["Front"].Contains(ca),"General edge delete left dangling relationships or deleted corners");
            Undo(this,new RoutedEventArgs());Check(connectionEdges.Contains(connection) && edgeGuides["Front"].Contains(a),"General delete undo failed");
            highlightedGuides.Clear();highlightedConnections.Add("merge|"+mergeGroup+"|"+cb.Id);DeleteSelectedPreviewItems(this,new RoutedEventArgs());
            Check(edgeGuides["Left"][0]==cb with {CornerGroup=null} && edgeGuides["Front"].Contains(a),"General merge delete removed a corner or edge");
            ApplyDocument(DrawingDocument.Read(Snapshot()));Check(edgeGuides["Left"][0].CornerGroup==null,"Deletion did not persist");
        }
        finally
        {
            PreviewSelectionMode.SelectedIndex=oldMode;highlightedGuides.Clear();highlightedConnections.Clear();ApplyDocument(previous);undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);
        }
    }
}
