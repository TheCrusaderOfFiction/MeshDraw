using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeshDraw;
public partial class EditorWindow
{
    private void OpenFaceCopy(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();var source=ActiveView;
        var window=new Window {Title="Copy face · "+FaceName(source),Owner=this,Width=460,Height=620,MinWidth=400,MinHeight=480,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(17,21,29)),Foreground=Brushes.White};
        window.Resources.MergedDictionaries.Add(Resources);
        var root=new Grid {Margin=new Thickness(20)};root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});window.Content=root;
        var header=new StackPanel();header.Children.Add(new TextBlock {Text="Copy "+FaceName(source)+" to…",FontSize=22,TextWrapping=TextWrapping.Wrap});
        header.Children.Add(new TextBlock {Text="Copies outline, color, texture, and photo layers.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16)});root.Children.Add(header);
        var choices=new StackPanel();var targets=new List<CheckBox>();
        foreach(var face in drawingViews.Keys.Where(f=>f!=source))
        {
            var check=new CheckBox {Content=FaceName(face),Tag=face,IsChecked=face==OppositeFace(source),Margin=new Thickness(0,5,0,5)};targets.Add(check);choices.Children.Add(check);
        }
        var scroll=new ScrollViewer {Content=choices,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var bottom=new StackPanel();Grid.SetRow(bottom,2);root.Children.Add(bottom);
        var mode=new ComboBox {ItemsSource=new[]{"Add copied layers", "Replace destination layers"},SelectedIndex=0};bottom.Children.Add(mode);
        var mirror=new CheckBox {Content="Flip copied layers horizontally"};bottom.Children.Add(mirror);
        var mask=new CheckBox {Content="Also copy the selection mask"};bottom.Children.Add(mask);
        var marks=new CheckBox {Content="Also copy edge marks",IsChecked=true};bottom.Children.Add(marks);
        var message=new TextBlock {Text="Add keeps existing layers. Replace overwrites their contents; Undo restores them.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};bottom.Children.Add(message);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);
        var cancel=new Button {Content="Cancel"};cancel.Click+=(_,_)=>window.DialogResult=false;buttons.Children.Add(cancel);
        var copy=new Button {Content="Copy"};buttons.Children.Add(copy);
        copy.Click+=(_,_)=>
        {
            try {CopyFace(source,targets.Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray(),mode.SelectedIndex==1,mask.IsChecked==true,mirror.IsChecked==true,marks.IsChecked==true);window.DialogResult=true;}
            catch(Exception error) {message.Text=error.Message;}
        };
        window.ShowDialog();
    }
    private void CopyFace(string source,IReadOnlyList<string> destinations,bool replace,bool copySelection,bool mirror,bool copyMarks=true)
    {
        if(!drawingViews.ContainsKey(source) || destinations.Count==0 || destinations.Distinct().Count()!=destinations.Count || destinations.Any(f=>f==source || !drawingViews.ContainsKey(f)))
            throw new InvalidOperationException("Choose at least one other face.");
        var document=Document();var sourceLayers=document.Layers[source];
        foreach(var target in destinations)
        {
            if(replace && document.Layers[target].Any(l=>l.Locked)) throw new InvalidOperationException("Unlock the layers on "+FaceName(target)+" before replacing them.");
            if(copyMarks && !replace && (document.EdgeGuides!.GetValueOrDefault(target)?.Count ?? 0)+(document.EdgeGuides!.GetValueOrDefault(source)?.Count ?? 0)>128) throw new InvalidOperationException(FaceName(target)+" would exceed 128 edge marks.");
            if(!replace && sourceLayers.Count+document.Layers[target].Count>32) throw new InvalidOperationException(FaceName(target)+" would exceed 32 layers. Choose Replace or remove some layers.");
        }
        var matrix=new Matrix(-1,0,0,1,canvasWidth,0);
        foreach(var target in destinations)
        {
            var copied=new List<LayerData>();
            foreach(var data in sourceLayers)
            {
                var clone=data with {Id=Guid.NewGuid().ToString("N")};
                if(mirror) {var layer=DrawingDocument.Layer(clone);TransformLayer(layer,matrix);clone=DrawingDocument.Data(layer);}
                copied.Add(clone);
            }
            if(copyMarks)
            {
                var guides=(document.EdgeGuides!.GetValueOrDefault(source) ?? []).Select(g=>g with {Id=Guid.NewGuid().ToString("N"),X1=mirror ? canvasWidth-g.X1 : g.X1,X2=mirror ? canvasWidth-g.X2 : g.X2,Control1X=mirror && g.Control1X!=null ? canvasWidth-g.Control1X : g.Control1X,Control2X=mirror && g.Control2X!=null ? canvasWidth-g.Control2X : g.Control2X,LinkGroup=null,LockGroup=null,PositionLocked=false,CornerGroup=null}).ToList();
                var original=document.EdgeGuides!.GetValueOrDefault(source) ?? [];
                var ids=original.Select((g,i)=>(g.Id,newId:guides[i].Id)).ToDictionary(g=>g.Id,g=>g.newId);
                copied=copied.Select(data=> {var strokes=DrawingDocument.Decode(data.Strokes);bool changed=false;foreach(var stroke in strokes)if(stroke.ContainsPropertyData(PaintedGuideId) && ids.TryGetValue((string)stroke.GetPropertyData(PaintedGuideId),out var id)) {stroke.RemovePropertyData(PaintedGuideId);stroke.AddPropertyData(PaintedGuideId,id);changed=true;}return changed?data with {Strokes=DrawingDocument.Encode(strokes)}:data;}).ToList();
                document.EdgeGuides![target]=replace ? guides : guides.Concat(document.EdgeGuides!.GetValueOrDefault(target) ?? []).ToList();
            }
            document.Layers[target]=replace ? copied : copied.Concat(document.Layers[target]).ToList();
            if(copySelection)
            {
                if(document.Selections!.TryGetValue(source,out var selection))
                {
                    if(mirror) {var geometry=Geometry.Parse(selection).Clone();geometry.Transform=new MatrixTransform(matrix);selection=geometry.GetFlattenedPathGeometry().ToString(CultureInfo.InvariantCulture);}
                    document.Selections[target]=selection;
                }
                else document.Selections.Remove(target);
            }
        }
        DrawingDocument.Validate(document);ApplyDocument(document);RecordChange();
        Status.Text="Copied "+FaceName(source)+" to "+destinations.Count+" face"+(destinations.Count==1?".":"s.")+" Undo restores the previous layers.";
    }
}
