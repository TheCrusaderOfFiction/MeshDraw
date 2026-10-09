using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshDraw;
public partial class EditorWindow
{
    private bool pickingColor, syncingColor;
    private double paintHue, paintSaturation=.43, paintValue=.255;
    private readonly List<Color> recentPaintColors = new();
    private static Color Hsv(double h,double s,double v)
    {
        var c=v*s; var x=c*(1-Math.Abs(h/60%2-1)); var m=v-c;
        (double r,double g,double b)=h switch { <60=>(c,x,0d), <120=>(x,c,0d), <180=>(0d,c,x), <240=>(0d,x,c), <300=>(x,0d,c), _=>(c,0d,x) };
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    private void SetPaintColor(Color color) { BrushHex.Text=$"#{color.R:X2}{color.G:X2}{color.B:X2}"; ApplyTools(); }
    private void UpdatePaintControls(Color color)
    {
        if(ColorButton==null || SizeLabel==null || OpacityLabel==null) return;
        ColorButton.Background=new SolidColorBrush(color);
        ColorButton.Foreground=(color.R*.299+color.G*.587+color.B*.114)>140 ? Brushes.Black : Brushes.White;
        ColorButton.Content=$"#{color.R:X2}{color.G:X2}{color.B:X2}";
        SizeLabel.Text=$"Size · {BrushSize.Value:0} px"; OpacityLabel.Text=$"Opacity · {BrushOpacity.Value:P0}";
        PenTool.BorderBrush=Tool.SelectedIndex==0 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        EraseTool.BorderBrush=Tool.SelectedIndex==1 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        BlurTool.BorderBrush=Tool.SelectedIndex==4 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        SmudgeTool.BorderBrush=Tool.SelectedIndex==5 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        MoveLayerTool.BorderBrush=Tool.SelectedIndex==7 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        EdgeGuideTool.BorderBrush=Tool.SelectedIndex==9 ? Brushes.DarkOrange : Brushes.SlateGray;
        SelectionTool.BorderBrush=Tool.SelectedIndex==8 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        BucketTool.BorderBrush=Tool.SelectedIndex==6 ? Brushes.DeepSkyBlue : Brushes.SlateGray;
        if(syncingColor) return;
        double r=color.R/255d,g=color.G/255d,b=color.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
        if(d>0) paintHue=((max==r ? (g-b)/d : max==g ? (b-r)/d+2 : (r-g)/d+4)*60+360)%360;
        paintSaturation=max==0 ? 0 : d/max; paintValue=max;
        syncingColor=true; HueSlider.Value=paintHue; syncingColor=false; UpdateColorSquare();
    }
    private void UpdateColorSquare()
    {
        HueBackground.Background=new SolidColorBrush(Hsv(paintHue,1,1));
        Canvas.SetLeft(ColorCursor,paintSaturation*260-6); Canvas.SetTop(ColorCursor,(1-paintValue)*180-6);
    }
    private void OpenColorPicker(object sender,RoutedEventArgs e)
    {
        if(ColorPalette.Children.Count==0)
            foreach(var hex in new[]{"#000000","#FFFFFF","#5F6775","#E95164","#FF914D","#F3BF59","#53CDA0","#508BE8","#AA72DA","#D798BA","#8B5A3C","#253341"}) AddSwatch(ColorPalette,(Color)ColorConverter.ConvertFromString(hex));
        ColorPopup.IsOpen=true;
    }
    private void AddSwatch(Panel panel,Color color)
    {
        var button=new Button {Width=32,Height=32,Padding=new Thickness(0),Background=new SolidColorBrush(color),Tag=$"#{color.R:X2}{color.G:X2}{color.B:X2}",ToolTip=$"#{color.R:X2}{color.G:X2}{color.B:X2}"};
        button.Click+=PickColor; panel.Children.Add(button);
    }
    private void RememberPaintColor()
    {
        if(ColorButton.Background is not SolidColorBrush brush) return;
        recentPaintColors.Remove(brush.Color); recentPaintColors.Insert(0,brush.Color);
        if(recentPaintColors.Count>8) recentPaintColors.RemoveAt(8);
        RecentColors.Children.Clear(); foreach(var color in recentPaintColors) AddSwatch(RecentColors,color);
    }
    private void CloseColorPicker(object sender,RoutedEventArgs e) { RememberPaintColor(); ColorPopup.IsOpen=false; }
    private void HueChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(syncingColor || ColorSquare==null || BrushHex==null) return;
        paintHue=e.NewValue; CommitSquareColor();
    }
    private void CommitSquareColor()
    {
        syncingColor=true; SetPaintColor(Hsv(paintHue,paintSaturation,paintValue)); syncingColor=false; UpdateColorSquare();
    }
    private void ColorSquareDown(object sender,MouseButtonEventArgs e) { ColorSquare.CaptureMouse(); PickSquare(e.GetPosition(ColorSquare)); }
    private void ColorSquareMove(object sender,MouseEventArgs e) { if(ColorSquare.IsMouseCaptured && e.LeftButton==MouseButtonState.Pressed) PickSquare(e.GetPosition(ColorSquare)); }
    private void ColorSquareUp(object sender,MouseButtonEventArgs e) { ColorSquare.ReleaseMouseCapture(); }
    private void PickSquare(Point point) { paintSaturation=Math.Clamp(point.X/260,0,1); paintValue=1-Math.Clamp(point.Y/180,0,1); CommitSquareColor(); }
    private void SelectPaintTool(object sender,RoutedEventArgs e) { CancelGuidedStroke();pickingColor=false; int next=int.Parse(((Button)sender).Tag.ToString()!);if(next==8) {SelectSelectionLayer(sender,e);Tool.SelectedIndex=8;} else Tool.SelectedIndex=next; ApplyTools(); }
    private void BrushPreset(object sender,RoutedEventArgs e)
    {
        pickingColor=false; Tool.SelectedIndex=0;
        var preset=((Button)sender).Tag.ToString(); BrushSize.Value=preset=="pen" ? 5 : preset=="paint" ? 40 : 24; BrushOpacity.Value=preset=="paint" ? .3 : 1;
        ApplyTools();
    }
    private void SelectCoreLayer(object sender,RoutedEventArgs e)
    {
        var kind=(LayerKind)int.Parse(((Button)sender).Tag.ToString()!); var list=drawingViews[ActiveView];
        var layer=list.FirstOrDefault(l=>l.Kind==kind);
        if(layer==null)
        {
            if(list.Count>=32) {Status.Text="This view already has 32 layers.";return;}
            layer=Attach(new PaintLayer {Name=kind==LayerKind.Artwork ? "Outline" : "Color",Kind=kind});
            var outline=list.FindIndex(l=>l.Kind==LayerKind.Artwork);
            list.Insert(kind==LayerKind.BaseColor && outline>=0 ? outline+1 : 0,layer); RecordChange();
        }
        activeLayers[ActiveView]=layer.Id; pickingColor=false; Tool.SelectedIndex=0; RefreshEditor();
    }
    private void SelectEyedropper(object sender,RoutedEventArgs e) { pickingColor=true; Status.Text="Click the drawing to pick a color."; }
    private void SampleDrawing(object sender,MouseButtonEventArgs e)
    {
        if(!pickingColor || sender is not Grid surface) return;
        e.Handled=true; var point=e.GetPosition(surface);
        // Sample the displayed paint without guides or bone overlays.
        var visual=new DrawingVisual(); using(var context=visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,canvasWidth,canvasHeight));
            foreach(var layer in drawingViews[ActiveView].AsEnumerable().Reverse().Where(l=>l.Ink.Visibility==Visibility.Visible))
            { context.PushOpacity(layer.Opacity);layer.Draw(context,canvasWidth,canvasHeight);context.Pop(); }
        }
        var image=new RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32); image.Render(visual);
        byte[] pixel=new byte[4]; image.CopyPixels(new Int32Rect(Math.Clamp((int)point.X,0,canvasWidth-1),Math.Clamp((int)point.Y,0,canvasHeight-1),1,1),pixel,4,0);
        SetPaintColor(Color.FromRgb(pixel[2],pixel[1],pixel[0])); RememberPaintColor(); pickingColor=false;Status.Text="Picked color "+BrushHex.Text;
    }
}

