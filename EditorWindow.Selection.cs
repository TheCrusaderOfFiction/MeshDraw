using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Ink;
namespace MeshDraw;
public partial class EditorWindow
{
    private const string SelectionLayerId="__selection__";
    private readonly Dictionary<string,string> lastPaintLayers=new();
    private readonly Dictionary<string,SelectionLayerRow> selectionRows=new();
    private bool SelectionEditing=>activeLayers.GetValueOrDefault(ActiveView)==SelectionLayerId;
    private bool selectionBrushStroke,selectionBrushErase;
    private sealed class SelectionLayerRow(Func<BitmapSource> preview)
    {
        public string Name=>"Selection layer";
        public string Description=>"Brush / eraser / shape mask";
        public BitmapSource Thumbnail=>preview();
    }
    private SelectionLayerRow SelectionRow(string face)
    {
        if(!selectionRows.TryGetValue(face,out var row)) selectionRows[face]=row=new(()=>SelectionThumbnail(face));
        return row;
    }
    private BitmapSource SelectionThumbnail(string face)
    {
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,64,64));
            context.PushTransform(new ScaleTransform(64d/canvasWidth,64d/canvasHeight));
            if(SelectionFor(face) is { } geometry) context.DrawGeometry(Brushes.DodgerBlue,null,geometry);
        }
        var bitmap=new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    private void SelectSelectionLayer(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();if(!SelectionEditing && ActiveLayer!=null) lastPaintLayers[ActiveView]=ActiveLayer.Id;
        activeLayers[ActiveView]=SelectionLayerId;Tool.SelectedIndex=0;RefreshEditor();
        Status.Text="Selection layer selected. Brush adds blue areas; Eraser removes them. Select uses shapes.";
    }
    private void SelectionDrawMode(object sender,RoutedEventArgs e)
    {
        SelectSelectionLayer(sender,e);Tool.SelectedIndex=int.Parse(((Button)sender).Tag.ToString()!);ApplyTools();RenderRig();
    }
    private bool BeginSelectionBrush(Canvas canvas,Point point,float pressure)
    {
        CancelGuidedStroke();selectionBrushStroke=true;selectionBrushErase=Tool.SelectedIndex==1;
        guidedLayer=ActiveLayer;guidedCanvas=canvas;guidedAttributes=new DrawingAttributes {Color=Colors.DodgerBlue,Width=BrushSize.Value,Height=BrushSize.Value,FitToCurve=true,IgnorePressure=false};
        strokeGuideMode=RulerMode.SelectedIndex;strokeGuideAngle=RulerAngle.Value;strokeGuideCenter=strokeGuideMode==1 ? point : RulerCenter(ActiveView);strokeGuideRadius=(point-strokeGuideCenter).Length;
        guidedSmoothing=StrokeStabilizer.Value;smoothAfter=StabilizerMethod.SelectedIndex==1;strokeSmoother=new StrokeSmoother(point,pressure,guidedSmoothing/RulerDisplayScale(canvas));
        guidedPoints.Clear();guidedPressures.Clear();guidedPoints.Add(point);guidedPressures.Add(pressure);
        canvas.CaptureMouse();canvas.CaptureStylus();RenderRig();return true;
    }
    private void CommitSelectionBrush(Stroke stroke,bool erase)
    {
        var current=SelectionFor(ActiveView);
        if(erase && current==null) {RenderRig();return;}
        SetSelection(Geometry.Combine(current ?? Geometry.Empty,stroke.GetGeometry(),erase ? GeometryCombineMode.Exclude : GeometryCombineMode.Union,null));
    }
    private void UseLayerAsMask(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();var source=ActiveLayer;if(source==null) return;
        try
        {
            var geometry=TraceSelectionMask(LayerPixels(new[]{source},true),canvasWidth,canvasHeight);
            SelectSelectionLayer(sender,e);if(SetSelection(geometry)) Status.Text="Selection set from "+source.Name+". Brush adds areas; Eraser removes them.";
        }
        catch(InvalidDataException error) {Status.Text=error.Message;}
    }
    internal static Geometry TraceSelectionMask(byte[] pixels,int width,int height)
    {
        // Follow the boundary of nontransparent pixels, retaining holes with even-odd fill.
        var mask=new bool[width*height];for(int i=0;i<mask.Length;i++) mask[i]=pixels[i*4+3]>0;
        bool Filled(int x,int y)=>x>=0 && y>=0 && x<width && y<height && mask[y*width+x];
        int stride=width+1,edgeCount=0;var edges=new Dictionary<int,List<int>>();
        void Edge(int x,int y,int xx,int yy)
        {
            if(++edgeCount>100000) throw new InvalidDataException("This layer mask is too detailed. Use simpler painted areas or select with Brush and shapes.");
            int from=y*stride+x,to=yy*stride+xx;if(!edges.TryGetValue(from,out var list)) edges[from]=list=[];list.Add(to);
        }
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            if(!Filled(x,y)) continue;
            if(!Filled(x,y-1)) Edge(x,y,x+1,y);
            if(!Filled(x+1,y)) Edge(x+1,y,x+1,y+1);
            if(!Filled(x,y+1)) Edge(x+1,y+1,x,y+1);
            if(!Filled(x-1,y)) Edge(x,y+1,x,y);
        }
        var geometry=new StreamGeometry {FillRule=FillRule.EvenOdd};using(var context=geometry.Open())
        while(edges.Count>0)
        {
            int start=edges.First().Key,current=start,previousDirection=0;var points=new List<Point>();
            do
            {
                var outgoing=edges[current];int next=outgoing[^1];outgoing.RemoveAt(outgoing.Count-1);if(outgoing.Count==0) edges.Remove(current);
                int direction=next-current;if(previousDirection!=0 && direction!=previousDirection) points.Add(new Point(current%stride,current/stride));
                previousDirection=direction;current=next;
            } while(current!=start);
            context.BeginFigure(new Point(start%stride,start/stride),true,true);context.PolyLineTo(points,true,false);
        }
        geometry.Freeze();return geometry;
    }
    private readonly Dictionary<string,Geometry> selections=new();
    private readonly Dictionary<string,(Geometry geometry,int width,int height,bool[] mask)> selectionMasks=new();
    private readonly List<Point> selectionPoints=[];
    private bool selectingArea;
    private int selectionGestureShape,selectionGestureOperation;
    private Geometry? selectionPreview;
    private Geometry? SelectionFor(string face)=>selections.GetValueOrDefault(face);
    private Point ClampSelectionPoint(Point p)=>new(Math.Clamp(p.X,0,canvasWidth),Math.Clamp(p.Y,0,canvasHeight));
    private bool BeginSelectionGesture(Canvas canvas,Point point)
    {
        CancelGuidedStroke();selectingArea=true;guidedCanvas=canvas;
        selectionGestureShape=SelectionShape.SelectedIndex;selectionGestureOperation=SelectionOperation.SelectedIndex;
        selectionPoints.Clear();selectionPoints.Add(ClampSelectionPoint(point));selectionPreview=null;
        canvas.CaptureMouse();canvas.CaptureStylus();RenderRig();return true;
    }
    private void ContinueSelectionGesture(Point point)
    {
        point=ClampSelectionPoint(point);
        if(selectionGestureShape==2)
        {
            if((selectionPoints[^1]-point).Length>=1 && selectionPoints.Count<2048) selectionPoints.Add(point);
            var path=new StreamGeometry {FillRule=FillRule.EvenOdd};
            if(selectionPoints.Count>=3) using(var context=path.Open()) {context.BeginFigure(selectionPoints[0],true,true);context.PolyLineTo(selectionPoints.Skip(1).ToList(),true,false);}
            selectionPreview=path;
        }
        else
        {
            var rect=new Rect(selectionPoints[0],point);
            selectionPreview=selectionGestureShape==1 ? new EllipseGeometry(rect) : new RectangleGeometry(rect);
        }
        RenderRig();
    }
    private void EndSelectionGesture(Point point)
    {
        ContinueSelectionGesture(point);var shape=selectionPreview;var operation=selectionGestureOperation;
        CancelGuidedStroke();
        if(shape==null || shape.GetArea()<1) {RenderRig();return;}
        Geometry result=shape;
        if(operation==1 && SelectionFor(ActiveView) is { } current) result=Geometry.Combine(current,shape,GeometryCombineMode.Union,null);
        if(operation==2) result=Geometry.Combine(SelectionFor(ActiveView) ?? new RectangleGeometry(new Rect(0,0,canvasWidth,canvasHeight)),shape,GeometryCombineMode.Exclude,null);
        if(SetSelection(result)) Status.Text="Selection layer updated. Paint stays inside the highlighted area.";
    }
    private void CancelSelectionGesture() {selectingArea=false;selectionPreview=null;selectionPoints.Clear();}
    private bool SetSelection(Geometry geometry)
    {
        // Flatten combines before saving so no live control transforms enter the project.
        var bounded=Geometry.Combine(geometry,new RectangleGeometry(new Rect(0,0,canvasWidth,canvasHeight)),GeometryCombineMode.Intersect,null);
        var path=bounded.GetFlattenedPathGeometry(.25,ToleranceType.Absolute);
        if(path.ToString(CultureInfo.InvariantCulture).Length>262144) {Status.Text="This selection is too complex. Use simpler brush strokes or shapes.";return false;}
        var frozen=Geometry.Parse(path.ToString(CultureInfo.InvariantCulture));frozen.Freeze();selections[ActiveView]=frozen;selectionMasks.Remove(ActiveView);RecordChange();ApplyTools();RenderRig();UpdateSelectionStatus();Layers.Items.Refresh();
        Status.Text=path.GetArea()<.01 ? "The selection is empty. Painting is blocked until you add an area or clear the selection." : "Selection active. Painting stays inside the highlighted area.";
        return true;
    }
    private void ClearSelection(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();if(selections.Remove(ActiveView)) {selectionMasks.Remove(ActiveView);RecordChange();}
        ApplyTools();RenderRig();UpdateSelectionStatus();Layers.Items.Refresh();Status.Text="Selection cleared on this face. Paint anywhere on the canvas.";
    }
    private void SelectEntireCanvas(object sender,RoutedEventArgs e) {CancelGuidedStroke();SetSelection(new RectangleGeometry(new Rect(0,0,canvasWidth,canvasHeight)));}
    private void InvertSelection(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();var entire=new RectangleGeometry(new Rect(0,0,canvasWidth,canvasHeight));
        SetSelection(SelectionFor(ActiveView) is { } current ? Geometry.Combine(entire,current,GeometryCombineMode.Exclude,null) : entire);
    }
    private void UpdateSelectionStatus()
    {
        if(SelectionStatus!=null) SelectionStatus.Text=SelectionFor(ActiveView) is { } geometry
            ? geometry.GetArea()<.01 ? "Selection layer · empty (painting blocked)" : "Selection layer · active (paint inside blue area)"
            : "Selection layer · none";
    }
    private void RenderSelection(Canvas canvas,string face)
    {
        void Draw(Geometry geometry,bool preview)
        {
            canvas.Children.Add(new System.Windows.Shapes.Path {Data=geometry,Fill=new SolidColorBrush(Color.FromArgb(preview ? (byte)65 : (byte)40,65,125,255)),
                Stroke=Brushes.DodgerBlue,StrokeThickness=1.5/RulerDisplayScale(canvas),StrokeDashArray=new DoubleCollection {5,3},IsHitTestVisible=false});
        }
        if(SelectionFor(face) is { } selection) Draw(selection,false);
        if(selectingArea && face==ActiveView && selectionPreview!=null) Draw(selectionPreview,true);
    }
    private bool[]? SelectionMask(string face)
    {
        var geometry=SelectionFor(face);if(geometry==null) return null;
        if(selectionMasks.TryGetValue(face,out var cached) && ReferenceEquals(cached.geometry,geometry) && cached.width==canvasWidth && cached.height==canvasHeight) return cached.mask;
        var visual=new DrawingVisual();RenderOptions.SetEdgeMode(visual,EdgeMode.Aliased);
        using(var context=visual.RenderOpen()) context.DrawGeometry(Brushes.White,null,geometry);
        var bitmap=new RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var pixels=new byte[canvasWidth*canvasHeight*4];bitmap.CopyPixels(pixels,canvasWidth*4,0);var mask=new bool[canvasWidth*canvasHeight];
        for(int i=0;i<mask.Length;i++) mask[i]=pixels[i*4+3]>=128;
        selectionMasks[face]=(geometry,canvasWidth,canvasHeight,mask);return mask;
    }
    private void CommitSelectedStroke(PaintLayer layer,Stroke stroke,Geometry selection)
    {
        if(Geometry.Combine(stroke.GetGeometry(),selection,GeometryCombineMode.Intersect,null).GetArea()<.001) return;
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())
        {
            layer.Draw(context,canvasWidth,canvasHeight);context.PushClip(selection);stroke.Draw(context);context.Pop();
        }
        var bitmap=new RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var pixels=new byte[canvasWidth*canvasHeight*4];bitmap.CopyPixels(pixels,canvasWidth*4,0);
        layer.ImageData=EncodeRaster(pixels,canvasWidth,canvasHeight);layer.Ink.Strokes.Clear();
    }
}
