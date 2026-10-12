using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeshDraw;
public partial class EditorWindow
{
    private readonly Dictionary<string,Point> rulerCenters=new();
    private readonly List<Point> guidedPoints=new();
    private readonly List<float> guidedPressures=new();
    private PaintLayer? guidedLayer;
    private DrawingAttributes? guidedAttributes;
    private Canvas? guidedCanvas;
    private double guidedSmoothing;
    private StrokeSmoother? strokeSmoother;
    private bool smoothAfter;
    private bool movingRuler;
    private int rulerDragHandle;
    private Vector rulerDragOffset;
    private Point rulerFixedEnd,strokeGuideCenter;
    private double strokeGuideRadius,strokeGuideAngle;
    private int strokeGuideMode;
    private readonly Dictionary<string,double> straightRulerSpans=new();
    private readonly Dictionary<string,double> circleHandleAngles=new();
    private void AttachBrushInput(Canvas canvas)
    {
        canvas.PreviewMouseLeftButtonDown+=(_,e)=> {if(e.StylusDevice==null && BeginGuidedStroke(canvas,e.GetPosition(canvas))) e.Handled=true;};
        canvas.PreviewMouseMove+=(_,e)=> {if(e.StylusDevice==null && guidedCanvas==canvas && e.LeftButton==MouseButtonState.Pressed) {ContinueGuidedStroke(e.GetPosition(canvas));e.Handled=true;}};
        canvas.PreviewMouseLeftButtonUp+=(_,e)=> {if(e.StylusDevice==null && guidedCanvas==canvas) {EndGuidedStroke(e.GetPosition(canvas));e.Handled=true;}};
        canvas.PreviewStylusDown+=(_,e)=> {if(BeginGuidedStroke(canvas,e.GetPosition(canvas),e.GetStylusPoints(canvas)[0].PressureFactor)) e.Handled=true;};
        canvas.PreviewStylusMove+=(_,e)=> {if(guidedCanvas==canvas) {foreach(var point in e.GetStylusPoints(canvas)) ContinueGuidedStroke(new Point(point.X,point.Y),point.PressureFactor);e.Handled=true;}};
        canvas.PreviewStylusUp+=(_,e)=> {if(guidedCanvas==canvas) {EndGuidedStroke(e.GetPosition(canvas),e.GetStylusPoints(canvas)[0].PressureFactor);e.Handled=true;}};
        canvas.LostMouseCapture+=(_,_)=> {if(guidedCanvas==canvas) CancelGuidedStroke();};
        canvas.LostStylusCapture+=(_,_)=> {if(guidedCanvas==canvas) CancelGuidedStroke();};
    }
    private Point RulerCenter(string view) => rulerCenters.TryGetValue(view,out var center) ? new Point(center.X*canvasWidth,center.Y*canvasHeight) : new Point(canvasWidth/2d,canvasHeight/2d);
    internal static Point ConstrainRulerPoint(Point point,Point center,int mode,double angle,double radius)
    {
        if(mode==1) {var radians=angle*Math.PI/180;var axis=new Vector(Math.Cos(radians),Math.Sin(radians));return center+axis*Vector.Multiply(point-center,axis);}
        if(mode==2) {var direction=point-center;if(direction.Length<.001) direction=new Vector(1,0);direction.Normalize();return center+direction*radius;}
        return point;
    }
    private double RulerDisplayScale(Canvas canvas)
    {
        try {var transform=canvas.TransformToAncestor(EditorScreen);double scale=(transform.Transform(new Point(1,0))-transform.Transform(new Point(0,0))).Length;return Math.Max(.05,scale);} catch {return 1;}
    }
    private (Point a,Point b) StraightRulerEnds(string view)
    {
        var center=RulerCenter(view);double radians=RulerAngle.Value*Math.PI/180;
        double length=(straightRulerSpans.GetValueOrDefault(view,.35))*Math.Min(canvasWidth,canvasHeight);
        var axis=new Vector(Math.Cos(radians),Math.Sin(radians))*length;return (center-axis,center+axis);
    }
    private Point CircleRulerHandle(string view)
    {
        var center=RulerCenter(view);double angle=circleHandleAngles.GetValueOrDefault(view,0);
        return center+new Vector(Math.Cos(angle),Math.Sin(angle))*(RulerRadius.Value*Math.Min(canvasWidth,canvasHeight));
    }
    private bool BeginRulerHandleDrag(Canvas canvas,Point point)
    {
        if(RulerMode.SelectedIndex<=0 || Tool.SelectedIndex is not (0 or 1)) return false;
        double hit=12/RulerDisplayScale(canvas);var center=RulerCenter(ActiveView);int handle=0;
        if((point-center).Length<=hit) handle=1;
        else if(RulerMode.SelectedIndex==1)
        {var ends=StraightRulerEnds(ActiveView);if((point-ends.a).Length<=hit) {handle=2;rulerFixedEnd=ends.b;} else if((point-ends.b).Length<=hit) {handle=3;rulerFixedEnd=ends.a;}}
        else if((point-CircleRulerHandle(ActiveView)).Length<=hit) handle=4;
        if(handle==0 && PositionRuler.IsChecked!=true) return false;
        movingRuler=true;rulerDragHandle=handle==0 ? 1 : handle;rulerDragOffset=handle==1 ? point-center : new Vector();guidedCanvas=canvas;
        canvas.CaptureMouse();canvas.CaptureStylus();return true;
    }
    private bool BeginGuidedStroke(Canvas canvas,Point point,float pressure=.5f)
    {
        if(Tool.SelectedIndex==9) return BeginEdgeGuide(canvas,point);
        if(Tool.SelectedIndex==8) return BeginSelectionGesture(canvas,point);
        if(SelectionEditing)
        {
            if(Tool.SelectedIndex is 0 or 1) {if(BeginRulerHandleDrag(canvas,point)) return true;return BeginSelectionBrush(canvas,point,pressure);}
            Status.Text="Use Brush, Eraser, or Select to edit the selection layer.";return false;
        }
        if(Tool.SelectedIndex==7) return BeginLayerMove(canvas,point);
        if(BeginRulerHandleDrag(canvas,point)) return true;
        if(Tool.SelectedIndex>=4 || (Tool.SelectedIndex==1 && (ActiveLayer?.Photo!=null || selections.ContainsKey(ActiveView) || selectedFaces.Any(f=>f!=ActiveView)))) return BeginRasterGesture(canvas,point);
        if(Tool.SelectedIndex!=0) return false;
        if(ActiveLayer is not {Locked:false,Visible:true,Opacity:>0} layer) return false;
        strokeGuideMode=RulerMode.SelectedIndex;strokeGuideAngle=RulerAngle.Value;
        strokeGuideCenter=strokeGuideMode==1 ? point : RulerCenter(ActiveView);
        strokeGuideRadius=(point-strokeGuideCenter).Length;
        guidedLayer=layer;guidedCanvas=canvas;guidedAttributes=layer.Ink.DefaultDrawingAttributes.Clone();guidedAttributes.IgnorePressure=false;
        guidedSmoothing=StrokeStabilizer.Value;smoothAfter=StabilizerMethod.SelectedIndex==1;strokeSmoother=new StrokeSmoother(point,pressure,guidedSmoothing/RulerDisplayScale(canvas));guidedPoints.Clear();guidedPressures.Clear();guidedPoints.Add(point);guidedPressures.Add(pressure);
        canvas.CaptureMouse();canvas.CaptureStylus();RenderRig();return true;
    }
    private Point GuidePoint(Point point) => ConstrainRulerPoint(point,strokeGuideCenter,strokeGuideMode,strokeGuideAngle,strokeGuideRadius);
    private void DragRulerHandle(Point point)
    {
        if(rulerDragHandle==1)
        {
            var center=point-new Vector(rulerDragOffset.X,rulerDragOffset.Y);
            rulerCenters[ActiveView]=new Point(Math.Clamp(center.X/canvasWidth,0,1),Math.Clamp(center.Y/canvasHeight,0,1));
        }
        else if(rulerDragHandle is 2 or 3)
        {
            var moved=new Point(Math.Clamp(point.X,0,canvasWidth),Math.Clamp(point.Y,0,canvasHeight));
            Point a=rulerDragHandle==2 ? moved : rulerFixedEnd,b=rulerDragHandle==2 ? rulerFixedEnd : moved;
            var direction=b-a;if(direction.Length<10) return;
            var center=new Point((a.X+b.X)/2,(a.Y+b.Y)/2);rulerCenters[ActiveView]=new Point(center.X/canvasWidth,center.Y/canvasHeight);
            straightRulerSpans[ActiveView]=direction.Length/(2*Math.Min(canvasWidth,canvasHeight));
            RulerAngle.Value=Math.Atan2(direction.Y,direction.X)*180/Math.PI;
        }
        else if(rulerDragHandle==4)
        {
            var direction=point-RulerCenter(ActiveView);if(direction.Length<1) return;
            circleHandleAngles[ActiveView]=Math.Atan2(direction.Y,direction.X);
            RulerRadius.Value=Math.Clamp(direction.Length/Math.Min(canvasWidth,canvasHeight),RulerRadius.Minimum,RulerRadius.Maximum);
        }
        UpdateRulerNumberInputs();RenderRig();
    }
    private void ContinueGuidedStroke(Point point,float pressure=.5f)
    {
        if(drawingEdgeGuide) {ContinueEdgeGuide(point);return;}
        if(selectingArea) {ContinueSelectionGesture(point);return;}
        if(movingLayer!=null) {ContinueLayerMove(point);return;}
        if(rasterPixels!=null) {ContinueRasterGesture(point);return;}
        if(movingRuler) {DragRulerHandle(point);return;}
        if(guidedLayer==null || guidedPoints.Count==0) return;
        if(smoothAfter || guidedSmoothing<=0) AppendGuidedPoint(point,pressure);
        else foreach(var sample in strokeSmoother!.Move(point,pressure)) AppendGuidedPoint(sample.point,sample.pressure);
        RenderRig();
    }
    private void AppendGuidedPoint(Point point,float pressure,bool endpoint=false)
    {
        point=GuidePoint(point);
        if((point-guidedPoints[^1]).Length>=.15) {guidedPoints.Add(point);guidedPressures.Add(pressure);}
        else if(endpoint) {guidedPoints[^1]=point;guidedPressures[^1]=pressure;}
    }
    private void EndGuidedStroke(Point point,float pressure=.5f)
    {
        if(guidedCanvas==null) return;
        if(drawingEdgeGuide) {EndEdgeGuide(point);return;}
        if(selectingArea) {EndSelectionGesture(point);return;}
        if(movingLayer!=null) {EndLayerMove(point);return;}
        if(rasterPixels!=null) {EndRasterGesture(point);return;}
        ContinueGuidedStroke(point,pressure);var layer=guidedLayer;var attributes=guidedAttributes;
        if(!movingRuler && layer!=null && attributes!=null)
        {
            if(smoothAfter && guidedSmoothing>0)
            {
                var result=StrokeSmoother.SmoothAfter(guidedPoints,guidedPressures,guidedSmoothing/RulerDisplayScale(guidedCanvas));
                guidedPoints.Clear();guidedPressures.Clear();foreach(var sample in result) {guidedPoints.Add(GuidePoint(sample.point));guidedPressures.Add(sample.pressure);}
            }
            else if(guidedSmoothing>0) foreach(var sample in strokeSmoother!.Finish(point,pressure)) AppendGuidedPoint(sample.point,sample.pressure);
            AppendGuidedPoint(point,pressure,true);
            var stroke=CreateGuidedStroke(guidedPoints,attributes,StrokeFade.IsChecked==true,FadeAmount.Value,guidedPressures);
            bool maskStroke=selectionBrushStroke,eraseMask=selectionBrushErase;
            CancelGuidedStroke();if(maskStroke) CommitSelectionBrush(stroke,eraseMask);else CommitFaceStroke(layer,stroke);
        }
        else CancelGuidedStroke();
        RenderRig();
    }
    private void CancelGuidedStroke()
    {
        drawingEdgeGuide=false;movingEdgeGuide=null;
        selectionBrushStroke=false;selectionBrushErase=false;
        CancelSelectionGesture();
        CancelLayerMove();
        CancelRasterGesture();
        var canvas=guidedCanvas;guidedCanvas=null;strokeSmoother=null;guidedLayer=null;guidedAttributes=null;movingRuler=false;rulerDragHandle=0;guidedPoints.Clear();guidedPressures.Clear();
        canvas?.ReleaseMouseCapture();canvas?.ReleaseStylusCapture();
    }
    internal static Stroke CreateGuidedStroke(IReadOnlyList<Point> points,DrawingAttributes attributes,bool fade,double fadeAmount,IReadOnlyList<float>? pressures=null)
    {
        var positions=points.Count==0 ? new[]{new Point(0,0)} : points.ToArray();var distance=new double[positions.Length];
        for(int i=1;i<positions.Length;i++) distance[i]=distance[i-1]+(positions[i]-positions[i-1]).Length;
        var samples=new StylusPointCollection();double total=distance[^1],length=Math.Max(1,total*fadeAmount);
        for(int i=0;i<positions.Length;i++)
        {
            float pressure=pressures!=null && i<pressures.Count ? pressures[i] : .5f;
            if(fade && total>0) pressure*=(float)(.1+.9*Math.Clamp(Math.Min(distance[i],total-distance[i])/length,0,1));
            pressure=Math.Clamp(pressure,0,1);
            samples.Add(new StylusPoint(positions[i].X,positions[i].Y,pressure));
        }
        var drawing=attributes.Clone();drawing.IgnorePressure=false;return new Stroke(samples,drawing);
    }
    private void RenderBrushGuides(Canvas canvas,string view)
    {
        if(view==ActiveView && RulerMode?.SelectedIndex>0 && Tool.SelectedIndex<2)
        {
            var center=RulerCenter(view);var brush=new SolidColorBrush(Color.FromArgb(210,55,150,230));double scale=RulerDisplayScale(canvas);
            void Handle(Point point,string tooltip)
            {
                double size=18/scale;var handle=new Ellipse {Width=size,Height=size,Fill=brush,Stroke=Brushes.White,StrokeThickness=2/scale,IsHitTestVisible=true,Cursor=Cursors.SizeAll,ToolTip=tooltip};
                Canvas.SetLeft(handle,point.X-size/2);Canvas.SetTop(handle,point.Y-size/2);canvas.Children.Add(handle);
            }
            if(RulerMode.SelectedIndex==1)
            {
                double angle=RulerAngle.Value*Math.PI/180,length=Math.Max(canvasWidth,canvasHeight)*2;var axis=new Vector(Math.Cos(angle),Math.Sin(angle))*length;var a=center-axis;var b=center+axis;
                canvas.Children.Add(new Line {X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=brush,StrokeThickness=2/scale,StrokeDashArray=new DoubleCollection {8,4},IsHitTestVisible=false});
                var ends=StraightRulerEnds(view);Handle(ends.a,"Drag to rotate and resize ruler");Handle(ends.b,"Drag to rotate and resize ruler");
            }
            else
            {
                double radius=RulerRadius.Value*Math.Min(canvasWidth,canvasHeight);var ring=new Ellipse {Width=radius*2,Height=radius*2,Stroke=brush,StrokeThickness=2/scale,StrokeDashArray=new DoubleCollection {8,4},IsHitTestVisible=false};
                Canvas.SetLeft(ring,center.X-radius);Canvas.SetTop(ring,center.Y-radius);canvas.Children.Add(ring);Handle(CircleRulerHandle(view),"Drag to change circle radius");
            }
            Handle(center,"Drag to move ruler");
        }
        RenderRasterGesture(canvas);
        if(guidedCanvas==canvas && guidedLayer!=null && guidedAttributes!=null && guidedPoints.Count>0)
        {
            var stroke=CreateGuidedStroke(guidedPoints,guidedAttributes,StrokeFade.IsChecked==true,FadeAmount.Value,guidedPressures);
            canvas.Children.Add(new System.Windows.Shapes.Path {Data=stroke.GetGeometry(),Clip=selectionBrushStroke ? null : SelectionFor(view),Fill=new SolidColorBrush(guidedAttributes.Color),Opacity=selectionBrushStroke ? .5 : guidedLayer.Opacity,IsHitTestVisible=false});
        }
    }
    private void DrawingAidChanged(object sender,RoutedEventArgs e) {if(drawingViews.Count>=6) {ApplyTools();RenderRig();}}
    private void DrawingAidValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e) {UpdateRulerNumberInputs();DrawingAidChanged(sender,e);}
    private void UpdateRulerNumberInputs()
    {
        if(RulerAngleInput==null || RulerRadiusInput==null || RulerAngle==null || RulerRadius==null) return;
        RulerRadius.Maximum=Math.Sqrt((double)canvasWidth*canvasWidth+(double)canvasHeight*canvasHeight)/Math.Min(canvasWidth,canvasHeight);
        RulerAngleInput.Text=RulerAngle.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
        RulerRadiusInput.Text=(RulerRadius.Value*Math.Min(canvasWidth,canvasHeight)).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
    }
    private void ResetRulerPosition(object sender,RoutedEventArgs e) {CancelGuidedStroke();rulerCenters.Remove(ActiveView);straightRulerSpans.Remove(ActiveView);circleHandleAngles.Remove(ActiveView);RenderRig();Status.Text="Ruler centered in "+ActiveView+".";}
    private void RulerNumberCommitted(object sender,RoutedEventArgs e)
    {
        var box=(TextBox)sender;
        if(double.TryParse(box.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number) && double.IsFinite(number))
        {
            if(ReferenceEquals(box,RulerAngleInput)) RulerAngle.Value=Math.Clamp(number,-180,180);
            else RulerRadius.Value=Math.Clamp(number/Math.Min(canvasWidth,canvasHeight),RulerRadius.Minimum,RulerRadius.Maximum);
        }
        else Status.Text="Enter a numeric angle or radius.";
        UpdateRulerNumberInputs();RenderRig();
    }
    private void RulerNumberKeyDown(object sender,KeyEventArgs e) {if(e.Key==Key.Enter) {RulerNumberCommitted(sender,e);e.Handled=true;}}

}
