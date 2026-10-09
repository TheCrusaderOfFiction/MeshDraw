using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeshDraw;
public partial class EditorWindow
{
    private readonly Dictionary<string,List<EdgeGuideData>> edgeGuides=new();
    private bool drawingEdgeGuide;
    private Point edgeGuideStart,edgeGuideEnd;
    private Point EdgePoint(Point point)=>new(Math.Clamp(point.X,0,canvasWidth),Math.Clamp(point.Y,0,canvasHeight));
    private void AddEdgeGuide(EdgeGuideData guide)
    {
        var document=Document();var list=document.EdgeGuides!.GetValueOrDefault(ActiveView) ?? [];
        if(list.Count>=128) {Status.Text="This face already has 128 edge marks.";return;}
        list.Add(guide);document.EdgeGuides![ActiveView]=list;DrawingDocument.Validate(document);
        edgeGuides[ActiveView]=list;RecordChange();RenderRig();
        Status.Text=guide.Kind=="Straight" ? "Marked a straight edge. Generation will flatten and simplify that boundary." : "Marked a sharp corner. Generation will preserve that corner.";
    }
    private bool BeginEdgeGuide(Canvas canvas,Point point)
    {
        point=EdgePoint(point);
        if(EdgeGuideMode.SelectedIndex==2)
        {
            var nearest=(edgeGuides.GetValueOrDefault(ActiveView) ?? []).Select(g=>new {Guide=g,Distance=GuideDistance(point,g)})
                .Where(g=>g.Distance<=Math.Max(12,g.Guide.Radius)).OrderBy(g=>g.Distance).FirstOrDefault();
            if(nearest!=null) {edgeGuides[ActiveView].Remove(nearest.Guide);RecordChange();RenderRig();Status.Text="Removed edge mark.";}
            return true;
        }
        if(EdgeGuideMode.SelectedIndex==1) {AddEdgeGuide(new(Guid.NewGuid().ToString("N"),"Corner",point.X,point.Y,point.X,point.Y,EdgeGuideRadius.Value));return true;}
        if((edgeGuides.GetValueOrDefault(ActiveView)?.Count ?? 0)>=128) {Status.Text="This face already has 128 edge marks.";return true;}
        drawingEdgeGuide=true;edgeGuideStart=edgeGuideEnd=point;guidedCanvas=canvas;canvas.CaptureMouse();canvas.CaptureStylus();RenderRig();return true;
    }
    private void ContinueEdgeGuide(Point point) {edgeGuideEnd=EdgePoint(point);RenderRig();}
    private void EndEdgeGuide(Point point)
    {
        edgeGuideEnd=EdgePoint(point);var start=edgeGuideStart;var end=edgeGuideEnd;CancelGuidedStroke();
        if((end-start).Length<4) {RenderRig();Status.Text="Drag along the outline to mark a straight edge, or choose Sharp corner to tap a corner.";return;}
        AddEdgeGuide(new(Guid.NewGuid().ToString("N"),"Straight",start.X,start.Y,end.X,end.Y,EdgeGuideRadius.Value));
    }
    private static double GuideDistance(Point point,EdgeGuideData guide)
    {
        var a=new Point(guide.X1,guide.Y1);var direction=new Vector(guide.X2-guide.X1,guide.Y2-guide.Y1);
        if(guide.Kind=="Corner" || direction.LengthSquared<.001)return (point-a).Length;
        var t=Math.Clamp(Vector.Multiply(point-a,direction)/direction.LengthSquared,0,1);return (point-(a+direction*t)).Length;
    }
    private void RenderEdgeGuides(Canvas canvas,string face)
    {
        if(ShowEdgeGuides?.IsChecked!=true && Tool.SelectedIndex!=9) return;
        foreach(var guide in edgeGuides.GetValueOrDefault(face) ?? [])
        {
            if(guide.Kind=="Straight")
            {
                canvas.Children.Add(new Line {X1=guide.X1,Y1=guide.Y1,X2=guide.X2,Y2=guide.Y2,Stroke=Brushes.DarkOrange,StrokeThickness=3,IsHitTestVisible=false});
                foreach(var point in new[]{new Point(guide.X1,guide.Y1),new Point(guide.X2,guide.Y2)})
                {var dot=new Ellipse {Width=8,Height=8,Fill=Brushes.DarkOrange,Stroke=Brushes.White,StrokeThickness=1,IsHitTestVisible=false};Canvas.SetLeft(dot,point.X-4);Canvas.SetTop(dot,point.Y-4);canvas.Children.Add(dot);}
            }
            else
            {
                var corner=new Rectangle {Width=12,Height=12,Stroke=Brushes.DarkOrange,StrokeThickness=3,Fill=Brushes.White,IsHitTestVisible=false};Canvas.SetLeft(corner,guide.X1-6);Canvas.SetTop(corner,guide.Y1-6);canvas.Children.Add(corner);
            }
        }
        if(drawingEdgeGuide && face==ActiveView) canvas.Children.Add(new Line {X1=edgeGuideStart.X,Y1=edgeGuideStart.Y,X2=edgeGuideEnd.X,Y2=edgeGuideEnd.Y,Stroke=Brushes.DarkOrange,StrokeThickness=3,StrokeDashArray=new DoubleCollection {3,2},IsHitTestVisible=false});
    }
    private void ClearEdgeGuides(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();if(!edgeGuides.Remove(ActiveView))return;RecordChange();RenderRig();Status.Text="Cleared edge marks on "+FaceName(ActiveView)+".";
    }
    private void RemoveLastEdgeGuide(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();if(edgeGuides.TryGetValue(ActiveView,out var list) && list.Count>0) {list.RemoveAt(list.Count-1);RecordChange();RenderRig();}
    }
    private void EdgeGuideVisibilityChanged(object sender,RoutedEventArgs e) {if(drawingViews.Count>=6)RenderRig();}
}
