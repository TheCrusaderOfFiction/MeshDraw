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
            var invalid=Document();invalid.EdgeGuides!["Front"][0]=invalid.EdgeGuides["Front"][0] with {X1=double.NaN};
            bool rejected=false;try {DrawingDocument.Validate(invalid);}catch(InvalidDataException) {rejected=true;}Check(rejected,"Invalid guide coordinate accepted");
        }
        finally {ApplyDocument(previous);Tool.SelectedIndex=oldTool;undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);}
    }
}
