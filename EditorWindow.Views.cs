using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace MeshDraw;
public partial class EditorWindow
{
    private string viewLayout="single";
    private readonly Dictionary<string,Viewbox> multiViewHosts=new();
    private readonly Dictionary<string,Border> multiViewFrames=new();
    private DispatcherTimer? outlinePreviewTimer;
    private double previewYaw=35,previewPitch=22,previewDistance=5.5;
    private Point? previewDrag;
    private int previewOccupiedCells;
    private Task previewLoadTask=Task.CompletedTask;
    private int previewRequest;
    private void ChangeViewLayout(object sender,RoutedEventArgs e) => SetViewLayout(((Button)sender).Tag.ToString()!);
    private void SetViewLayout(string layout)
    {
        if(drawingViews.Count<6) return;
        foreach(var host in multiViewHosts.Values) host.Child=null;
        AllViews.Children.Clear();multiViewHosts.Clear();multiViewFrames.Clear();
        foreach(TabItem tab in Views.Items)
        {
            var name=(string)tab.Tag;var single=(Viewbox)tab.Content;single.Child=null;
            if(layout=="all")
            {
                var host=new Viewbox {Child=surfaces[name],Stretch=Stretch.Uniform};multiViewHosts[name]=host;
                var panel=new DockPanel();var title=new Button {Content=FaceName(name),Tag=name,Margin=new Thickness(0),Padding=new Thickness(6),HorizontalContentAlignment=HorizontalAlignment.Left};
                title.Click+=(_,_)=>SelectMultiView(name);DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);panel.Children.Add(host);
                var frame=new Border {Child=panel,BorderThickness=new Thickness(2),Margin=new Thickness(3),Background=new SolidColorBrush(Color.FromRgb(36,45,58))};
                multiViewFrames[name]=frame;AllViews.Children.Add(frame);
            }
            else single.Child=surfaces[name];
        }
        AllViews.Rows=(drawingViews.Count+2)/3;
        LoadPreviewFaces();viewLayout=layout;Views.Visibility=layout=="single" ? Visibility.Visible : Visibility.Collapsed;
        AllViews.Visibility=layout=="all" ? Visibility.Visible : Visibility.Collapsed;
        OutlinePreviewScreen.Visibility=layout=="3d" ? Visibility.Visible : Visibility.Collapsed;
        UpdateMultiViewSelection();if(layout=="3d") RenderOutlinePreview();
        Status.Text=layout=="all" ? "All views visible. Select a view by its title or canvas to draw and edit its layers." : layout=="3d" ? "Inspect the outline planes and their shared silhouette overlap." : "Single-view drawing.";
    }
    private void SelectMultiView(string name)
    {
        if(viewLayout!="all" || ActiveView==name) return;
        Views.SelectedItem=Views.Items.Cast<TabItem>().First(t=>(string)t.Tag==name);
    }
    private void UpdateMultiViewSelection()
    {
        foreach(var pair in multiViewFrames) pair.Value.BorderBrush=pair.Key==ActiveView ? Brushes.DeepSkyBlue : selectedFaces.Contains(pair.Key) ? Brushes.MediumSeaGreen : Brushes.SlateGray;
    }
    private void MarkOutlinePreviewDirty()
    {
        if(viewLayout!="3d") return;
        if(outlinePreviewTimer==null)
        {
            outlinePreviewTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
            outlinePreviewTimer.Tick+=(_,_)=>{outlinePreviewTimer.Stop();RenderOutlinePreview();};
        }
        outlinePreviewTimer.Stop();outlinePreviewTimer.Start();
    }
    private void DrawPreviewOutline(DrawingContext context,PaintLayer layer,int size)
    {
        if(layer.Photo!=null)
        {
            var scale=Math.Min(canvasWidth/(double)layer.Photo.PixelWidth,canvasHeight/(double)layer.Photo.PixelHeight);
            context.DrawImage(layer.Photo,new Rect((canvasWidth-layer.Photo.PixelWidth*scale)/2,(canvasHeight-layer.Photo.PixelHeight*scale)/2,layer.Photo.PixelWidth*scale,layer.Photo.PixelHeight*scale));
        }
        foreach(var original in layer.Ink.Strokes)
        {
            var stroke=original.Clone();var attributes=stroke.DrawingAttributes.Clone();
            attributes.Width=Math.Max(attributes.Width,canvasWidth/(double)size*1.8);attributes.Height=Math.Max(attributes.Height,canvasHeight/(double)size*1.8);
            stroke.DrawingAttributes=attributes;stroke.Draw(context);
        }
    }
    private BitmapSource OutlineTexture(string view,int size=192)
    {
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size/(double)canvasWidth,size/(double)canvasHeight));
            foreach(var layer in drawingViews[view].Where(l=>l.Kind==LayerKind.Artwork && l.Visible && l.Opacity>0).Reverse())
            {context.PushOpacity(layer.Opacity);DrawPreviewOutline(context,layer,size);context.Pop();}
        }
        var image=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);image.Render(visual);
        var pixels=new byte[size*size*4];image.CopyPixels(pixels,size*4,0);
        var tint=view is "Front" or "Back" ? Colors.Coral : view is "Left" or "Right" ? Colors.MediumSeaGreen : Colors.CornflowerBlue;
        for(int i=0;i<pixels.Length;i+=4) {double a=pixels[i+3]/255d;pixels[i]=(byte)(tint.B*a);pixels[i+1]=(byte)(tint.G*a);pixels[i+2]=(byte)(tint.R*a);}
        return BitmapSource.Create(size,size,96,96,PixelFormats.Pbgra32,null,pixels,size*4);
    }
    private bool[,] OutlineMask(string view,out bool closed,int size=256)
    {
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,size,size));context.PushTransform(new ScaleTransform(size/(double)canvasWidth,size/(double)canvasHeight));
            foreach(var layer in drawingViews[view].Where(l=>l.Kind==LayerKind.Artwork && l.Visible && l.Opacity>0).Reverse())
            {context.PushOpacity(layer.Opacity);DrawPreviewOutline(context,layer,size);context.Pop();}
        }
        var image=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);image.Render(visual);var pixels=new byte[size*size*4];image.CopyPixels(pixels,size*4,0);
        var ink=new bool[size,size];int inkCount=0;
        for(int y=0;y<size;y++) for(int x=0;x<size;x++)
        {var i=(y*size+x)*4;ink[x,y]=Math.Min(pixels[i],Math.Min(pixels[i+1],pixels[i+2]))<220;if(ink[x,y]) inkCount++;}
        var outside=new bool[size,size];var queue=new Queue<(int x,int y)>();
        void Visit(int x,int y) {if(x>=0 && y>=0 && x<size && y<size && !ink[x,y] && !outside[x,y]) {outside[x,y]=true;queue.Enqueue((x,y));}}
        for(int i=0;i<size;i++) {Visit(i,0);Visit(i,size-1);Visit(0,i);Visit(size-1,i);}
        while(queue.Count>0) {var (x,y)=queue.Dequeue();Visit(x-1,y);Visit(x+1,y);Visit(x,y-1);Visit(x,y+1);}
        var mask=new bool[size,size];int filled=0;
        for(int y=0;y<size;y++) for(int x=0;x<size;x++) {mask[x,y]=!outside[x,y];if(mask[x,y]) filled++;}
        closed=inkCount>0 && filled>inkCount*1.15 && filled>30;return mask;
    }
    internal static Point3D OutlinePlanePoint(string view,double u,double v) => view switch
    {
        "Front"=>new(2*u-1,1-2*v,1),"Back"=>new(1-2*u,1-2*v,-1),
        "Left"=>new(-1,1-2*v,2*u-1),"Right"=>new(1,1-2*v,1-2*u),
        "Top"=>new(2*u-1,1,2*v-1),"Bottom"=>new(2*u-1,-1,1-2*v),_=>throw new ArgumentException("Unknown view")
    };
    private static void AddQuad(MeshGeometry3D mesh,Point3D a,Point3D b,Point3D c,Point3D d)
    {
        int index=mesh.Positions.Count;foreach(var p in new[]{a,b,c,d}) mesh.Positions.Add(p);
        foreach(var i in new[]{0,1,2,0,2,3}) mesh.TriangleIndices.Add(index+i);
    }
    private MeshGeometry3D BuildOutlineOverlap(Dictionary<string,bool[,]> masks,int n=28) => BuildOutlineOverlapCore(masks,n,out previewOccupiedCells);
    private static MeshGeometry3D BuildOutlineOverlapCore(Dictionary<string,bool[,]> masks,int n,out int occupied,FaceDefinition[]? extras=null)
    {
        var cells=new bool[n,n,n];occupied=0;
        var fields=new Dictionary<string,double[,]>();
        foreach(var face in extras ?? [])
        {
            var mask=masks[face.Id];int width=mask.GetLength(0),height=mask.GetLength(1);var field=new double[width,height];
            for(int a=0;a<width;a++) for(int b=0;b<height;b++) field[a,b]=mask[a,b] ? 0 : width+height;
            for(int a=0;a<width;a++) for(int b=0;b<height;b++)
            {
                if(a>0) field[a,b]=Math.Min(field[a,b],field[a-1,b]+1);
                if(b>0) field[a,b]=Math.Min(field[a,b],field[a,b-1]+1);
                if(a>0 && b>0) field[a,b]=Math.Min(field[a,b],field[a-1,b-1]+Math.Sqrt(2));
                if(a>0 && b+1<height) field[a,b]=Math.Min(field[a,b],field[a-1,b+1]+Math.Sqrt(2));
            }
            for(int a=width-1;a>=0;a--) for(int b=height-1;b>=0;b--)
            {
                if(a+1<width) field[a,b]=Math.Min(field[a,b],field[a+1,b]+1);
                if(b+1<height) field[a,b]=Math.Min(field[a,b],field[a,b+1]+1);
                if(a+1<width && b+1<height) field[a,b]=Math.Min(field[a,b],field[a+1,b+1]+Math.Sqrt(2));
                if(a+1<width && b>0) field[a,b]=Math.Min(field[a,b],field[a+1,b-1]+Math.Sqrt(2));
            }
            fields[face.Id]=field;
        }
        var regional=(extras ?? []).Where(f=>f.IsSection && f.SectionMode!="Whole").ToArray();
        var signedFields=regional.ToDictionary(f=>f.Id,f=>SignedSectionMask(masks[f.Id]));
        var blendFields=regional.Any(f=>f.SectionMode=="Blend")?DrawingDocument.ViewNames.ToDictionary(name=>name,name=>SignedSectionMask(masks[name])):new Dictionary<string,double[,]>();
        double? BaseDistance(string name,double u,double v){if(!blendFields.TryGetValue(name,out var field))return null;return 2*field[Math.Clamp((int)(u*field.GetLength(0)),0,field.GetLength(0)-1),Math.Clamp((int)(v*field.GetLength(1)),0,field.GetLength(1)-1)]/Math.Max(field.GetLength(0),field.GetLength(1));}
        double? PairDistance(string a,double u,double v,string b,double bu,double bv){var da=BaseDistance(a,u,v);var db=BaseDistance(b,bu,bv);return da.HasValue && db.HasValue?Math.Max(da.Value,db.Value):null;}
        bool Sample(string name,double u,double v) {var mask=masks[name];return mask[Math.Clamp((int)(u*mask.GetLength(0)),0,mask.GetLength(0)-1),Math.Clamp((int)(v*mask.GetLength(1)),0,mask.GetLength(1)-1)];}
        for(int x=0;x<n;x++) for(int y=0;y<n;y++) for(int z=0;z<n;z++)
        {
            double u=(x+.5)/n,v=(y+.5)/n,w=(z+.5)/n;
            var world=new Vector3D(2*u-1,2*v-1,2*w-1);
            bool fb=Sample("Front",u,1-v)&&Sample("Back",1-u,1-v),lr=Sample("Left",w,1-v)&&Sample("Right",1-w,1-v),tb=Sample("Top",u,w)&&Sample("Bottom",u,1-w);
            cells[x,y,z]=RegionalPreviewProfile(world,2,fb,regional,signedFields,PairDistance("Front",u,1-v,"Back",1-u,1-v)) && RegionalPreviewProfile(world,0,lr,regional,signedFields,PairDistance("Left",w,1-v,"Right",1-w,1-v)) && RegionalPreviewProfile(world,1,tb,regional,signedFields,PairDistance("Top",u,w,"Bottom",u,1-w));
            if(cells[x,y,z]) foreach(var face in extras ?? [])
            {
                if(face.IsSection && face.SectionMode!="Whole")continue;
                var basis=FaceBasis(face);var point=new Vector3D(2*u-1-face.MoveX,2*v-1-face.MoveY,2*w-1-face.MoveZ);
                double fu=(Vector3D.DotProduct(point,basis.u)+1)/2,fv=(1-Vector3D.DotProduct(point,basis.v))/2;
                double depth=Vector3D.DotProduct(point,basis.normal);
                var field=fields[face.Id];double outside=fu<0 || fu>1 || fv<0 || fv>1 ? 4 :
                    2*field[Math.Clamp((int)(fu*field.GetLength(0)),0,field.GetLength(0)-1),Math.Clamp((int)(fv*field.GetLength(1)),0,field.GetLength(1)-1)]/Math.Max(field.GetLength(0),field.GetLength(1));
                double allowance=face.IsSection ? 1.5*Math.Abs(depth-face.Offset*(Math.Abs(basis.normal.X)+Math.Abs(basis.normal.Y)+Math.Abs(basis.normal.Z))) : 0;
                if(outside>allowance) {cells[x,y,z]=false;break;}
            }
            if(cells[x,y,z]) occupied++;
        }
        var mesh=new MeshGeometry3D();bool Filled(int x,int y,int z)=>x>=0&&y>=0&&z>=0&&x<n&&y<n&&z<n&&cells[x,y,z];
        for(int x=0;x<n;x++) for(int y=0;y<n;y++) for(int z=0;z<n;z++)
        {
            if(!cells[x,y,z]) continue;double a=2d*x/n-1,b=2d*(x+1)/n-1,c=2d*y/n-1,d=2d*(y+1)/n-1,e=2d*z/n-1,f=2d*(z+1)/n-1;
            if(!Filled(x-1,y,z)) AddQuad(mesh,new(a,c,e),new(a,c,f),new(a,d,f),new(a,d,e));
            if(!Filled(x+1,y,z)) AddQuad(mesh,new(b,c,f),new(b,c,e),new(b,d,e),new(b,d,f));
            if(!Filled(x,y-1,z)) AddQuad(mesh,new(a,c,f),new(a,c,e),new(b,c,e),new(b,c,f));
            if(!Filled(x,y+1,z)) AddQuad(mesh,new(a,d,e),new(a,d,f),new(b,d,f),new(b,d,e));
            if(!Filled(x,y,z-1)) AddQuad(mesh,new(b,c,e),new(a,c,e),new(a,d,e),new(b,d,e));
            if(!Filled(x,y,z+1)) AddQuad(mesh,new(a,c,f),new(b,c,f),new(b,d,f),new(a,d,f));
        }
        return mesh;
    }
    private static (int cells,int mask,int texture,string label) PreviewQualitySettings(int value)
    {
        int cells=Math.Clamp(value,16,72);
        int mask=cells<=28 ? 128+(cells-16)*128/12 : cells<=48 ? 256+(cells-28)*256/20 : 512+(cells-48)*256/24;
        int texture=cells<=28 ? 128+(cells-16)*64/12 : cells<=48 ? 192+(cells-28)*320/20 : 512+(cells-48)*256/24;
        return (cells,mask,texture,cells<=20 ? "Low" : cells<=32 ? "Balanced" : cells<=52 ? "High" : "Ultra");
    }
    private void PreviewQualityChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(PreviewQualityInput!=null) PreviewQualityInput.Text=((int)Math.Round(e.NewValue)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(drawingViews.Count<6 || viewLayout!="3d") return;
        if(PreviewQuality.IsMouseCaptureWithin) MarkOutlinePreviewDirty();else RenderOutlinePreview();
    }
    private void PreviewQualityDragFinished(object sender,MouseButtonEventArgs e)
    {
        if(drawingViews.Count>=6 && viewLayout=="3d") RenderOutlinePreview();
    }
    private void PreviewQualityCommitted(object sender,RoutedEventArgs e)
    {
        if(int.TryParse(PreviewQualityInput.Text,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var value))
            PreviewQuality.Value=Math.Clamp(value,16,72);
        PreviewQualityInput.Text=((int)Math.Round(PreviewQuality.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private void PreviewQualityKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter) {PreviewQualityCommitted(sender,e);e.Handled=true;}
    }
    private void RenderOutlinePreview() {outlinePreviewTimer?.Stop();previewLoadTask=LoadOutlinePreviewAsync();}
    private async Task LoadOutlinePreviewAsync()
    {
        if(OutlineViewport==null || drawingViews.Count<6) return;
        var quality=PreviewQualitySettings((int)Math.Round(PreviewQuality?.Value ?? 28));
        int request=++previewRequest;PreviewLoadingOverlay.Visibility=Visibility.Visible;PreviewRefreshButton.IsEnabled=false;PreviewLoadingBar.Value=0;
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
        var scene=new Model3DGroup();scene.Children.Add(new AmbientLight(Colors.White));
        var masks=new Dictionary<string,bool[,]>();var incomplete=new List<string>();
        foreach(var view in drawingViews.Keys.ToArray())
        {
            if(request!=previewRequest) return;
            PreviewLoadingText.Text="Reading "+view.ToLowerInvariant()+" outline…";
            masks[view]=OutlineMask(view,out var closed,quality.mask);if(!closed) incomplete.Add(FaceName(view));
            PreviewLoadingBar.Value=masks.Count*8;await Dispatcher.Yield(DispatcherPriority.Background);
        }
        var faceDefinitions=extraFaces.ToArray();
        previewOccupiedCells=0;
        if(incomplete.Count==0)
        {
            PreviewLoadingText.Text="Building shared volume…";PreviewLoadingBar.Value=55;
            var result=await Task.Run(()=> {var mesh=BuildOutlineOverlapCore(masks,quality.cells,out var occupied,faceDefinitions);mesh.Freeze();return (mesh,occupied);});
            if(request!=previewRequest) return;var hull=result.mesh;previewOccupiedCells=result.Item2;
            if(ShowOverlapVolume.IsChecked==true) {var material=new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(74,180,210)));scene.Children.Add(new GeometryModel3D(hull,material) {BackMaterial=material});}
        }
        PreviewLoadingText.Text="Loading outline planes…";PreviewLoadingBar.Value=75;
        previewFaceModels.Clear();
        if(ShowOutlinePlanes.IsChecked==true)
        foreach(var view in drawingViews.Keys.ToArray())
        {
            if(request!=previewRequest) return;
            var mesh=new MeshGeometry3D();AddQuad(mesh,FacePlanePoint(view,0,0),FacePlanePoint(view,1,0),FacePlanePoint(view,1,1),FacePlanePoint(view,0,1));
            foreach(var uv in new[]{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)}) mesh.TextureCoordinates.Add(uv);
            var texture=OutlineTexture(view,quality.texture);var material=new EmissiveMaterial(new ImageBrush(texture) {Opacity=.85});var planeModel=new GeometryModel3D(mesh,material) {BackMaterial=material};scene.Children.Add(planeModel);previewFaceModels[planeModel]=(view,false);
            // Thin colored borders identify each projection plane, even when its drawing is blank.
            var color=view is "Front" or "Back" ? Colors.Coral : view is "Left" or "Right" ? Colors.MediumSeaGreen : Colors.CornflowerBlue;
            var border=new MeshGeometry3D();double t=.006;
            foreach(var (u,v,w,h) in new[]{(0d,0d,1d,t),(0d,1-t,1d,1d),(0d,0d,t,1d),(1-t,0d,1d,1d)}) AddQuad(border,FacePlanePoint(view,u,v),FacePlanePoint(view,w,v),FacePlanePoint(view,w,h),FacePlanePoint(view,u,h));
            var edge=new EmissiveMaterial(new SolidColorBrush(color));var borderModel=new GeometryModel3D(border,edge) {BackMaterial=edge};scene.Children.Add(borderModel);previewFaceModels[borderModel]=(view,true);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        if(request!=previewRequest) return;
        OutlineViewport.Children.Clear();OutlineViewport.Children.Add(new ModelVisual3D {Content=scene});UpdatePreviewCamera();PreviewLoadingBar.Value=100;
        await Dispatcher.Yield(DispatcherPriority.Render);
        OutlinePreviewStatus.Text=quality.label+" quality · "+quality.cells+"³ volume samples · "+(incomplete.Count>0 ? "Close the outline in: "+string.Join(", ",incomplete)+". Planes show the current drawings." : previewOccupiedCells==0 ? "The drawing silhouettes have no shared overlap. Check alignment and proportions." : "Approximate shared volume from the drawing silhouettes. Coral: front/back · Green: left/right · Blue: top/bottom.");
        }
        catch(Exception error) {if(request==previewRequest) OutlinePreviewStatus.Text="Preview could not load: "+error.Message;}
        finally {if(request==previewRequest) {PreviewLoadingOverlay.Visibility=Visibility.Collapsed;PreviewRefreshButton.IsEnabled=true;}}
    }
    private void UpdatePreviewCamera()
    {
        var yaw=previewYaw*Math.PI/180;var pitch=previewPitch*Math.PI/180;
        var position=new Point3D(previewDistance*Math.Cos(pitch)*Math.Sin(yaw),previewDistance*Math.Sin(pitch),previewDistance*Math.Cos(pitch)*Math.Cos(yaw));
        OutlineViewport.Camera=new PerspectiveCamera(position,new Vector3D(-position.X,-position.Y,-position.Z),new Vector3D(0,1,0),45);
    }
    private void PreviewOrbitDown(object sender,MouseButtonEventArgs e) {if(BeginPreviewFaceDrag(e)) return;previewDrag=e.GetPosition(OutlineViewport);OutlineViewport.CaptureMouse();}
    private void PreviewOrbitMove(object sender,MouseEventArgs e)
    {
        if(MovePreviewFaceDrag(e)) return;
        if(previewDrag==null || e.LeftButton!=MouseButtonState.Pressed) return;var point=e.GetPosition(OutlineViewport);var delta=point-previewDrag.Value;previewDrag=point;
        previewYaw+=delta.X*.5;previewPitch=Math.Clamp(previewPitch-delta.Y*.5,-80,80);UpdatePreviewCamera();
    }
    private void PreviewOrbitUp(object sender,MouseButtonEventArgs e) {EndPreviewFaceDrag();previewDrag=null;OutlineViewport.ReleaseMouseCapture();}
    private void PreviewZoom(object sender,MouseWheelEventArgs e) {previewDistance=Math.Clamp(previewDistance-e.Delta/120d*.4,2.5,12);UpdatePreviewCamera();e.Handled=true;}
    private void ResetPreviewCamera(object sender,RoutedEventArgs e) {previewYaw=35;previewPitch=22;previewDistance=5.5;UpdatePreviewCamera();}
    private void RefreshOutlinePreview(object sender,RoutedEventArgs e) => RenderOutlinePreview();
    private void PreviewFlagsChanged(object sender,RoutedEventArgs e) {if(viewLayout=="3d") RenderOutlinePreview();}
}
