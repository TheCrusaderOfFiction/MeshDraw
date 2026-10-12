using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeshDraw;

public partial class EditorWindow
{
    internal static Point ProjectJoint(JointData joint,string view,int width=640,int height=640) => view switch {
        "Front" => new(joint.X*width,(1-joint.Y)*height), "Back" => new((1-joint.X)*width,(1-joint.Y)*height),
        "Left" => new(joint.Z*width,(1-joint.Y)*height), "Right" => new((1-joint.Z)*width,(1-joint.Y)*height),
        "Top" => new(joint.X*width,joint.Z*height), "Bottom" => new(joint.X*width,(1-joint.Z)*height),
        _ => throw new ArgumentException("Unknown view") };
    internal static JointData MoveJoint(JointData joint,Point point,string view,int width=640,int height=640)
    {
        var u=Math.Clamp(point.X/width,0,1); var v=Math.Clamp(point.Y/height,0,1);
        return view switch {
            "Front" => joint with {X=u,Y=1-v}, "Back" => joint with {X=1-u,Y=1-v},
            "Left" => joint with {Z=u,Y=1-v}, "Right" => joint with {Z=1-u,Y=1-v},
            "Top" => joint with {X=u,Z=v}, "Bottom" => joint with {X=u,Z=1-v},
            _ => throw new ArgumentException("Unknown view") };
    }
    private JointData? ClosestJoint(Point point)
    {
        return rig.Joints.Select(j => new {Joint=j, Distance=(ProjectJoint(j,ActiveView,canvasWidth,canvasHeight)-point).Length})
            .Where(p=>p.Distance<=15).OrderBy(p=>p.Distance).Select(p=>p.Joint).FirstOrDefault();
    }
    private void RigMouseDown(object sender,MouseButtonEventArgs e)
    {
        if (Tool.SelectedIndex is not (2 or 3)) return;
        if(SelectionEditing) {Status.Text="Choose a paint layer before editing the skeleton.";return;}
        if(extraFaces.Any(f=>f.Id==ActiveView)) {Status.Text="Edit skeleton joints in the six main views.";return;}
        var canvas=(Canvas)sender; var point=e.GetPosition(canvas); var nearest=ClosestJoint(point);
        dragBefore=Snapshot();
        if (Tool.SelectedIndex==2)
        {
            if (rig.Bones.Count>=128) {Status.Text="The skeleton already has 128 bones."; return;}
            boneStart=nearest ?? MoveJoint(new JointData(Guid.NewGuid().ToString("N"),.5,.5,.5),point,ActiveView,canvasWidth,canvasHeight);
            dragPoint=point;
        }
        else
        {
            if (nearest==null) return;
            dragJoint=nearest.Id; selectedBone=rig.Bones.FirstOrDefault(b=>b.Tail==nearest.Id || b.Head==nearest.Id)?.Id;
        }
        canvas.CaptureMouse(); RenderRig(); e.Handled=true;
    }
    private void RigMouseMove(object sender,MouseEventArgs e)
    {
        if (e.LeftButton!=MouseButtonState.Pressed || !((Canvas)sender).IsMouseCaptured) return;
        var point=e.GetPosition((Canvas)sender);
        if (dragJoint!=null)
        {
            var index=rig.Joints.FindIndex(j=>j.Id==dragJoint);
            if(index>=0) rig.Joints[index]=MoveJoint(rig.Joints[index],point,ActiveView,canvasWidth,canvasHeight);
        }
        if (boneStart!=null) dragPoint=point;
        RenderRig(); e.Handled=true;
    }
    private void RigMouseUp(object sender,MouseButtonEventArgs e)
    {
        var canvas=(Canvas)sender; if(!canvas.IsMouseCaptured) return;
        canvas.ReleaseMouseCapture();
        if(boneStart!=null)
        {
            var point=e.GetPosition(canvas);
            if((ProjectJoint(boneStart,ActiveView,canvasWidth,canvasHeight)-point).Length>=6)
            {
                var tail=MoveJoint(boneStart with {Id=Guid.NewGuid().ToString("N")},point,ActiveView,canvasWidth,canvasHeight);
                if(!rig.Joints.Any(j=>j.Id==boneStart.Id)) rig.Joints.Add(boneStart);
                rig.Joints.Add(tail);
                var parent=rig.Bones.FirstOrDefault(b=>b.Tail==boneStart.Id);
                var name="bone_"+(rig.Bones.Count+1);
                while(rig.Bones.Any(b=>b.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) name+="_new";
                var bone=new BoneData(Guid.NewGuid().ToString("N"),name,boneStart.Id,tail.Id,parent?.Id);
                rig.Bones.Add(bone); selectedBone=bone.Id;
            }
        }
        boneStart=null; dragJoint=null; dragPoint=null;
        try {DrawingDocument.ValidateRig(rig); RecordChange(); RefreshEditor();}
        catch(Exception error)
        {
            if(dragBefore!=null) ApplyDocument(DrawingDocument.Read(dragBefore));
            Status.Text=error.Message;
        }
        dragBefore=null; e.Handled=true;
    }
    private void RenderRig()
    {
        if(ShowRig==null) return;
        if(ShowCrossFaceEdges?.IsChecked==true)PrepareCrossFaceEdges();
        var joints=rig.Joints.ToDictionary(j=>j.Id);
        foreach(var pair in overlays)
        {
            var canvas=pair.Value; canvas.Children.Clear();
            canvas.Visibility=Visibility.Visible;
            RenderSelection(canvas,pair.Key);
            RenderCrossFaceEdges(canvas,pair.Key);
            RenderEdgeGuides(canvas,pair.Key);
            foreach(var bone in rig.Bones.Where(_=>ShowRig.IsChecked==true && !extraFaces.Any(f=>f.Id==pair.Key)))
            {
                var a=ProjectJoint(joints[bone.Head],pair.Key,canvasWidth,canvasHeight); var b=ProjectJoint(joints[bone.Tail],pair.Key,canvasWidth,canvasHeight);
                var color=bone.Id==selectedBone ? Brushes.Goldenrod : Brushes.MediumTurquoise;
                canvas.Children.Add(new Line {X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=color,StrokeThickness=4,IsHitTestVisible=false});
                var label=new TextBlock {Text=bone.Name, FontSize=10, Foreground=Brushes.DarkSlateGray, Background=new SolidColorBrush(Color.FromArgb(180,255,255,255)), IsHitTestVisible=false};
                Canvas.SetLeft(label,(a.X+b.X)/2+5); Canvas.SetTop(label,(a.Y+b.Y)/2+3); canvas.Children.Add(label);
            }
            foreach(var joint in rig.Joints.Where(_=>ShowRig.IsChecked==true && !extraFaces.Any(f=>f.Id==pair.Key)))
            {
                var point=ProjectJoint(joint,pair.Key,canvasWidth,canvasHeight);
                var circle=new Ellipse {Width=10,Height=10,Fill=Brushes.DarkSlateBlue,Stroke=Brushes.White,StrokeThickness=1,IsHitTestVisible=false};
                Canvas.SetLeft(circle,point.X-5); Canvas.SetTop(circle,point.Y-5); canvas.Children.Add(circle);
            }
            RenderBrushGuides(canvas,pair.Key);
            if(pair.Key==ActiveView && boneStart!=null && dragPoint!=null)
            {
                var a=ProjectJoint(boneStart,pair.Key,canvasWidth,canvasHeight); var b=dragPoint.Value;
                canvas.Children.Add(new Line {X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=Brushes.Goldenrod,StrokeThickness=3,StrokeDashArray=new DoubleCollection {3,3},IsHitTestVisible=false});
            }
        }
    }
    private void RigVisibilityChanged(object sender,RoutedEventArgs e) {if(!syncing) RenderRig();}
    private void BoneSelected(object sender,SelectionChangedEventArgs e)
    {
        if(syncing) return;
        selectedBone=(BoneList.SelectedItem as BoneData)?.Id;
        BoneName.Text=(BoneList.SelectedItem as BoneData)?.Name ?? ""; RenderRig();
    }
    private void RenameBone(object sender,RoutedEventArgs e)
    {
        var index=rig.Bones.FindIndex(b=>b.Id==selectedBone); if(index<0) return;
        var previous=rig.Bones[index]; rig.Bones[index]=previous with {Name=BoneName.Text.Trim()};
        try {DrawingDocument.ValidateRig(rig); RecordChange(); RefreshEditor();}
        catch(Exception error) {rig.Bones[index]=previous; ShowError(error);}
    }
    private void DeleteBone(object sender,RoutedEventArgs e)
    {
        if(selectedBone==null) return;
        if(MessageBox.Show(this,"Delete the selected bone and all of its child bones?", "Delete bones",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        var removed=new HashSet<string> {selectedBone}; bool changed;
        do {changed=false; foreach(var bone in rig.Bones) if(bone.Parent!=null && removed.Contains(bone.Parent)) changed|=removed.Add(bone.Id);} while(changed);
        rig.Bones.RemoveAll(b=>removed.Contains(b.Id));
        var used=rig.Bones.SelectMany(b=>new[] {b.Head,b.Tail}).ToHashSet(); rig.Joints.RemoveAll(j=>!used.Contains(j.Id));
        selectedBone=null; RecordChange(); RefreshEditor();
    }
    private void LoadHumanoid(object sender,RoutedEventArgs e)
    {
        if(rig.Bones.Count>0 && MessageBox.Show(this,"Replace the current skeleton with an editable humanoid guide?", "Humanoid guide",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        skeletonType="Humanoid";rig=HumanoidGuide(); selectedBone=null; ShowRig.IsChecked=true; RecordChange(); RefreshEditor();
    }
    internal static RigData HumanoidGuide()
    {
        var result=new RigData([],[]); var ends=new Dictionary<string,JointData>(); var ids=new Dictionary<string,string>();
        void Add(string name, double[] a,double[] b,string? parent)
        {
            var head=parent!=null ? ends[parent] : new JointData(Guid.NewGuid().ToString("N"),a[0],a[1],a[2]);
            if(parent==null) result.Joints.Add(head);
            var tail=new JointData(Guid.NewGuid().ToString("N"),b[0],b[1],b[2]); result.Joints.Add(tail);
            var id=Guid.NewGuid().ToString("N"); result.Bones.Add(new(id,name,head.Id,tail.Id,parent!=null ? ids[parent] : null)); ends[name]=tail; ids[name]=id;
        }
        Add("pelvis", [.5,.48,.5],[.5,.56,.5],null);
        Add("spine_01",[],[.5,.67,.5],"pelvis"); Add("spine_02",[],[.5,.80,.5],"spine_01");
        Add("neck",[],[.5,.87,.5],"spine_02"); Add("head",[],[.5,.98,.5],"neck");
        foreach(var (side,sign) in new[] {("l",1),("r",-1)})
        {
            Add($"clavicle_{side}",[],[.5+.10*sign,.80,.5],"spine_02");
            Add($"upperarm_{side}",[],[.5+.25*sign,.80,.5],$"clavicle_{side}");
            Add($"lowerarm_{side}",[],[.5+.38*sign,.80,.5],$"upperarm_{side}");
            Add($"hand_{side}",[],[.5+.47*sign,.80,.5],$"lowerarm_{side}");
            // Legs branch from pelvis tail; positions are editable in every view.
            Add($"thigh_{side}",[],[.5+.07*sign,.27,.5],"pelvis");
            Add($"calf_{side}",[],[.5+.07*sign,.045,.5],$"thigh_{side}");
            Add($"foot_{side}",[],[.5+.07*sign,.025,.35],$"calf_{side}");
        }
        DrawingDocument.ValidateRig(result); return result;
    }
}
