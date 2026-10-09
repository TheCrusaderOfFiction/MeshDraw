using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MeshDraw;
public partial class EditorWindow
{
    private readonly HashSet<string> selectedFaces=new();
    private readonly Dictionary<string,Image> shadowImages=new();
    private readonly Dictionary<int,(double size,double opacity)> toolSettings=new();
    private int settingsTool;
    private bool restoringTool;
    private static string OppositeFace(string face)=>face switch {"Front"=>"Back","Back"=>"Front","Left"=>"Right","Right"=>"Left","Top"=>"Bottom","Bottom"=>"Top",_=>""};
    private Point FacePoint(Point point,string source,string target)=>OppositeFace(source)==target ? source is "Top" or "Bottom" ? new Point(point.X,canvasHeight-point.Y) : new Point(canvasWidth-point.X,point.Y) : point;
    private void InitializeFaceSelection()
    {
        FaceSelection.Children.Clear();
        foreach(var name in drawingViews.Keys)
        {
            var box=new CheckBox {Content=FaceName(name),Tag=name,IsChecked=selectedFaces.Contains(name),ToolTip="Apply paint to this face too"};
            box.Checked+=FaceSelectionChanged;box.Unchecked+=FaceSelectionChanged;FaceSelection.Children.Add(box);
        }
    }
    private void FaceSelectionChanged(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();var box=(CheckBox)sender;var face=(string)box.Tag;
        if(box.IsChecked==true) selectedFaces.Add(face);else selectedFaces.Remove(face);
        ApplyTools();UpdateMultiViewSelection();
        Status.Text="Paint applies to the active face and checked faces. Opposite faces are mirrored; matching layers receive the edit.";
    }
    private IEnumerable<(string face,PaintLayer layer)> FaceTargets(PaintLayer source)
    {
        yield return (ActiveView,source);
        foreach(var face in selectedFaces.Where(f=>f!=ActiveView))
        {
            var layer=drawingViews[face].FirstOrDefault(l=>l.Kind==source.Kind && l.Name==source.Name) ?? drawingViews[face].FirstOrDefault(l=>l.Kind==source.Kind);
            if(layer is {Locked:false,Visible:true,Opacity:>0}) yield return (face,layer);
        }
    }
    private void CommitFaceStroke(PaintLayer layer,Stroke stroke)
    {
        syncing=true;
        try {foreach(var target in FaceTargets(layer)) {var copy=stroke.Clone();if(OppositeFace(ActiveView)==target.face) copy.Transform(ActiveView is "Top" or "Bottom" ? new Matrix(1,0,0,-1,0,canvasHeight) : new Matrix(-1,0,0,1,canvasWidth,0),false);if(SelectionFor(target.face) is { } selection) CommitSelectedStroke(target.layer,copy,selection);else target.layer.Ink.Strokes.Add(copy);}}
        finally {syncing=false;}
        RecordChange();Layers.Items.Refresh();RefreshOppositeShadows();
    }
    private void ShadowChanged(object sender,RoutedEventArgs e) {if(drawingViews.Count>=6) RefreshOppositeShadows();}
    private void RefreshOppositeShadows()
    {
        foreach(var face in DrawingDocument.ViewNames)
        {
            if(!shadowImages.TryGetValue(face,out var image)) shadowImages[face]=image=new Image {IsHitTestVisible=false,Opacity=.18,Stretch=Stretch.Fill};
            if(image.Parent is Panel parent) parent.Children.Remove(image);
            image.Width=canvasWidth;image.Height=canvasHeight;
            if(ShowOppositeShadow?.IsChecked!=true) continue;
            const int size=512;var visual=new DrawingVisual();using(var context=visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(size/(double)canvasWidth,size/(double)canvasHeight));
                context.PushTransform(new MatrixTransform(face is "Top" or "Bottom" ? new Matrix(1,0,0,-1,0,canvasHeight) : new Matrix(-1,0,0,1,canvasWidth,0)));
                foreach(var layer in drawingViews[OppositeFace(face)].Where(l=>l.Visible && l.Kind is LayerKind.Artwork or LayerKind.BaseColor).Reverse()) {context.PushOpacity(layer.Opacity);layer.Draw(context,canvasWidth,canvasHeight);context.Pop();}
            }
            var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();image.Source=bitmap;
            surfaces[face].Children.Insert(Math.Min(1,surfaces[face].Children.Count),image);
        }
    }
    private void RememberToolSettings()
    {
        if(restoringTool || BrushSize==null || BrushOpacity==null || drawingViews.Count<6) return;
        toolSettings[settingsTool]=(BrushSize.Value,BrushOpacity.Value);
    }
    private void RestoreToolSettings()
    {
        var next=Tool.SelectedIndex;if(next==settingsTool) return;
        RememberToolSettings();settingsTool=next;restoringTool=true;
        try {var value=toolSettings.GetValueOrDefault(next,(next is 4 or 5 ? 40d : next==1 ? 20d : 5d,1d));BrushSize.Value=value.Item1;BrushOpacity.Value=value.Item2;} finally {restoringTool=false;}
    }
    private void UpdateContextSettings()
    {
        if(PenSettings==null) return;int tool=Tool.SelectedIndex;
        SizeOpacitySettings.Visibility=tool is 2 or 3 or 7 or 8 or 9 ? Visibility.Collapsed : Visibility.Visible;
        SizeLabel.Visibility=BrushSize.Visibility=tool==6 ? Visibility.Collapsed : Visibility.Visible;
        EdgeGuideSettings.Visibility=tool==9 ? Visibility.Visible : Visibility.Collapsed;
        SelectionSettings.Visibility=(SelectionEditing && tool!=9) || tool==8 ? Visibility.Visible : Visibility.Collapsed;
        OpacityLabel.Visibility=BrushOpacity.Visibility=SelectionEditing ? Visibility.Collapsed : Visibility.Visible;
        PenSettings.Visibility=tool==0 ? Visibility.Visible : Visibility.Collapsed;
        ColorSettings.Visibility=!SelectionEditing && tool is 0 or 6 ? Visibility.Visible : Visibility.Collapsed;
        RasterSettings.Visibility=!SelectionEditing && tool is 4 or 5 ? Visibility.Visible : Visibility.Collapsed;
        BucketSettings.Visibility=!SelectionEditing && tool==6 ? Visibility.Visible : Visibility.Collapsed;
    }
}
