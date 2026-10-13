using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeshDraw;
public partial class EditorWindow
{
    private static readonly Guid PaintedGuideId=new("38354b83-4a9c-4f60-b302-fca1cb387092");
    private EdgeGuideData? curveDraft;
    private string? curveEditId;
    private Canvas? curveCanvas;
    private PaintLayer? curveLayer;
    private bool curveDragging,curveCreating;
    private int curveHandle;
    internal static List<Point> GuidePathPoints(EdgeGuideData guide)
    {
        var a=new Point(guide.X1,guide.Y1);var b=new Point(guide.X2,guide.Y2);
        if(guide.Kind!="Curve")return [a,b];
        var c=new Point(guide.Control1X!.Value,guide.Control1Y!.Value);var d=new Point(guide.Control2X!.Value,guide.Control2Y!.Value);
        int count=Math.Clamp((int)Math.Ceiling(((c-a).Length+(d-c).Length+(b-d).Length)/1.5),8,32768);
        return Enumerable.Range(0,count+1).Select(i=> {double t=(double)i/count,q=1-t;return new Point(q*q*q*a.X+3*q*q*t*c.X+3*q*t*t*d.X+t*t*t*b.X,q*q*q*a.Y+3*q*q*t*c.Y+3*q*t*t*d.Y+t*t*t*b.Y);}).ToList();
    }
    private EdgeGuideData NewCurve(Point a,Point b)=>new(Guid.NewGuid().ToString("N"),CurveLineKind.SelectedIndex==1?"Straight":"Curve",a.X,a.Y,b.X,b.Y,EdgeGuideRadius.Value,EdgeGuideBehavior.SelectedIndex==1?"Absolute":"Relative",Control1X:a.X+(b.X-a.X)/3,Control1Y:a.Y+(b.Y-a.Y)/3,Control2X:a.X+(b.X-a.X)*2/3,Control2Y:a.Y+(b.Y-a.Y)*2/3);
    private bool BeginCurveLine(Canvas canvas,Point point)
    {
        if(SelectionEditing || ActiveLayer is not {Locked:false,Visible:true,Opacity:>0} layer) {Status.Text="Choose an unlocked drawing layer to create a curve.";return true;}
        point=EdgePoint(point);curveCreating=false;
        if(curveDraft==null)
        {
            var existing=(edgeGuides.GetValueOrDefault(ActiveView) ?? []).Where(g=>g.Kind=="Curve" && GuideDistance(point,g)<12/RulerDisplayScale(canvas)).OrderBy(g=>GuideDistance(point,g)).FirstOrDefault();
            curveDraft=existing ?? NewCurve(point,point);curveEditId=existing?.Id;curveCanvas=canvas;curveLayer=layer;
            if(existing!=null) {RenderRig();Status.Text="Drag the curve's ends or blue control points, then Apply.";return true;}
            curveCreating=true;curveHandle=1;
        }
        else
        {
            var handles=CurveHandles(curveDraft);curveHandle=Array.FindIndex(handles,p=>(p-point).Length<=12/RulerDisplayScale(canvas));
            if(curveHandle<0) {Status.Text="Drag a control point, then Apply or Cancel this line.";return true;}
        }
        curveDragging=true;guidedCanvas=canvas;canvas.CaptureMouse();canvas.CaptureStylus();RenderRig();return true;
    }
    private static Point[] CurveHandles(EdgeGuideData g)=>g.Kind=="Curve" ? [new(g.X1,g.Y1),new(g.X2,g.Y2),new(g.Control1X!.Value,g.Control1Y!.Value),new(g.Control2X!.Value,g.Control2Y!.Value)] : [new(g.X1,g.Y1),new(g.X2,g.Y2)];
    private void ContinueCurveLine(Point point)
    {
        if(curveDraft==null)return;point=EdgePoint(point);
        if(curveCreating) {var id=curveDraft.Id;curveDraft=NewCurve(new(curveDraft.X1,curveDraft.Y1),point) with {Id=id};}
        else curveDraft=curveHandle switch {0=>curveDraft with {X1=point.X,Y1=point.Y},1=>curveDraft with {X2=point.X,Y2=point.Y},2=>curveDraft with {Control1X=point.X,Control1Y=point.Y},_=>curveDraft with {Control2X=point.X,Control2Y=point.Y}};
        RenderRig();
    }
    private void EndCurveLine(Point point)
    {
        ContinueCurveLine(point);curveDragging=curveCreating=false;var canvas=guidedCanvas;guidedCanvas=null;canvas?.ReleaseMouseCapture();canvas?.ReleaseStylusCapture();
        Status.Text="Drag the blue points to bend the curve, then Apply line.";RenderRig();
    }
    private void ClearCurveDraft() {curveDraft=null;curveEditId=null;curveCanvas=null;curveLayer=null;curveDragging=curveCreating=false;}
    private void CancelCurveLine(object sender,RoutedEventArgs e) {CancelGuidedStroke();RenderRig();Status.Text="Line cancelled.";}
    private void CurveShapeChanged(object sender,SelectionChangedEventArgs e) {if(drawingViews.Count<6)return;CancelGuidedStroke();RenderRig();}
    private Stroke PaintedLine(EdgeGuideData guide,DrawingAttributes attributes)
    {
        attributes=attributes.Clone();attributes.FitToCurve=false;
        var stroke=CreateGuidedStroke(GuidePathPoints(guide),attributes,StrokeFade.IsChecked==true,FadeAmount.Value);
        stroke.AddPropertyData(PaintedGuideId,guide.Id);return stroke;
    }
    private void PaintGuide(EdgeGuideData guide,bool record=true)
    {
        if(ActiveLayer is not {Locked:false,Visible:true,Opacity:>0} layer || SelectionEditing) {Status.Text="Choose an unlocked drawing layer to paint the line.";return;}
        foreach(var target in FaceTargets(layer))foreach(var old in target.layer.Ink.Strokes.Where(s=>s.ContainsPropertyData(PaintedGuideId) && (string)s.GetPropertyData(PaintedGuideId)==guide.Id).ToList())target.layer.Ink.Strokes.Remove(old);
        CommitFaceStroke(layer,PaintedLine(guide,layer.Ink.DefaultDrawingAttributes),record);
    }
    private void ApplyCurveLine(object sender,RoutedEventArgs e)
    {
        if(curveDraft==null || curveLayer==null)return;
        if(curveLayer!=ActiveLayer || curveLayer.Locked || !curveLayer.Visible) {CancelCurveLine(sender,e);return;}
        var guide=curveDraft;var points=GuidePathPoints(guide);
        if(points.Zip(points.Skip(1),(a,b)=>(b-a).Length).Sum()<4) {Status.Text="Make the line at least 4 pixels long.";return;}
        bool paint=CurveOutput.SelectedIndex!=2,mark=CurveOutput.SelectedIndex!=1;
        var list=edgeGuides.GetValueOrDefault(ActiveView) ?? [];
        if(mark && curveEditId==null && list.Count>=128) {Status.Text="This face already has 128 edge marks.";return;}
        if(mark) {if(curveEditId!=null)list.RemoveAll(g=>g.Id==curveEditId);list.Add(guide);edgeGuides[ActiveView]=list;}
        if(paint)PaintGuide(guide,false);
        ClearCurveDraft();RecordChange();RenderRig();Status.Text="Applied "+(guide.Kind=="Curve"?"curved":"straight")+" line.";
    }
    private void RenderCurveDraft(Canvas canvas,string face)
    {
        if(curveDraft==null || curveCanvas!=canvas || face!=ActiveView)return;
        var points=GuidePathPoints(curveDraft);
        if(curveLayer!=null && CurveOutput.SelectedIndex!=2) {var stroke=PaintedLine(curveDraft,curveLayer.Ink.DefaultDrawingAttributes);canvas.Children.Add(new System.Windows.Shapes.Path {Data=stroke.GetGeometry(),Fill=new SolidColorBrush(stroke.DrawingAttributes.Color),Opacity=curveLayer.Opacity,Clip=SelectionFor(face),IsHitTestVisible=false});}
        canvas.Children.Add(new Polyline {Points=new PointCollection(points),Stroke=Brushes.DarkOrange,StrokeThickness=2/RulerDisplayScale(canvas),IsHitTestVisible=false});
        var handles=CurveHandles(curveDraft);
        if(handles.Length==4) foreach(var (a,b) in new[]{(handles[0],handles[2]),(handles[1],handles[3])})canvas.Children.Add(new Line {X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=Brushes.DeepSkyBlue,StrokeThickness=1/RulerDisplayScale(canvas),StrokeDashArray=new DoubleCollection {4,3},IsHitTestVisible=false});
        double size=10/RulerDisplayScale(canvas);
        for(int i=0;i<handles.Length;i++) {var dot=new Ellipse {Width=size,Height=size,Fill=i<2?Brushes.DarkOrange:Brushes.DeepSkyBlue,Stroke=Brushes.White,StrokeThickness=1/RulerDisplayScale(canvas),IsHitTestVisible=false};Canvas.SetLeft(dot,handles[i].X-size/2);Canvas.SetTop(dot,handles[i].Y-size/2);canvas.Children.Add(dot);}
    }
}
