using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckFaceCopyAndVertexLimit(string folder)
    {
        static void Check(bool value,string message) {if(!value)throw new Exception(message);}
        var previous=Document();var oldUndo=undo.ToList();var oldRedo=redo.ToArray();
        try
        {
            var empty=DrawingDocument.Encode(new StrokeCollection());
            var strokes=new StrokeCollection {new Stroke(new StylusPointCollection {new StylusPoint(24,40),new StylusPoint(50,55)})};
            var ink=DrawingDocument.Encode(strokes);
            var pixels=new byte[256*192*4];for(int y=30;y<60;y++)for(int x=20;x<55;x++) {var i=(y*256+x)*4;pixels[i+2]=128;pixels[i+3]=128;}
            LayerData Layer(string name,LayerKind kind,string data,bool locked=false,string? image=null)=>new(Guid.NewGuid().ToString("N"),name,kind,true,locked,.7,data,image);
            var layers=DrawingDocument.ViewNames.ToDictionary(f=>f,f=>new List<LayerData> {Layer("Outline",LayerKind.Artwork,empty),Layer("Color",LayerKind.BaseColor,empty)});
            layers["Front"]=[Layer("Outline",LayerKind.Artwork,ink),Layer("Color",LayerKind.BaseColor,empty,false,EncodeRaster(pixels,256,192)),Layer("Roughness",LayerKind.Roughness,ink),Layer("Photo",LayerKind.Reference,empty,true,EncodeRaster(pixels,256,192))];
            var extra=new FaceDefinition("custom_"+Guid.NewGuid().ToString("N"),"Center slice",45,0,.3,true);layers[extra.Id]=[Layer("Outline",LayerKind.Artwork,empty),Layer("Color",LayerKind.BaseColor,empty)];
            ApplyDocument(new EditorDocument(2,0,layers,new([],[]),256,192,"Custom",[extra]));Views.SelectedIndex=0;
            SetSelection(new RectangleGeometry(new Rect(18,25,50,60)));
            var source=JsonSerializer.Serialize(Document().Layers["Front"]);undo.Clear();undo.Add(Snapshot());redo.Clear();var before=Snapshot();
            CopyFace("Front",["Back","Left"],false,false,false);
            Check(drawingViews["Back"].Count==6 && drawingViews["Left"].Count==6,"Append copy lost destination layers");
            Check(JsonSerializer.Serialize(Document().Layers["Front"])==source,"Face copy changed source");
            var sourceData=Document().Layers["Front"];
            foreach(var face in new[]{"Back","Left"}) for(int i=0;i<4;i++)
            {
                var data=DrawingDocument.Data(drawingViews[face][i]);Check(data.Id!=sourceData[i].Id && data with {Id=sourceData[i].Id}==sourceData[i],"Copied layer properties or independent IDs failed");
            }
            Check(SelectionFor("Back")==null,"Copy unexpectedly replaced selection");Check(undo.Count==2,"Multi-face copy was not one undo step");var copied=Snapshot();
            Undo(this,new RoutedEventArgs());Check(Snapshot()==before,"Copy undo failed");Redo(this,new RoutedEventArgs());Check(Snapshot()==copied,"Copy redo failed");
            drawingViews["Back"][0].Ink.Strokes[0].StylusPoints[0]=new StylusPoint(100,100);
            Check(JsonSerializer.Serialize(Document().Layers["Front"])==source,"Editing copied stroke changed source");ApplyDocument(DrawingDocument.Read(copied));
            var protectedState=Snapshot();bool rejected=false;
            try {CopyFace("Front",["Back"],true,false,false);}catch(InvalidOperationException) {rejected=true;}
            Check(rejected && Snapshot()==protectedState,"Replace overwrote locked destination layer");
            CopyFace("Front",["Right",extra.Id],true,true,true);
            Check(drawingViews["Right"].Count==4 && drawingViews[extra.Id].Count==4,"Replace copy has wrong layer count");
            Check(extraFaces.Single()==extra,"Copy changed face angle or slice position");
            var point=drawingViews["Right"][0].Ink.Strokes[0].StylusPoints[0];var originalPoint=drawingViews["Front"][0].Ink.Strokes[0].StylusPoints[0];
            // WPF's saved ink coordinates quantize to hundredths of a pixel.
            Check(Math.Abs(point.X-(256-originalPoint.X))<.1 && Math.Abs(point.Y-originalPoint.Y)<.1,"Copied strokes did not flip: "+point.X+", "+point.Y);
            Check(SelectionFor("Right")!.FillContains(new Point(220,50)) && !SelectionFor("Right")!.FillContains(new Point(30,50)),"Copied selection did not flip");
            var flipped=LayerPixels(new[]{drawingViews["Right"][1]});Check(flipped[(40*256+220)*4+3]>0 && flipped[(40*256+30)*4+3]==0,"Copied photo did not flip");
            var overloaded=Document();overloaded.Layers["Top"]=Enumerable.Range(0,32).Select(i=>Layer("Layer "+i,LayerKind.Artwork,empty)).ToList();ApplyDocument(overloaded);
            protectedState=Snapshot();rejected=false;try {CopyFace("Front",["Bottom","Top"],false,false,false);}catch(InvalidOperationException) {rejected=true;}
            Check(rejected && Snapshot()==protectedState,"Multi-target preflight partially changed a face");
            VertexLimitInput.Text="4200";CommitVertexLimit(this,new RoutedEventArgs());Check(vertexLimit==4200 && VertexLimitSlider.Value==4200 && Document().VertexLimit==4200,"Vertex setting input not synchronized");
            VertexLimitSlider.Value=6000;CommitVertexLimit(this,new RoutedEventArgs());Check(VertexLimitInput.Text=="6000" && vertexLimit==6000,"Vertex slider not synchronized");
            VertexLimitInput.Text="garbage";CommitVertexLimit(this,new RoutedEventArgs());Check(vertexLimit==6000 && VertexLimitInput.Text=="6000","Invalid vertex input changed limit");
            VertexLimitInput.Text="20";CommitVertexLimit(this,new RoutedEventArgs());Check(vertexLimit==256,"Vertex limit minimum not enforced");
            VertexLimitInput.Text="999999";CommitVertexLimit(this,new RoutedEventArgs());Check(vertexLimit==100000,"Vertex limit maximum not enforced");
            LoadVertexLimit(4200);RecordChange();var saved=Snapshot();File.WriteAllText(Path.Combine(folder,"copied-faces.meshdraw"),saved);ApplyDocument(DrawingDocument.Read(saved));
            Check(vertexLimit==4200 && drawingViews[extra.Id][1].Photo!=null && SelectionFor(extra.Id)!=null,"Copy or vertex limit lost on load");
            var legacy=JsonSerializer.SerializeToNode(Document())!;legacy.AsObject().Remove("VertexLimit");Check(DrawingDocument.Read(legacy.ToJsonString()).VertexLimit==50000,"Legacy vertex default failed");
            rejected=false;try {DrawingDocument.Validate(Document() with {VertexLimit=255});}catch(InvalidDataException) {rejected=true;}Check(rejected,"Invalid saved vertex limit accepted");
        }
        finally {ApplyDocument(previous);undo.Clear();undo.AddRange(oldUndo);redo.Clear();foreach(var state in oldRedo.Reverse())redo.Push(state);}
    }
}
