using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MeshDraw;
public partial class EditorWindow
{
    private sealed record SkeletonPreset(int Version,string Name,RigData Skeleton);
    private readonly Dictionary<string,RigData> skeletonTypes=new(StringComparer.OrdinalIgnoreCase);
    private string SkeletonTypesFolder => Path.Combine(projectRoot,"skeleton-types");
    private void CreationOutputChanged(object sender,SelectionChangedEventArgs e)
    {
        if(CreationRigOptions!=null) CreationRigOptions.Visibility=NewOutputType.SelectedIndex==1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshSkeletonTypes()
    {
        skeletonTypes.Clear();skeletonTypes["Custom"]=new([],[]);skeletonTypes["Humanoid"]=HumanoidGuide();
        foreach(var name in new[]{"Quadruped","Winged","Serpent"}) skeletonTypes[name]=CreatureGuide(name);
        if(Directory.Exists(SkeletonTypesFolder))
        foreach(var path in Directory.EnumerateFiles(SkeletonTypesFolder,"*.skeleton.json"))
        {
            try
            {
                var preset=ReadSkeletonPreset(path);
                if(!skeletonTypes.ContainsKey(preset.Name)) skeletonTypes[preset.Name]=preset.Skeleton;
            }
            catch { /* An invalid template never replaces a valid preset. */ }
        }
        var names=skeletonTypes.Keys.ToList();NewSkeletonType.ItemsSource=names;NewSkeletonType.SelectedIndex=0;
        EditorSkeletonTypes.ItemsSource=names;EditorSkeletonTypes.SelectedItem=skeletonTypes.ContainsKey(skeletonType) ? skeletonType : "Custom";
    }
    private static SkeletonPreset ReadSkeletonPreset(string path)
    {
        if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException("Skeleton type file is too large.");
        var preset=JsonSerializer.Deserialize<SkeletonPreset>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty skeleton type.");
        if(preset.Version!=1 || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length>80) throw new InvalidDataException("Invalid skeleton type name.");
        DrawingDocument.ValidateRig(preset.Skeleton);return preset;
    }
    private static void WriteSkeletonPreset(string path,string name,RigData skeleton)
    {
        DrawingDocument.ValidateRig(skeleton);
        if(skeleton.Bones.Count==0) throw new InvalidDataException("Draw at least one bone before saving a skeleton type.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new SkeletonPreset(1,name,skeleton)));File.Move(temp,path,true);
    }
    private RigData GetSkeletonType(string name)
    {
        if(!skeletonTypes.TryGetValue(name,out var template)) throw new InvalidDataException("Choose a skeleton type.");
        return JsonSerializer.Deserialize<RigData>(JsonSerializer.Serialize(template))!;
    }
    private void SaveSkeletonType(object sender,RoutedEventArgs e)
    {
        if(rig.Bones.Count==0) {Status.Text="Draw bones first, then save them as a skeleton type.";return;}
        AskDrawingName("Save skeleton type","My skeleton",name=>
        {
            RefreshSkeletonTypes();if(skeletonTypes.ContainsKey(name)) throw new InvalidDataException("That skeleton type already exists. Choose another name.");
            WriteSkeletonPreset(Path.Combine(SkeletonTypesFolder,Guid.NewGuid().ToString("N")+".skeleton.json"),name,rig);
            skeletonType=name;RecordChange();RefreshSkeletonTypes();Status.Text="Saved reusable skeleton type: "+name;
        });
    }
    private void ApplySkeletonType(object sender,RoutedEventArgs e)
    {
        var name=EditorSkeletonTypes.SelectedItem?.ToString();if(name==null) return;
        if(rig.Bones.Count>0 && MessageBox.Show(this,"Replace the current bones with this skeleton type? You can undo this change.","Apply skeleton type",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        rig=GetSkeletonType(name);skeletonType=name;selectedBone=null;ShowRig.IsChecked=true;Output.SelectedIndex=1;RecordChange();RefreshEditor();
    }
    private static RigData CreatureGuide(string type)
    {
        var result=new RigData([],[]);var ends=new Dictionary<string,JointData>();var ids=new Dictionary<string,string>();
        void Add(string name,double x,double y,double z,string? parent=null,double hx=.5,double hy=.5,double hz=.5)
        {
            var head=parent!=null ? ends[parent] : new JointData(Guid.NewGuid().ToString("N"),hx,hy,hz);
            if(parent==null) result.Joints.Add(head);
            var tail=new JointData(Guid.NewGuid().ToString("N"),x,y,z);result.Joints.Add(tail);
            var id=Guid.NewGuid().ToString("N");result.Bones.Add(new(id,name,head.Id,tail.Id,parent!=null ? ids[parent] : null));ends[name]=tail;ids[name]=id;
        }
        if(type=="Serpent")
        {
            Add("tail_01",.5,.5,.8,null,.5,.5,.95);
            for(int i=2;i<=7;i++) Add("spine_"+i,.5,.5,.8-(i-1)*.1,i==2 ? "tail_01" : "spine_"+(i-1));
            Add("head",.5,.55,.06,"spine_7");
        }
        else if(type=="Quadruped")
        {
            Add("pelvis",.5,.6,.72,null,.5,.6,.85);Add("spine",.5,.65,.4,"pelvis");Add("neck",.5,.78,.25,"spine");Add("head",.5,.8,.08,"neck");
            Add("tail_01",.5,.65,.9,"pelvis");Add("tail_02",.5,.7,.98,"tail_01");
            foreach(var (side,x) in new[]{("l",.3),("r",.7)})
            foreach(var (limb,parent,z) in new[]{("hind","pelvis",.75),("front","spine",.35)})
            {var name=limb+"_"+side;Add(name+"_upper",x,.35,z,parent);Add(name+"_lower",x,.12,z,name+"_upper");Add(name+"_foot",x,.06,z-.12,name+"_lower");}
        }
        else
        {
            Add("body",.5,.65,.5,null,.5,.35,.6);Add("neck",.5,.8,.35,"body");Add("head",.5,.87,.18,"neck");Add("tail",.5,.3,.9,"body");
            foreach(var (side,sign) in new[]{("l",-1),("r",1)})
            {Add("wing_"+side+"_upper",.5+.22*sign,.68,.55,"body");Add("wing_"+side+"_tip",.5+.47*sign,.75,.7,"wing_"+side+"_upper");Add("leg_"+side,.5+.1*sign,.18,.6,"body");Add("foot_"+side,.5+.1*sign,.08,.4,"leg_"+side);}
        }
        DrawingDocument.ValidateRig(result);return result;
    }
}
