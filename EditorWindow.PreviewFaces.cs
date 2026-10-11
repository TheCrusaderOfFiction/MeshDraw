using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private readonly Dictionary<GeometryModel3D,(string face,bool border)> previewFaceModels=new();
    private FaceDefinition? previewFaceStart;
    private Point previewFaceDragStart;
    private bool loadingPreviewFaces;
    private FaceDefinition? SelectedPreviewFace=>extraFaces.FirstOrDefault(f=>f.Id==(string?)PreviewFace.SelectedValue);
    private void LoadPreviewFaces()
    {
        if(PreviewFace==null)return;
        var id=(string?)PreviewFace.SelectedValue;loadingPreviewFaces=true;
        PreviewFace.ItemsSource=extraFaces.ToArray();PreviewFace.SelectedValue=extraFaces.Any(f=>f.Id==id)?id:extraFaces.FirstOrDefault()?.Id;
        loadingPreviewFaces=false;LoadPreviewFaceValues();
    }
    private void PreviewFaceSelected(object sender,SelectionChangedEventArgs e) {if(!loadingPreviewFaces)LoadPreviewFaceValues();}
    private void LoadPreviewFaceValues()
    {
        if(PreviewFaceX==null)return;var face=SelectedPreviewFace;
        var boxes=new[]{PreviewFaceX,PreviewFaceY,PreviewFaceZ,PreviewFaceYaw,PreviewFacePitch,PreviewFaceRoll};
        var values=new[]{face?.MoveX ?? 0,face?.MoveY ?? 0,face?.MoveZ ?? 0,face?.Yaw ?? 0,face?.Pitch ?? 0,face?.Roll ?? 0};
        for(int i=0;i<boxes.Length;i++){boxes[i].IsEnabled=face!=null;boxes[i].Text=values[i].ToString("0.###",CultureInfo.InvariantCulture);}
    }
    private void SetPreviewFace(FaceDefinition face,bool commit)
    {
        int index=extraFaces.FindIndex(f=>f.Id==face.Id);if(index<0)return;
        DrawingDocument.Validate(Document() with {ExtraFaces=extraFaces.Select(f=>f.Id==face.Id?face:f).ToList()});
        extraFaces[index]=face;LoadPreviewFaceValues();LoadExtraFaceSettings();
        foreach(var pair in previewFaceModels.Where(p=>p.Value.face==face.Id))
        {
            var mesh=new MeshGeometry3D();
            if(pair.Value.border){double t=.006;foreach(var (u,v,w,h) in new[]{(0d,0d,1d,t),(0d,1-t,1d,1d),(0d,0d,t,1d),(1-t,0d,1d,1d)})AddQuad(mesh,FacePlanePoint(face.Id,u,v),FacePlanePoint(face.Id,w,v),FacePlanePoint(face.Id,w,h),FacePlanePoint(face.Id,u,h));}
            else {AddQuad(mesh,FacePlanePoint(face.Id,0,0),FacePlanePoint(face.Id,1,0),FacePlanePoint(face.Id,1,1),FacePlanePoint(face.Id,0,1));foreach(var p in new[]{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)})mesh.TextureCoordinates.Add(p);}
            pair.Key.Geometry=mesh;
        }
        if(commit){RecordChange();RenderOutlinePreview();Status.Text="Updated custom face position and rotation.";}
    }
    private void ApplyPreviewFace(object sender,RoutedEventArgs e)
    {
        var face=SelectedPreviewFace;if(face==null)return;
        var boxes=new[]{PreviewFaceX,PreviewFaceY,PreviewFaceZ,PreviewFaceYaw,PreviewFacePitch,PreviewFaceRoll};var values=new double[6];
        for(int i=0;i<6;i++)if(!double.TryParse(boxes[i].Text,NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]) || !double.IsFinite(values[i]) || Math.Abs(values[i])>(i<3?2:i==4?90:180)){Status.Text="Position must be −2 to 2; yaw/roll −180 to 180°; pitch −90 to 90°.";return;}
        SetPreviewFace(face with {MoveX=values[0],MoveY=values[1],MoveZ=values[2],Yaw=values[3],Pitch=values[4],Roll=values[5]},true);
    }
    private void ResetPreviewFace(object sender,RoutedEventArgs e) {if(SelectedPreviewFace is {} face)SetPreviewFace(face with {MoveX=0,MoveY=0,MoveZ=0,Yaw=0,Pitch=0,Roll=0,Offset=0},true);}
    private bool BeginPreviewFaceDrag(MouseButtonEventArgs e)
    {
        if(PreviewFaceMode.SelectedIndex==0)return false;
        // Clicking a custom plane selects it; the dropdown works even if hidden.
        VisualTreeHelper.HitTest(OutlineViewport,null,result=>{if(result is RayMeshGeometry3DHitTestResult hit && hit.ModelHit is GeometryModel3D model && previewFaceModels.TryGetValue(model,out var entry) && extraFaces.Any(f=>f.Id==entry.face)){PreviewFace.SelectedValue=entry.face;return HitTestResultBehavior.Stop;}return HitTestResultBehavior.Continue;},new PointHitTestParameters(e.GetPosition(OutlineViewport)));
        if(SelectedPreviewFace is not {} face)return true;
        ++previewRequest;PreviewLoadingOverlay.Visibility=Visibility.Collapsed;PreviewRefreshButton.IsEnabled=true;
        OutlineViewport.Focus();previewFaceStart=face;previewFaceDragStart=e.GetPosition(OutlineViewport);OutlineViewport.CaptureMouse();e.Handled=true;return true;
    }
    private bool MovePreviewFaceDrag(MouseEventArgs e)
    {
        if(previewFaceStart is not {} face)return false;if(e.LeftButton!=MouseButtonState.Pressed){EndPreviewFaceDrag();return true;}
        var delta=e.GetPosition(OutlineViewport)-previewFaceDragStart;
        double scale=2*previewDistance*Math.Tan(Math.PI/8)/Math.Max(1,OutlineViewport.ActualHeight);
        SetPreviewFace(TransformPreviewFace(face,delta,PreviewFaceMode.SelectedIndex,PreviewFaceAxis.SelectedIndex,scale),false);e.Handled=true;return true;
    }
    private FaceDefinition TransformPreviewFace(FaceDefinition face,Vector delta,int mode,int axis,double scale)
    {
        if(mode==1)
        {
            var movement=axis switch {1=>new Vector3D(delta.X*scale,0,0),2=>new Vector3D(0,-delta.Y*scale,0),3=>new Vector3D(0,0,delta.X*scale),_=>CameraMovement(delta,scale)};
            return face with {MoveX=Math.Clamp(face.MoveX+movement.X,-2,2),MoveY=Math.Clamp(face.MoveY+movement.Y,-2,2),MoveZ=Math.Clamp(face.MoveZ+movement.Z,-2,2)};
        }
        return face with {Yaw=WrapAngle(face.Yaw+(axis is 0 or 1?delta.X*.5:0)),Pitch=Math.Clamp(face.Pitch-(axis is 0 or 2?delta.Y*.5:0),-90,90),Roll=WrapAngle(face.Roll+(axis==3?delta.X*.5:0))};
    }
    private Vector3D CameraMovement(Vector delta,double scale)
    {
        double y=previewYaw*Math.PI/180,p=previewPitch*Math.PI/180;
        return new Vector3D(Math.Cos(y),0,-Math.Sin(y))*(delta.X*scale)+new Vector3D(-Math.Sin(y)*Math.Sin(p),Math.Cos(p),-Math.Cos(y)*Math.Sin(p))*(-delta.Y*scale);
    }
    private static double WrapAngle(double value){var v=(value+180)%360;return (v<0?v+360:v)-180;}
    private void PreviewFaceCaptureLost(object sender,MouseEventArgs e)=>EndPreviewFaceDrag();
    private void PreviewFaceKey(object sender,KeyEventArgs e){if(e.Key==Key.Escape && previewFaceStart is {} start){previewFaceStart=null;SetPreviewFace(start,false);OutlineViewport.ReleaseMouseCapture();RenderOutlinePreview();e.Handled=true;}}
    private void EndPreviewFaceDrag(){if(previewFaceStart is not {} start)return;previewFaceStart=null;OutlineViewport.ReleaseMouseCapture();if(extraFaces.FirstOrDefault(f=>f.Id==start.Id)!=start){RecordChange();RenderOutlinePreview();}}
}
