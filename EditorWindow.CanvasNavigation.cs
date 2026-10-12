using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MeshDraw;
public partial class EditorWindow
{
    private string? navigatingCanvas;
    private Point canvasNavigationStart;
    private Matrix canvasNavigationMatrix;
    private double canvasNavigationFit;
    private bool rotatingCanvas;
    private void InitializeCanvasNavigation()
    {
        PreviewMouseWheel+=CanvasNavigationWheel;
        PreviewMouseLeftButtonDown+=CanvasNavigationDown;
        PreviewMouseMove+=CanvasNavigationMove;
        PreviewMouseLeftButtonUp+=(_,e)=>{if(navigatingCanvas!=null){StopCanvasNavigation();e.Handled=true;}};
        LostMouseCapture+=(_,_)=>{if(navigatingCanvas!=null && Mouse.Captured!=this)StopCanvasNavigation();};
        Deactivated+=(_,_)=>StopCanvasNavigation();
        PreviewKeyDown+=(_,e)=>
        {
            if(viewLayout=="3d" || Keyboard.FocusedElement is TextBox)return;
            if(e.Key==Key.Space || (e.Key==Key.R && Keyboard.IsKeyDown(Key.Space)))e.Handled=true;
            if(e.Key==Key.Escape && navigatingCanvas!=null){surfaces[navigatingCanvas].RenderTransform=new MatrixTransform(canvasNavigationMatrix);StopCanvasNavigation();e.Handled=true;}
        };
    }
    private (string face,Viewbox host)? CanvasNavigationTarget(Point windowPoint)
    {
        if(viewLayout=="3d" || GalleryScreen.Visibility==Visibility.Visible)return null;
        var hosts=viewLayout=="all"?multiViewHosts:Views.Items.Cast<TabItem>().Where(t=>t.IsSelected).ToDictionary(t=>(string)t.Tag,t=>(Viewbox)t.Content);
        foreach(var pair in hosts)
        {
            var host=pair.Value;if(!host.IsVisible || host.ActualWidth<=0 || host.ActualHeight<=0)continue;
            var p=TranslatePoint(windowPoint,host);
            if(new Rect(0,0,host.ActualWidth,host.ActualHeight).Contains(p))return(pair.Key,host);
        }
        return null;
    }
    private static double CanvasZoom(Matrix matrix)=>Math.Sqrt(matrix.M11*matrix.M11+matrix.M12*matrix.M12);
    private double CanvasFit(string face,Viewbox host)
    {
        var surface=surfaces[face];var transform=surface.TransformToAncestor(host);
        return Math.Max(.0001,(transform.Transform(new Point(1,0))-transform.Transform(new Point(0,0))).Length/CanvasZoom(surface.RenderTransform.Value));
    }
    private void CanvasNavigationWheel(object sender,MouseWheelEventArgs e)
    {
        if(navigatingCanvas!=null)return;
        if(CanvasNavigationTarget(e.GetPosition(this)) is not {} target)return;
        var surface=surfaces[target.face];var next=ZoomDrawingCanvas(target.face,e.GetPosition(surface),e.Delta);
        Status.Text=$"Canvas zoom: {next*100:0}% · Space + drag: move · Space + R + drag: rotate";e.Handled=true;
    }
    private double ZoomDrawingCanvas(string face,Point anchorPoint,int wheelDelta)
    {
        var surface=surfaces[face];var matrix=surface.RenderTransform.Value;var zoom=CanvasZoom(matrix);
        var next=Math.Clamp(zoom*Math.Pow(1.15,wheelDelta/120.0),.1,20);var anchor=matrix.Transform(anchorPoint);
        matrix.ScaleAt(next/zoom,next/zoom,anchor.X,anchor.Y);surface.RenderTransform=new MatrixTransform(matrix);return next;
    }
    private void CanvasNavigationDown(object sender,MouseButtonEventArgs e)
    {
        if(!Keyboard.IsKeyDown(Key.Space) || e.StylusDevice!=null)return;
        if(CanvasNavigationTarget(e.GetPosition(this)) is not {} target)return;
        CancelGuidedStroke();SelectMultiView(target.face);Focus();
        navigatingCanvas=target.face;canvasNavigationStart=e.GetPosition(this);canvasNavigationMatrix=surfaces[target.face].RenderTransform.Value;
        canvasNavigationFit=CanvasFit(target.face,target.host);rotatingCanvas=Keyboard.IsKeyDown(Key.R);
        Mouse.OverrideCursor=rotatingCanvas?Cursors.SizeAll:Cursors.Hand;CaptureMouse();e.Handled=true;
    }
    private void CanvasNavigationMove(object sender,MouseEventArgs e)
    {
        if(navigatingCanvas==null)return;
        if(e.LeftButton!=MouseButtonState.Pressed){StopCanvasNavigation();return;}
        var delta=e.GetPosition(this)-canvasNavigationStart;var matrix=canvasNavigationMatrix;
        if(rotatingCanvas)
        {
            var center=matrix.Transform(new Point(canvasWidth/2.0,canvasHeight/2.0));matrix.RotateAt(delta.X*.5,center.X,center.Y);
        }
        else matrix.Translate(delta.X/canvasNavigationFit,delta.Y/canvasNavigationFit);
        surfaces[navigatingCanvas].RenderTransform=new MatrixTransform(matrix);e.Handled=true;
    }
    private void StopCanvasNavigation()
    {
        if(navigatingCanvas==null)return;navigatingCanvas=null;Mouse.OverrideCursor=null;if(Mouse.Captured==this)ReleaseMouseCapture();
    }
    private void ResetCanvasView(object sender,RoutedEventArgs e)
    {
        StopCanvasNavigation();if(surfaces.TryGetValue(ActiveView,out var surface))surface.RenderTransform=Transform.Identity;
        Status.Text="Canvas view reset · Scroll: zoom · Space + drag: move · Space + R + drag: rotate";
    }
}
