using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshDraw;
public partial class EditorWindow
{
    private readonly List<(Point point,Point previous)> rasterDabs=new();
    private byte[]? rasterPixels;
    private PaintLayer? rasterLayer;
    private WriteableBitmap? rasterPreview;
    private Image? rasterDisplay;
    private Point rasterPrevious;
    private int rasterMode;
    private bool rasterChanged;
    private double rasterRadius,rasterAmount;
    private byte[] LayerPixels(IEnumerable<PaintLayer> layers,bool opacity=false)
    {
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())
        foreach(var layer in layers.Where(l=>l.Visible).Reverse())
        {context.PushOpacity(opacity ? layer.Opacity : 1);layer.Draw(context,canvasWidth,canvasHeight);context.Pop();}
        var bitmap=new RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var pixels=new byte[canvasWidth*canvasHeight*4];bitmap.CopyPixels(pixels,canvasWidth*4,0);return pixels;
    }
    private static string EncodeRaster(byte[] pixels,int width,int height)
    {
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=new MemoryStream();encoder.Save(stream);return Convert.ToBase64String(stream.ToArray());
    }
    private void CommitRaster(PaintLayer layer,byte[] pixels)
    {
        try
        {
            var encoded=EncodeRaster(pixels,canvasWidth,canvasHeight);_ = DrawingDocument.DecodePhoto(encoded);
            syncing=true;try {layer.ImageData=encoded;layer.Ink.Strokes.Clear();} finally {syncing=false;}
            RecordChange();RefreshEditor();
        }
        catch(Exception error) {ApplyTools();RenderRig();ShowError(error);}
    }
    private bool BeginRasterGesture(Canvas canvas,Point point)
    {
        if(ActiveLayer is not {Locked:false,Visible:true,Opacity:>0} layer) return false;
        var pixels=LayerPixels(new[]{layer});
        if(Tool.SelectedIndex==6)
        {
            syncing=true;
            try {foreach(var target in FaceTargets(layer))
            {
                var data=LayerPixels(new[]{target.layer});
                var reference=BucketReference.SelectedIndex==0 ? LayerPixels(drawingViews[target.face].Where(l=>l.Visible && l.Opacity>0 && (l.Kind==LayerKind.Artwork || l.Id==target.layer.Id)),true) : (byte[])data.Clone();
                var seed=FacePoint(point,ActiveView,target.face);
                if(FloodFillPixels(data,reference,canvasWidth,canvasHeight,(int)seed.X,(int)seed.Y,layer.Ink.DefaultDrawingAttributes.Color,(int)(BucketTolerance.Value*2.55),SelectionMask(target.face))>0) {target.layer.ImageData=EncodeRaster(data,canvasWidth,canvasHeight);target.layer.Ink.Strokes.Clear();}
            }} finally {syncing=false;}
            RecordChange();RefreshEditor();
            Status.Text="Filled connected region on "+layer.Name+".";return true;
        }
        rasterDabs.Clear();rasterPixels=pixels;rasterLayer=layer;rasterPrevious=point;rasterMode=Tool.SelectedIndex==1 ? 7 : Tool.SelectedIndex;
        rasterRadius=BrushSize.Value/2;rasterAmount=RasterStrength.Value*BrushOpacity.Value;rasterChanged=false;
        rasterPreview=new WriteableBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32,null);rasterPreview.WritePixels(new Int32Rect(0,0,canvasWidth,canvasHeight),pixels,canvasWidth*4,0);
        guidedCanvas=canvas;layer.Ink.Visibility=Visibility.Hidden;
        rasterDisplay=new Image {Source=rasterPreview,Width=canvasWidth,Height=canvasHeight,Opacity=layer.Opacity,IsHitTestVisible=false};
        var surface=surfaces[ActiveView];surface.Children.Insert(Math.Max(0,surface.Children.IndexOf(layer.Ink)),rasterDisplay);
        canvas.CaptureMouse();canvas.CaptureStylus();
        if(rasterMode!=5) ContinueRasterGesture(point);RenderRig();return true;
    }
    private void ContinueRasterGesture(Point point)
    {
        if(rasterPixels==null || rasterPreview==null) return;
        double distance=(point-rasterPrevious).Length;int steps=Math.Clamp((int)Math.Ceiling(distance/Math.Max(1,rasterRadius/3)),1,200);var start=rasterPrevious;
        for(int step=1;step<=steps;step++)
        {
            var next=start+(point-start)*(step/(double)steps);
            rasterDabs.Add((next,rasterPrevious));
            rasterChanged|=ApplyRasterDab(rasterPixels,canvasWidth,canvasHeight,next,rasterPrevious,rasterRadius,rasterAmount,rasterMode,SelectionMask(ActiveView));
            rasterPrevious=next;
        }
        int x0=Math.Max(0,(int)Math.Floor(Math.Min(start.X,point.X)-rasterRadius)),y0=Math.Max(0,(int)Math.Floor(Math.Min(start.Y,point.Y)-rasterRadius));
        int x1=Math.Min(canvasWidth-1,(int)Math.Ceiling(Math.Max(start.X,point.X)+rasterRadius)),y1=Math.Min(canvasHeight-1,(int)Math.Ceiling(Math.Max(start.Y,point.Y)+rasterRadius));
        if(x1>=x0 && y1>=y0)
        {
            int width=x1-x0+1,height=y1-y0+1;var patch=new byte[width*height*4];
            for(int row=0;row<height;row++) Buffer.BlockCopy(rasterPixels,((y0+row)*canvasWidth+x0)*4,patch,row*width*4,width*4);
            rasterPreview.WritePixels(new Int32Rect(x0,y0,width,height),patch,width*4,0);
        }
    }
    private void EndRasterGesture(Point point)
    {
        ContinueRasterGesture(point);var pixels=rasterPixels;var layer=rasterLayer;var changed=rasterChanged;
        var dabs=rasterDabs.ToArray();var mode=rasterMode;var radius=rasterRadius;var amount=rasterAmount;
        CancelGuidedStroke();
        if(layer!=null)
        {
            syncing=true;try {foreach(var target in FaceTargets(layer))
            {
                var data=target.layer==layer ? pixels! : LayerPixels(new[]{target.layer});bool edited=target.layer==layer && changed;
                if(target.layer!=layer) foreach(var dab in dabs) edited|=ApplyRasterDab(data,canvasWidth,canvasHeight,FacePoint(dab.point,ActiveView,target.face),FacePoint(dab.previous,ActiveView,target.face),radius,amount,mode,SelectionMask(target.face));
                if(edited) {target.layer.ImageData=EncodeRaster(data,canvasWidth,canvasHeight);target.layer.Ink.Strokes.Clear();}
            }} finally {syncing=false;}
            RecordChange();RefreshEditor();
        }
        else {ApplyTools();RenderRig();}
    }
    private void CancelRasterGesture()
    {
        var layer=rasterLayer;if(rasterDisplay?.Parent is Panel parent) parent.Children.Remove(rasterDisplay);rasterDisplay=null;rasterPixels=null;rasterLayer=null;rasterPreview=null;rasterChanged=false;
        if(layer!=null) layer.Ink.Visibility=layer.Visible ? Visibility.Visible : Visibility.Hidden;
    }
    private void RenderRasterGesture(Canvas canvas) { /* Raster preview stays at the selected layer's position. */ }
    internal static bool ApplyRasterDab(byte[] pixels,int width,int height,Point center,Point previous,double radius,double strength,int mode,bool[]? allowed=null)
    {
        int x0=Math.Max(0,(int)Math.Floor(center.X-radius)),x1=Math.Min(width-1,(int)Math.Ceiling(center.X+radius));
        int y0=Math.Max(0,(int)Math.Floor(center.Y-radius)),y1=Math.Min(height-1,(int)Math.Ceiling(center.Y+radius));
        if(x0>x1 || y0>y1 || strength<=0) return false;
        int dx=(int)Math.Round(previous.X-center.X),dy=(int)Math.Round(previous.Y-center.Y);
        int sx0=Math.Max(0,Math.Min(x0,x0+dx)-2),sy0=Math.Max(0,Math.Min(y0,y0+dy)-2);
        int sx1=Math.Min(width-1,Math.Max(x1,x1+dx)+2),sy1=Math.Min(height-1,Math.Max(y1,y1+dy)+2);
        int patchWidth=sx1-sx0+1;var source=new byte[patchWidth*(sy1-sy0+1)*4];
        for(int y=sy0;y<=sy1;y++) Buffer.BlockCopy(pixels,(y*width+sx0)*4,source,(y-sy0)*patchWidth*4,patchWidth*4);
        int Sample(int x,int y,int channel) => x<0 || y<0 || x>=width || y>=height ? 0 : source[((y-sy0)*patchWidth+x-sx0)*4+channel];
        bool changed=false;
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
        {
            if(allowed!=null && !allowed[y*width+x]) continue;
            double distance=Math.Sqrt(Math.Pow(x-center.X,2)+Math.Pow(y-center.Y,2));if(distance>radius) continue;
            double amount=strength*(1-distance/Math.Max(radius,.001));int index=(y*width+x)*4;
            for(int c=0;c<4;c++)
            {
                double target=0;
                if(mode==4) {for(int oy=-2;oy<=2;oy++) for(int ox=-2;ox<=2;ox++) target+=Sample(x+ox,y+oy,c);target/=25;}
                else if(mode==5) target=Sample(x+dx,y+dy,c);
                int value=(int)Math.Round(pixels[index+c]*(1-amount)+target*amount);
                if(value!=pixels[index+c]) {pixels[index+c]=(byte)value;changed=true;}
            }
        }
        return changed;
    }
    internal static int FloodFillPixels(byte[] pixels,byte[] reference,int width,int height,int x,int y,Color color,int tolerance,bool[]? allowed=null)
    {
        if(x<0 || y<0 || x>=width || y>=height) return 0;
        int seed=y*width+x;if(allowed!=null && !allowed[seed]) return 0;
        int offset=seed*4;var target=reference.AsSpan(offset,4).ToArray();var visited=new bool[width*height];var queue=new Queue<int>();queue.Enqueue(seed);visited[seed]=true;int count=0;
        bool Matches(int pixel) {if(allowed!=null && !allowed[pixel]) return false;for(int c=0;c<4;c++) if(Math.Abs(reference[pixel*4+c]-target[c])>tolerance) return false;return true;}
        void Visit(int xx,int yy) {if(xx<0 || yy<0 || xx>=width || yy>=height) return;int p=yy*width+xx;if(!visited[p]) {visited[p]=true;if(Matches(p)) queue.Enqueue(p);}}
        double a=color.A/255d;var fill=new[]{color.B*a,color.G*a,color.R*a,(double)color.A};
        while(queue.Count>0)
        {
            int p=queue.Dequeue(),px=p%width,py=p/width;count++;
            for(int c=0;c<4;c++) pixels[p*4+c]=(byte)Math.Round(fill[c]+pixels[p*4+c]*(1-a));
            Visit(px-1,py);Visit(px+1,py);Visit(px,py-1);Visit(px,py+1);
        }
        return count;
    }
}
