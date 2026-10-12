using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private readonly List<FaceDefinition> extraFaces=[];
    private string FaceName(string id)=>extraFaces.FirstOrDefault(f=>f.Id==id)?.Name ?? id;
    private void CreateFaceSurface(string name,string label)
    {
            var surface = new Grid { Width=640, Height=640, Background=Brushes.White, ClipToBounds=true };
            surface.PreviewMouseLeftButtonDown += (_,_)=>SelectMultiView(name);
            surface.PreviewMouseLeftButtonDown += SampleDrawing;
            var overlay = new Canvas { Width=640, Height=640, Background=Brushes.Transparent };
            overlay.MouseLeftButtonDown += RigMouseDown;
            overlay.MouseMove += RigMouseMove;
            overlay.MouseLeftButtonUp += RigMouseUp;
            AttachBrushInput(overlay);
            overlays[name] = overlay; surfaces[name] = surface;
            var outline = Attach(new PaintLayer { Name="Outline", Kind=LayerKind.Artwork });
            var color = Attach(new PaintLayer { Name="Color", Kind=LayerKind.BaseColor });
            drawingViews[name] = [outline, color]; activeLayers[name] = outline.Id;
            Views.Items.Add(new TabItem { Header=label, Tag=name, Content=new Viewbox { Child=surface, Stretch=Stretch.Uniform, ClipToBounds=true } });
    }
    private void LoadExtraFaceSettings()
    {
        if(ExtraFaceName==null) return;
        var face=extraFaces.FirstOrDefault(f=>f.Id==ActiveView);
        ExtraFaceApply.IsEnabled=ExtraFaceDelete.IsEnabled=face!=null;
        if(face==null) return;
        ExtraFaceName.Text=face.Name;ExtraFaceType.SelectedIndex=face.IsSection ? 1 : 0;
        ExtraFaceYaw.Text=face.Yaw.ToString(CultureInfo.InvariantCulture);ExtraFacePitch.Text=face.Pitch.ToString(CultureInfo.InvariantCulture);
        ExtraFaceOffset.Value=face.Offset;ExtraFaceSectionMode.SelectedIndex=face.SectionMode=="Sections"?0:face.SectionMode=="Blend"?1:2;ExtraFaceReverse.IsChecked=face.ReverseSection;
    }
    private FaceDefinition? ReadExtraFace(string id)
    {
        var name=ExtraFaceName.Text.Trim();
        if(name.Length is <1 or >40 || !double.TryParse(ExtraFaceYaw.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var yaw)
            || !double.IsFinite(yaw) || Math.Abs(yaw)>180 || !double.TryParse(ExtraFacePitch.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var pitch)
            || !double.IsFinite(pitch) || Math.Abs(pitch)>90)
        {Status.Text="Enter a name, a yaw from −180 to 180°, and a pitch from −90 to 90°.";return null;}
        var old=extraFaces.FirstOrDefault(f=>f.Id==id);
        return new(id,name,yaw,pitch,ExtraFaceOffset.Value,ExtraFaceType.SelectedIndex==1,old?.MoveX ?? 0,old?.MoveY ?? 0,old?.MoveZ ?? 0,old?.Roll ?? 0,ExtraFaceSectionMode.SelectedIndex==0?"Sections":ExtraFaceSectionMode.SelectedIndex==1?"Blend":"Whole",ExtraFaceReverse.IsChecked==true);
    }
    private void AddExtraFace(object sender,RoutedEventArgs e)
    {
        if(extraFaces.Count>=8) {Status.Text="You can add up to eight extra faces.";return;}
        var face=ReadExtraFace("custom_"+Guid.NewGuid().ToString("N"));if(face==null) return;
        CancelGuidedStroke();syncing=true;
        try {extraFaces.Add(face);CreateFaceSurface(face.Id,face.Name);Views.SelectedIndex=Views.Items.Count-1;InitializeFaceSelection();}
        finally {syncing=false;}
        SetViewLayout(viewLayout);RefreshEditor();RecordChange();Status.Text="Added "+face.Name+". Draw a closed outline; the 3D preview shows its angle and position.";
    }
    private void ApplyExtraFace(object sender,RoutedEventArgs e)
    {
        int index=extraFaces.FindIndex(f=>f.Id==ActiveView);if(index<0) return;
        var face=ReadExtraFace(ActiveView);if(face==null) return;
        extraFaces[index]=face;((TabItem)Views.SelectedItem).Header=face.Name;InitializeFaceSelection();SetViewLayout(viewLayout);RecordChange();
        Status.Text="Updated face angle and position.";
    }
    private void DeleteExtraFace(object sender,RoutedEventArgs e)
    {
        var face=extraFaces.FirstOrDefault(f=>f.Id==ActiveView);if(face==null) return;
        CancelGuidedStroke();var document=Document();document.Layers.Remove(face.Id);
        document.Selections?.Remove(face.Id);document.EdgeGuides?.Remove(face.Id);
        ApplyDocument(document with {ExtraFaces=extraFaces.Where(f=>f.Id!=face.Id).ToList(),Connections=document.Connections?.Where(c=>c.FaceA!=face.Id && c.FaceB!=face.Id).ToList(),LineEqualities=document.LineEqualities?.Where(r=>new[]{r.Whole.Face,r.First.Face,r.Second.Face}.All(f=>f!=face.Id)).ToList()});RecordChange();
        Status.Text="Removed "+face.Name+". Undo restores the face and its layers.";
    }
    internal static (Vector3D u,Vector3D v,Vector3D normal) FaceBasis(FaceDefinition face)
    {
        double y=face.Yaw*Math.PI/180,p=face.Pitch*Math.PI/180;
        var u=new Vector3D(Math.Cos(y),0,-Math.Sin(y));var v=new Vector3D(-Math.Sin(y)*Math.Sin(p),Math.Cos(p),-Math.Cos(y)*Math.Sin(p));
        double r=face.Roll*Math.PI/180;
        return (u*Math.Cos(r)+v*Math.Sin(r),v*Math.Cos(r)-u*Math.Sin(r),new(Math.Sin(y)*Math.Cos(p),Math.Sin(p),Math.Cos(y)*Math.Cos(p)));
    }
    private Point3D FacePlanePoint(string id,double u,double v)
    {
        var face=extraFaces.FirstOrDefault(f=>f.Id==id);if(face==null) return OutlinePlanePoint(id,u,v);
        var basis=FaceBasis(face);double radius=Math.Abs(basis.normal.X)+Math.Abs(basis.normal.Y)+Math.Abs(basis.normal.Z);
        return new Point3D(face.MoveX,face.MoveY,face.MoveZ)+basis.u*(2*u-1)+basis.v*(1-2*v)+basis.normal*radius*(face.IsSection ? face.Offset : 1);
    }
}
