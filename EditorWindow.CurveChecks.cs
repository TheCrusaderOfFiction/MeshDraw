using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MeshDraw;
public partial class EditorWindow
{
    private void CheckCurves(string folder)
    {
        static void Assert(bool value,string text){if(!value)throw new Exception(text);}
        StartDrawing(640,640,0,"None",new([],[]));Tool.SelectedIndex=10;CurveOutput.SelectedIndex=0;CurveLineKind.SelectedIndex=0;StrokeFade.IsChecked=false;ShowRig.IsChecked=false;
        var before=Snapshot();var centerCanvas=overlays["Front"];
        Assert(BeginCurveLine(centerCanvas,new(100,400)),"Curve tool did not start");EndCurveLine(new(500,400));
        Assert(curveDraft!=null && ActiveLayer!.Ink.Strokes.Count==0,"Curve draft painted before Apply");
        BeginCurveLine(centerCanvas,new(curveDraft!.Control1X!.Value,curveDraft.Control1Y!.Value));EndCurveLine(new(180,100));
        BeginCurveLine(centerCanvas,new(curveDraft!.Control2X!.Value,curveDraft.Control2Y!.Value));EndCurveLine(new(420,100));
        ApplyCurveLine(this,new RoutedEventArgs());
        var guide=edgeGuides["Front"].Single();var stroke=ActiveLayer!.Ink.Strokes.Single();
        Assert(guide.Kind=="Curve" && !stroke.DrawingAttributes.FitToCurve && stroke.StylusPoints.Min(p=>p.Y)<200,"Curve was drawn as a straight line");
        var both=Snapshot();DrawingDocument.Read(both);
        Undo(this,new RoutedEventArgs());Assert(Snapshot()==before,"Curve + paint undo was not atomic");Redo(this,new RoutedEventArgs());Assert(Snapshot()==both,"Curve redo failed");
        centerCanvas=overlays["Front"];
        Tool.SelectedIndex=10;BeginCurveLine(centerCanvas,GuidePathPoints(guide)[GuidePathPoints(guide).Count/2]);
        Assert(curveEditId==guide.Id,"Existing curve was not editable");
        BeginCurveLine(centerCanvas,new(180,100));EndCurveLine(new(180,160));ApplyCurveLine(this,new RoutedEventArgs());
        Assert(edgeGuides["Front"].Count==1 && ActiveLayer!.Ink.Strokes.Count==1,"Editing a curve duplicated the line");
        CopyFace("Front",["Back"],true,false,true,true);
        centerCanvas=overlays["Front"];
        var mirrored=edgeGuides["Back"].Single();Assert(Math.Abs(mirrored.Control1X!.Value-(640-edgeGuides["Front"][0].Control1X!.Value))<.01,"Copied curve controls did not mirror");
        Assert((string)drawingViews["Back"][0].Ink.Strokes.Single().GetPropertyData(PaintedGuideId)==mirrored.Id,"Copied painted curve lost its guide association");
        CurveOutput.SelectedIndex=1;CurveLineKind.SelectedIndex=1;Tool.SelectedIndex=10;
        BeginCurveLine(centerCanvas,new(90,500));EndCurveLine(new(530,500));ApplyCurveLine(this,new RoutedEventArgs());
        Assert(ActiveLayer!.Ink.Strokes.Count==2 && edgeGuides["Front"].Count==1,"Drawing-only straight line added a guide");
        var cancelled=Snapshot();BeginCurveLine(centerCanvas,new(50,50));EndCurveLine(new(300,50));CancelCurveLine(this,new RoutedEventArgs());Assert(Snapshot()==cancelled,"Cancel changed drawing");
        Tool.SelectedIndex=9;EdgeGuideMode.SelectedIndex=0;PaintEdgeLines.IsChecked=true;ConnectEdgeLines.IsChecked=false;
        BeginEdgeGuide(centerCanvas,new(100,550));EndEdgeGuide(new(500,550));
        Assert(edgeGuides["Front"].Any(g=>g.Kind=="Straight") && ActiveLayer!.Ink.Strokes.Count==3,"Straight edge did not paint");
        EdgeGuideMode.SelectedIndex=7;BeginEdgeGuide(centerCanvas,new(300,550));Assert(ActiveLayer!.Ink.Strokes.Count==3,"Painting existing straight edge duplicated its stroke");
        var export=Path.Combine(folder,"curved-line-export");Directory.CreateDirectory(export);WriteDrawings(export);File.WriteAllText(Path.Combine(export,"project.meshdraw"),Snapshot());
        var content=(FrameworkElement)Content;content.Measure(new Size(1408,880));content.Arrange(new Rect(0,0,1408,880));content.UpdateLayout();
        Tool.SelectedIndex=10;CurveLineKind.SelectedIndex=0;CurveOutput.SelectedIndex=0;ApplyTools();var curve=edgeGuides["Front"].First(g=>g.Kind=="Curve");BeginCurveLine(centerCanvas,GuidePathPoints(curve)[GuidePathPoints(curve).Count/2]);
        Assert(centerCanvas.Children.OfType<System.Windows.Shapes.Ellipse>().Count(s=>s.Fill==Brushes.DeepSkyBlue)>=2,"Curve control handles are not visible");
        content.UpdateLayout();var bmp=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);bmp.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine(folder,"curves-preview.png")))png.Save(stream);CancelGuidedStroke();
        File.WriteAllText(Path.Combine(folder,"curve-checks.txt"),"PASS: Bezier controls, both outputs, atomic undo/redo, saved guides and strokes, editable curve, mirrored copy, drawing-only straight line, cancellation, painted straight guides, existing-line painting.");
        PaintEdgeLines.IsChecked=false;ConnectEdgeLines.IsChecked=true;EdgeGuideMode.SelectedIndex=0;CurveOutput.SelectedIndex=0;CurveLineKind.SelectedIndex=0;Tool.SelectedIndex=0;
    }
}
