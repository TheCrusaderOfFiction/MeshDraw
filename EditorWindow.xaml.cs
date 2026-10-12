using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MeshDraw;

public partial class EditorWindow : Window
{
    private readonly Dictionary<string,List<PaintLayer>> drawingViews = new();
    private readonly Dictionary<string,Grid> surfaces = new();
    private readonly Dictionary<string,Canvas> overlays = new();
    private readonly Dictionary<string,string> activeLayers = new();
    private readonly List<string> undo = new();
    private readonly Stack<string> redo = new();
    private int canvasWidth=640, canvasHeight=640;
    private string skeletonType="Custom";
    private RigData rig = new([], []);
    private bool syncing;
    private string savedState = "";
    private string? selectedBone;
    private string? dragJoint;
    private JointData? boneStart;
    private Point? dragPoint;
    private string? dragBefore;

    public EditorWindow()
    {
        InitializeComponent();
        InitializeCanvasNavigation();
        syncing = true;
        foreach (var name in DrawingDocument.ViewNames)
        {
            CreateFaceSurface(name,name);
        }
        Views.SelectedIndex = 0;
        syncing = false;
        RefreshEditor(); savedState = Snapshot(); undo.Add(savedState); RefreshBackend(); UpdateDrawingTitle(); RefreshSkeletonTypes();
        InitializeFaceSelection(); LoadGallery();
        Closing += (_, e) => { if (!SaveBeforeLeaving()) e.Cancel=true; else StopGenerator(); };
    }
    private string ActiveView => Views.SelectedItem is TabItem item ? (item.Tag ?? item.Header).ToString()! : "Front";
    private PaintLayer? ActiveLayer => drawingViews.TryGetValue(ActiveView, out var items)
        ? items.FirstOrDefault(layer => (SelectionEditing ? lastPaintLayers.GetValueOrDefault(ActiveView) : activeLayers.GetValueOrDefault(ActiveView)) == layer.Id) ?? items.FirstOrDefault() : null;
    private PaintLayer Attach(PaintLayer layer)
    {
        layer.Ink.Strokes.StrokesChanged += (_, _) => { if (!syncing) { RecordChange(); Layers.Items.Refresh(); RefreshOppositeShadows(); MarkOutlinePreviewDirty(); } };
        return layer;
    }
    private EditorDocument Document() => new(2, Output.SelectedIndex, drawingViews.ToDictionary(p => p.Key, p => p.Value.Select(DrawingDocument.Data).ToList()), rig,canvasWidth,canvasHeight,skeletonType,extraFaces.ToList(),selections.ToDictionary(p=>p.Key,p=>p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),vertexLimit,edgeGuides.ToDictionary(p=>p.Key,p=>p.Value.ToList()),connectionEdges.Where(ConnectionExists).ToList(),lineEqualities.Where(EqualityExists).ToList());
    private string Snapshot() => JsonSerializer.Serialize(Document());
    private void RecordChange()
    {
        if (syncing || drawingViews.Count < 6) return;
        var state = Snapshot();
        if (undo.Count > 0 && undo[^1] == state) return;
        undo.Add(state); if (undo.Count > 24) undo.RemoveAt(0); redo.Clear(); MarkOutlinePreviewDirty();
    }
    private bool ConfirmDiscard() => Snapshot() == savedState || MessageBox.Show(this, "Discard changes made since the last save?", "Unsaved project", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void ApplyDocument(EditorDocument document)
    {
        CancelGuidedStroke();
        DrawingDocument.Validate(document);
        var loaded = document.Layers.ToDictionary(p => p.Key, p => p.Value.Select(item => Attach(DrawingDocument.Layer(item))).ToList());
        syncing=true;
        try
        {
            var selected=ActiveView;var previousLayers=new Dictionary<string,string>(activeLayers);var previousIds=drawingViews.ToDictionary(p=>p.Key,p=>p.Value.Select(l=>l.Id).ToHashSet());
            foreach(var host in multiViewHosts.Values) host.Child=null;
            foreach(TabItem tab in Views.Items) ((Viewbox)tab.Content).Child=null;
            Views.Items.Clear();surfaces.Clear();overlays.Clear();drawingViews.Clear();activeLayers.Clear();
            extraFaces.Clear();extraFaces.AddRange(document.ExtraFaces ?? []);
            edgeGuides.Clear();foreach(var pair in document.EdgeGuides ?? []) edgeGuides[pair.Key]=pair.Value.ToList();
            connectionEdges.Clear();connectionEdges.AddRange(document.Connections ?? []);
            lineEqualities.Clear();lineEqualities.AddRange(document.LineEqualities ?? []);
            selectionMasks.Clear();selections.Clear();foreach(var pair in document.Selections ?? []) {var geometry=Geometry.Parse(pair.Value);geometry.Freeze();selections[pair.Key]=geometry;}
            foreach(var pair in loaded) {CreateFaceSurface(pair.Key,FaceName(pair.Key));drawingViews[pair.Key]=pair.Value;activeLayers[pair.Key]=(previousLayers.GetValueOrDefault(pair.Key)==SelectionLayerId && pair.Value.Any(l=>previousIds.GetValueOrDefault(pair.Key)?.Contains(l.Id)==true)) || pair.Value.Any(l=>l.Id==previousLayers.GetValueOrDefault(pair.Key)) ? previousLayers[pair.Key] : pair.Value[0].Id;}
            Views.SelectedItem=Views.Items.Cast<TabItem>().FirstOrDefault(t=>(string)t.Tag==selected) ?? Views.Items[0];
            selectedFaces.IntersectWith(drawingViews.Keys);InitializeFaceSelection();
            canvasWidth=document.CanvasWidth;canvasHeight=document.CanvasHeight;skeletonType=document.SkeletonType;
            rig = document.Skeleton; selectedBone = rig.Bones.Any(b => b.Id==selectedBone) ? selectedBone : null;
            Output.SelectedIndex=document.OutputType;LoadVertexLimit(document.VertexLimit);
        }
        finally { syncing=false; }
        SetViewLayout(viewLayout);RefreshEditor();
    }
    private void Undo(object sender, RoutedEventArgs e)
    {
        if (undo.Count < 2) return;
        redo.Push(undo[^1]); undo.RemoveAt(undo.Count-1); ApplyDocument(DrawingDocument.Read(undo[^1]));
        Status.Text="Undid the last edit.";
    }
    private void Redo(object sender, RoutedEventArgs e)
    {
        if (redo.Count==0) return;
        var state=redo.Pop(); undo.Add(state); ApplyDocument(DrawingDocument.Read(state)); Status.Text="Redid the edit.";
    }
    private void ViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && drawingViews.Count>=6 && ReferenceEquals(e.Source,Views)) RefreshEditor();
    }
    private void RefreshEditor()
    {
        if (drawingViews.Count < 6) return;
        syncing=true;
        try
        {
            Layers.ItemsSource = new object[]{SelectionRow(ActiveView)}.Concat(drawingViews[ActiveView]).ToList(); Layers.SelectedItem=SelectionEditing ? SelectionRow(ActiveView) : ActiveLayer;
            var active=ActiveLayer!;
            Layers.ScrollIntoView(Layers.SelectedItem);
            if(!SelectionEditing) {activeLayers[ActiveView]=active.Id;lastPaintLayers[ActiveView]=active.Id;}
            LayerName.IsEnabled=LayerVisible.IsEnabled=LayerLocked.IsEnabled=LayerOpacity.IsEnabled=!SelectionEditing;
            LayerName.Text=SelectionEditing ? "Selection layer" : active.Name; LayerVisible.IsChecked=SelectionEditing || active.Visible; LayerLocked.IsChecked=!SelectionEditing && active.Locked; LayerOpacity.Value=SelectionEditing ? 1 : active.Opacity;
            BoneList.ItemsSource=rig.Bones.ToList(); BoneList.SelectedItem=rig.Bones.FirstOrDefault(b => b.Id==selectedBone);
            BoneName.Text=rig.Bones.FirstOrDefault(b => b.Id==selectedBone)?.Name ?? "";
            foreach (var name in drawingViews.Keys)
            {
                var surface=surfaces[name]; surface.Children.Clear();
                surface.Width=canvasWidth;surface.Height=canvasHeight;overlays[name].Width=canvasWidth;overlays[name].Height=canvasHeight;
                surface.Children.Add(new System.Windows.Shapes.Path {
                    Data=Geometry.Parse(FormattableString.Invariant($"M {canvasWidth/2d},0 L {canvasWidth/2d},{canvasHeight} M 0,{canvasHeight/2d} L {canvasWidth},{canvasHeight/2d} M 0,{canvasHeight/4d} L {canvasWidth},{canvasHeight/4d} M 0,{canvasHeight*3/4d} L {canvasWidth},{canvasHeight*3/4d}")),
                    Stroke=new SolidColorBrush(System.Windows.Media.Color.FromRgb(220,227,235)), StrokeThickness=1,
                    StrokeDashArray=new DoubleCollection {5,5}, IsHitTestVisible=false });
                foreach (var layer in drawingViews[name].AsEnumerable().Reverse())
                {
                    layer.Ink.Width=canvasWidth;layer.Ink.Height=canvasHeight;
                    layer.Ink.Visibility=layer.Visible ? Visibility.Visible : Visibility.Hidden;
                    layer.Ink.Opacity=layer.Opacity; surface.Children.Add(layer.Ink);
                }
                surface.Children.Add(overlays[name]);
            }
        }
        finally { syncing=false; }
        RefreshOppositeShadows(); ApplyTools(); RenderRig();UpdateRulerNumberInputs();UpdateMultiViewSelection();MarkOutlinePreviewDirty();LoadExtraFaceSettings();UpdateSelectionStatus();
    }
    private void LayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if(syncing) return;CancelGuidedStroke();
        if(Layers.SelectedItem is SelectionLayerRow) {SelectSelectionLayer(sender,e);return;}
        if(Layers.SelectedItem is not PaintLayer layer) return;
        bool wasSelection=SelectionEditing;activeLayers[ActiveView]=layer.Id;lastPaintLayers[ActiveView]=layer.Id;
        if(wasSelection && Tool.SelectedIndex==8) Tool.SelectedIndex=0;RefreshEditor();
    }
    private void AddLayer(object sender, RoutedEventArgs e)
    {
        var list=drawingViews[ActiveView];
        if (list.Count>=32) { Status.Text="This view already has 32 layers."; return; }
        var kind=(LayerKind)Math.Max(0,NewLayerKind.SelectedIndex);
        var layer=Attach(new PaintLayer { Kind=kind, Name=$"{(kind==LayerKind.Artwork ? "Outline" : kind==LayerKind.BaseColor ? "Color" : kind.ToString())} {list.Count+1}" });
        list.Insert(0,layer); activeLayers[ActiveView]=layer.Id; RecordChange(); RefreshEditor();
    }
    private void DuplicateLayer(object sender, RoutedEventArgs e)
    {
        if(SelectionEditing) {Status.Text="Each face has one selection layer. Use Add to expand its mask.";return;}
        var source=ActiveLayer!; var list=drawingViews[ActiveView];
        if (list.Count>=32) return;
        var data=DrawingDocument.Data(source) with { Id=Guid.NewGuid().ToString("N"), Name=(source.Name.Length>70 ? source.Name[..70] : source.Name)+" copy", Locked=false };
        var layer=Attach(DrawingDocument.Layer(data)); list.Insert(list.IndexOf(source),layer); activeLayers[ActiveView]=layer.Id; RecordChange(); RefreshEditor();
    }
    private void DeleteLayer(object sender, RoutedEventArgs e)
    {
        if(SelectionEditing) {ClearSelection(sender,e);return;}
        var list=drawingViews[ActiveView]; if (list.Count<=1) { Status.Text="Keep at least one layer in each view."; return; }
        if (MessageBox.Show(this,$"Delete {ActiveLayer!.Name}?", "Delete layer", MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        list.Remove(ActiveLayer); activeLayers[ActiveView]=list[0].Id; RecordChange(); RefreshEditor();
    }
    private void RenameLayer(object sender, RoutedEventArgs e)
    {
        if(SelectionEditing) return;
        var name=LayerName.Text.Trim(); if (name.Length is <1 or >80) { Status.Text="Layer names need 1–80 characters."; return; }
        ActiveLayer!.Name=name; RecordChange(); RefreshEditor();
    }
    private void MoveLayer(int offset)
    {
        if(SelectionEditing) return;
        var list=drawingViews[ActiveView]; var index=list.IndexOf(ActiveLayer!); var next=index+offset;
        if (next<0 || next>=list.Count) return;
        var layer=list[index]; list.RemoveAt(index); list.Insert(next,layer); RecordChange(); RefreshEditor();
    }
    private void MoveLayerUp(object sender, RoutedEventArgs e) => MoveLayer(-1);
    private void MoveLayerDown(object sender, RoutedEventArgs e) => MoveLayer(1);
    private void LayerFlagsChanged(object sender, RoutedEventArgs e)
    {
        if (syncing || SelectionEditing || ActiveLayer==null) return;
        ActiveLayer.Visible=LayerVisible.IsChecked==true; ActiveLayer.Locked=LayerLocked.IsChecked==true; RecordChange(); RefreshEditor();
    }
    private void LayerOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (syncing || SelectionEditing || ActiveLayer==null) return;
        ActiveLayer.Opacity=e.NewValue; ActiveLayer.Ink.Opacity=e.NewValue; Layers.Items.Refresh();
    }
    private void OpacityCommit(object sender, RoutedEventArgs e) { if (!syncing) {RecordChange();RefreshOppositeShadows();} }
    private void ClearLayer(object sender, RoutedEventArgs e)
    {
        if(SelectionEditing) {ClearSelection(sender,e);return;}
        if (ActiveLayer is not {Locked:false} layer) { Status.Text="Unlock the layer to clear it."; return; }
        if ((layer.Ink.Strokes.Count>0 || layer.Photo!=null) && MessageBox.Show(this,$"Clear {layer.Name}?", "Clear layer", MessageBoxButton.YesNo)==MessageBoxResult.Yes)
        {
            syncing=true;try {layer.Ink.Strokes.Clear();layer.ImageData=null;} finally {syncing=false;}
            RecordChange();RefreshEditor();
        }
    }
    private void ToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (drawingViews.Count<6 || syncing) return;
        RestoreToolSettings();
        if (Tool.SelectedIndex is 2 or 3) ShowRig.IsChecked=true;
        ApplyTools(); RenderRig();
    }
    private void BrushChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { RememberToolSettings(); ApplyTools(); }
    private void BrushColorChanged(object sender, RoutedEventArgs e) => ApplyTools();
    private void PickColor(object sender, RoutedEventArgs e) { SetPaintColor((Color)ColorConverter.ConvertFromString(((Button)sender).Tag.ToString()!)); }
    private void ApplyTools()
    {
        if (Tool==null || BrushSize==null || BrushHex==null || BrushOpacity==null || PropertyValue==null || drawingViews.Count<6) return;
        System.Windows.Media.Color color;
        try { color=(System.Windows.Media.Color)ColorConverter.ConvertFromString(BrushHex.Text.Trim()); }
        catch { if (Status!=null) Status.Text="Use a color such as #E95164."; return; }
        foreach (var pair in drawingViews)
        {
            foreach (var layer in pair.Value)
            {
                var previewKind=Tool.SelectedIndex is 2 or 3 ? LayerKind.Artwork : ActiveLayer?.Kind ?? LayerKind.Artwork;
                layer.Ink.Visibility=layer.Visible && (layer.Kind==LayerKind.Reference || layer.Kind==LayerKind.Artwork || layer.Kind==previewKind || (previewKind==LayerKind.Artwork && layer.Kind==LayerKind.BaseColor)) ? Visibility.Visible : Visibility.Hidden;
                layer.Ink.IsHitTestVisible=!SelectionEditing && Tool.SelectedIndex==1 && !selections.ContainsKey(pair.Key) && pair.Key==ActiveView && layer.Id==ActiveLayer?.Id && layer.Visible && !layer.Locked && layer.Opacity>0;
                layer.Ink.EditingMode=Tool.SelectedIndex==1 ? InkCanvasEditingMode.EraseByPoint : InkCanvasEditingMode.None;
                var layerColor=color;
                if (layer.Kind is LayerKind.Roughness or LayerKind.Metallic or LayerKind.Height)
                {
                    var value=(byte)Math.Round(PropertyValue.Value*255); layerColor=System.Windows.Media.Color.FromRgb(value,value,value);
                }
                layerColor.A=(byte)Math.Round(BrushOpacity.Value*255);
                layer.Ink.EraserShape=new EllipseStylusShape(BrushSize.Value,BrushSize.Value);
                layer.Ink.DefaultDrawingAttributes=new DrawingAttributes {Color=layerColor, Width=BrushSize.Value, Height=BrushSize.Value, FitToCurve=true};
            }
            overlays[pair.Key].Background=!SelectionEditing && Tool.SelectedIndex==1 && ActiveLayer?.Photo==null && !selections.ContainsKey(ActiveView) && !selectedFaces.Any(f=>f!=ActiveView) ? null : Brushes.Transparent;
            overlays[pair.Key].IsHitTestVisible=pair.Key==ActiveView && (Tool.SelectedIndex==9 ||  (Tool.SelectedIndex is 0 or 1 && RulerMode.SelectedIndex>0) ||Tool.SelectedIndex is 2 or 3 || ((Tool.SelectedIndex==8 || Tool.SelectedIndex==0 || Tool.SelectedIndex>=4 || (Tool.SelectedIndex==1 && (SelectionEditing || ActiveLayer?.Photo!=null || selections.ContainsKey(ActiveView) || selectedFaces.Any(f=>f!=ActiveView)))) && (SelectionEditing || Tool.SelectedIndex==8 || PositionRuler.IsChecked==true || ActiveLayer is {Locked:false,Visible:true,Opacity:>0})));
        }
        UpdatePaintControls(color); UpdateContextSettings();
        ToolHint.Text=Tool.SelectedIndex==9 ? "Drag along a straight outline edge, tap a sharp corner, or erase a mark. Orange marks guide geometry and stay out of textures." : SelectionEditing ? "Selection layer: Brush adds highlighted areas; Eraser removes them; Select makes shapes. Choose a paint layer to draw inside the finished mask." : Tool.SelectedIndex switch {
            8 => "Draw a selection with a rectangle, ellipse, or lasso. Paint stays inside the blue area on this face. Clear selection to paint anywhere.",
            7 => "Drag on the canvas to move the selected layer. Flip buttons mirror the selected layer. Unlock the layer first.",
            2 => "Drag from an existing joint to branch a bone. Start in empty space to create a new root branch.",
            4 => "Blur softens paint on the selected layer. Brush size controls its area; Tool strength controls the effect.",
            5 => "Smudge drags paint along your stroke on the selected layer.",
            6 => "Bucket fills a connected region on this layer. Outline + layer uses visible outlines as boundaries.",
            3 => "Drag a joint. Front/back change width and height; left/right change depth and height; top/bottom change width and depth.",
            _ => ActiveLayer?.Kind switch {
                LayerKind.Roughness => "0 = smooth; 1 = rough. Paint with Property value.",
                LayerKind.Metallic => "0 = nonmetal; 1 = metal. Paint with Property value.",
                LayerKind.Height => "0.5 = neutral bump; lower = indent; higher = raised. This changes shading, not geometry.",
                LayerKind.Emission => "Paint glowing areas with Color. Black emits no light.",
                LayerKind.BaseColor => "Paint surface colors. These are baked to a UV texture.",
                _ => "Outline defines shape. Draw closed outlines, then choose Color to paint underneath." }};
    }
    private void OutputChanged(object sender, SelectionChangedEventArgs e) { if (!syncing) RecordChange(); }
    private void SaveProject(object sender, RoutedEventArgs e) => SaveDrawing(false);
    private void SaveDrawingCopy(object sender, RoutedEventArgs e) => SaveDrawing(true);
    private void OpenProject(object sender, RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Filter="MeshDraw project|*.meshdraw",InitialDirectory=DrawingLibraryFolder};
        if(dialog.ShowDialog(this)==true) OpenDrawingFile(dialog.FileName);
    }
    private void WriteComposite(string path, IEnumerable<PaintLayer> layers, bool transparent)
    {
        var visual=new DrawingVisual();
        using(var context=visual.RenderOpen())
        {
            if (!transparent) context.DrawRectangle(Brushes.White,null,new Rect(0,0,canvasWidth,canvasHeight));
            foreach(var layer in layers.Where(l=>l.Visible && l.Opacity>0).Reverse())
            {
                context.PushOpacity(layer.Opacity); layer.Draw(context,canvasWidth,canvasHeight); context.Pop();
            }
        }
        var bitmap=new RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(path); png.Save(file);
    }
    private void WriteDrawings(string folder)
    {
        foreach(var pair in drawingViews)
        {
            var view=pair.Key.ToLowerInvariant();
            WriteComposite(Path.Combine(folder,view+".png"),pair.Value.Where(l=>l.Kind==LayerKind.Artwork),false);
            foreach(var kind in Enum.GetValues<LayerKind>().Where(k=>k!=LayerKind.Artwork && k!=LayerKind.Reference))
                WriteComposite(Path.Combine(folder,$"{view}_{kind.ToString().ToLowerInvariant()}.png"),pair.Value.Where(l=>l.Kind==kind),true);
        }
        File.WriteAllText(Path.Combine(folder,"skeleton.json"),JsonSerializer.Serialize(rig));
        File.WriteAllText(Path.Combine(folder,"project.meshdraw"),Snapshot());
        File.WriteAllText(Path.Combine(folder,"manifest.json"),JsonSerializer.Serialize(new {version=2, outputType=Output.SelectedIndex==0 ? "static" : "skeletal", width=canvasWidth, height=canvasHeight, textureChannels=new[] {"basecolor","roughness","metallic","emission","height"}, rigCoordinates="normalized Hunyuan axes: X right, Y up, Z depth"}));
    }
    private void ExportDrawings(object sender, RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog {Title="Choose a folder for the layered export"}; if(dialog.ShowDialog(this)!=true) return;
        try
        {
            // Export to a unique subfolder so existing project files are never overwritten.
            var folder=Path.Combine(dialog.FolderName,"MeshDraw-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(folder); WriteDrawings(folder); Status.Text="Exported artwork, five material channels per view, the skeleton, and editable project to "+folder;
        }
        catch(Exception error) {ShowError(error);}
    }
    private void ShowError(Exception error) => MessageBox.Show(this,error.Message,"MeshDraw",MessageBoxButton.OK,MessageBoxImage.Error);
}
