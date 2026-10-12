using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeshDraw;
public partial class EditorWindow
{
    private readonly Dictionary<string,List<EdgeGuideData>> edgeGuides=new();
    private bool drawingEdgeGuide;
    private EdgeGuideData? movingEdgeGuide;
    private Point edgeGuideStart,edgeGuideEnd;
    private Point EdgePoint(Point point)=>new(Math.Clamp(point.X,0,canvasWidth),Math.Clamp(point.Y,0,canvasHeight));
    private void AddEdgeGuide(EdgeGuideData guide)
    {
        var document=Document();var list=document.EdgeGuides!.GetValueOrDefault(ActiveView) ?? [];
        if(list.Count>=128) {Status.Text="This face already has 128 edge marks.";return;}
        list.Add(guide);document.EdgeGuides![ActiveView]=list;DrawingDocument.Validate(document);
        edgeGuides[ActiveView]=list;RecordChange();RenderRig();
        Status.Text=guide.Kind=="Straight" ? "Marked a straight edge. Generation will flatten and simplify that boundary." : "Marked a "+guide.CornerStyle.ToLowerInvariant()+" corner.";
    }
    private bool BeginEdgeGuide(Canvas canvas,Point point)
    {
        point=EdgePoint(point);
        if(EdgeGuideMode.SelectedIndex is 2 or 3 or 4)
        {
            var nearest=(edgeGuides.GetValueOrDefault(ActiveView) ?? []).Select(g=>new {Guide=g,Distance=GuideDistance(point,g)})
                .Where(g=>(EdgeGuideMode.SelectedIndex!=4 || g.Guide.Kind=="Corner") && g.Distance<=Math.Max(12,g.Guide.Radius)).OrderBy(g=>g.Distance).FirstOrDefault();
            if(nearest!=null) {
                if(EdgeGuideMode.SelectedIndex==2)edgeGuides[ActiveView].Remove(nearest.Guide);
                else if(EdgeGuideMode.SelectedIndex==4) {var list=edgeGuides[ActiveView];list[list.IndexOf(nearest.Guide)]=nearest.Guide with {CornerStyle=EdgeCornerStyle.SelectedIndex==1?"Rounded":"Pointed"};}
                else {var list=edgeGuides[ActiveView];list[list.IndexOf(nearest.Guide)]=nearest.Guide with {Mode=EdgeGuideBehavior.SelectedIndex==1?"Absolute":"Relative"};}
                RecordChange();RenderRig();Status.Text=EdgeGuideMode.SelectedIndex==2?"Removed edge mark.":"Changed edge mark mode.";
            }
            return true;
        }
        if(EdgeGuideMode.SelectedIndex is 5 or 6)
        {
            movingEdgeGuide=(edgeGuides.GetValueOrDefault(ActiveView) ?? []).Where(g=>g.Kind==(EdgeGuideMode.SelectedIndex==6?"Corner":"Straight") && GuideDistance(point,g)<=Math.Max(12,g.Radius)).OrderBy(g=>GuideDistance(point,g)).FirstOrDefault();
            if(movingEdgeGuide==null)return true;
            drawingEdgeGuide=true;edgeGuideStart=edgeGuideEnd=point;guidedCanvas=canvas;canvas.CaptureMouse();RenderRig();return true;
        }
        if(EdgeGuideMode.SelectedIndex==1) {AddEdgeGuide(new(Guid.NewGuid().ToString("N"),"Corner",point.X,point.Y,point.X,point.Y,EdgeGuideRadius.Value,EdgeGuideBehavior.SelectedIndex==1?"Absolute":"Relative",EdgeCornerStyle.SelectedIndex==1?"Rounded":"Pointed"));return true;}
        if((edgeGuides.GetValueOrDefault(ActiveView)?.Count ?? 0)>=128) {Status.Text="This face already has 128 edge marks.";return true;}
        drawingEdgeGuide=true;edgeGuideStart=edgeGuideEnd=SnapEdgeEndpoint(point);guidedCanvas=canvas;canvas.CaptureMouse();canvas.CaptureStylus();RenderRig();return true;
    }
    private void ContinueEdgeGuide(Point point) {edgeGuideEnd=SnapEdgeEndpoint(EdgePoint(point));RenderRig();}
    private void EndEdgeGuide(Point point)
    {
        edgeGuideEnd=SnapEdgeEndpoint(EdgePoint(point));var start=edgeGuideStart;var end=edgeGuideEnd;var moving=movingEdgeGuide;CancelGuidedStroke();
        if(moving!=null)
        {
            var delta=end-start;delta.X=Math.Clamp(delta.X,-Math.Min(moving.X1,moving.X2),canvasWidth-Math.Max(moving.X1,moving.X2));delta.Y=Math.Clamp(delta.Y,-Math.Min(moving.Y1,moving.Y2),canvasHeight-Math.Max(moving.Y1,moving.Y2));
            var changed=moving with {X1=moving.X1+delta.X,Y1=moving.Y1+delta.Y,X2=moving.X2+delta.X,Y2=moving.Y2+delta.Y};
            if(!SyncLockedLine(ActiveView,changed)){Status.Text="Move blocked: a locked partner would leave its canvas.";RenderRig();return;}
            RecordChange();RenderRig();Status.Text="Moved line and locked partners.";return;
        }
        if((end-start).Length<4) {RenderRig();Status.Text="Drag along the outline to mark a straight edge, or choose Sharp corner to tap a corner.";return;}
        AddEdgeGuide(new(Guid.NewGuid().ToString("N"),"Straight",start.X,start.Y,end.X,end.Y,EdgeGuideRadius.Value,EdgeGuideBehavior.SelectedIndex==1?"Absolute":"Relative"));
        if(ConnectEdgeLines.IsChecked==true)
        foreach(var joint in new[]{start,end})
        {
            var list=edgeGuides.GetValueOrDefault(ActiveView) ?? [];
            var connected=list.Count(g=>g.Kind=="Straight" && (new Point(g.X1,g.Y1)-joint).Length<.01 || g.Kind=="Straight" && (new Point(g.X2,g.Y2)-joint).Length<.01);
            if(connected>=2 && !list.Any(g=>g.Kind=="Corner" && (new Point(g.X1,g.Y1)-joint).Length<.01))
                AddEdgeGuide(new(Guid.NewGuid().ToString("N"),"Corner",joint.X,joint.Y,joint.X,joint.Y,EdgeGuideRadius.Value,EdgeGuideBehavior.SelectedIndex==1?"Absolute":"Relative",EdgeCornerStyle.SelectedIndex==1?"Rounded":"Pointed"));
        }
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
            var color=highlightedGuides.Contains((face,guide.Id))?Brushes.DeepPink:guide.CornerGroup!=null?Brushes.DodgerBlue:guide.Mode=="Absolute"?Brushes.LimeGreen:Brushes.DarkOrange;
            if(guide.Kind=="Straight")
            {
                canvas.Children.Add(new Line {X1=guide.X1,Y1=guide.Y1,X2=guide.X2,Y2=guide.Y2,Stroke=color,StrokeThickness=3,IsHitTestVisible=false});
                foreach(var point in new[]{new Point(guide.X1,guide.Y1),new Point(guide.X2,guide.Y2)})
                {var dot=new Ellipse {Width=8,Height=8,Fill=color,Stroke=Brushes.White,StrokeThickness=1,IsHitTestVisible=false};Canvas.SetLeft(dot,point.X-4);Canvas.SetTop(dot,point.Y-4);canvas.Children.Add(dot);}
            }
            else
            {
                Shape corner=guide.CornerStyle=="Rounded" ? new Ellipse {Width=12,Height=12,Stroke=color,StrokeThickness=3,Fill=Brushes.White,IsHitTestVisible=false} : new Rectangle {Width=12,Height=12,Stroke=color,StrokeThickness=3,Fill=Brushes.White,IsHitTestVisible=false};Canvas.SetLeft(corner,guide.X1-6);Canvas.SetTop(corner,guide.Y1-6);canvas.Children.Add(corner);
            }
        }
        if(drawingEdgeGuide && face==ActiveView) canvas.Children.Add(new Line {X1=edgeGuideStart.X,Y1=edgeGuideStart.Y,X2=edgeGuideEnd.X,Y2=edgeGuideEnd.Y,Stroke=EdgeGuideBehavior.SelectedIndex==1?Brushes.LimeGreen:Brushes.DarkOrange,StrokeThickness=3,StrokeDashArray=new DoubleCollection {3,2},IsHitTestVisible=false});
    }
    private Point SnapEdgeEndpoint(Point point)
    {
        if(ConnectEdgeLines?.IsChecked!=true)return point;
        var points=(edgeGuides.GetValueOrDefault(ActiveView) ?? []).Where(g=>g.Kind=="Straight").SelectMany(g=>new[]{new Point(g.X1,g.Y1),new Point(g.X2,g.Y2)});
        var nearest=points.OrderBy(p=>(p-point).Length).Take(1).ToList();
        return nearest.Count>0 && (nearest[0]-point).Length<=12 ? nearest[0] : point;
    }
    private void ApplyCornerStyle(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();if(!edgeGuides.TryGetValue(ActiveView,out var list))return;
        var style=EdgeCornerStyle.SelectedIndex==1?"Rounded":"Pointed";
        edgeGuides[ActiveView]=list.Select(g=>g.Kind=="Corner"?g with {CornerStyle=style}:g).ToList();RecordChange();RenderRig();Status.Text="Changed corners to "+style+".";
    }
    private void ApplyEdgeGuideBehavior(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();if(!edgeGuides.TryGetValue(ActiveView,out var list) || list.Count==0)return;
        var mode=EdgeGuideBehavior.SelectedIndex==1?"Absolute":"Relative";
        if(list.All(g=>g.Mode==mode))return;
        edgeGuides[ActiveView]=list.Select(g=>g with {Mode=mode}).ToList();RecordChange();RenderRig();Status.Text="Changed face marks to "+mode+".";
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
