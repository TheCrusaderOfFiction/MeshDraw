using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private void RenderPreviewCorners(Model3DGroup scene)
    {
        var renderedPoints=new HashSet<string>();
        foreach(var pair in edgeGuides)
        foreach(var corner in pair.Value.Where(g=>g.Kind=="Corner"))
        {
            if(!renderedPoints.Add(corner.CornerGroup ?? corner.Id))continue;
            var point=GuideWorldPoint(pair.Key,corner,0);
            foreach(var axis in new[]{new Vector3D(.018,0,0),new Vector3D(0,.018,0),new Vector3D(0,0,.018)})PreviewEdgeSegment(scene,point-axis,point+axis,GuideDisplayColor(pair.Key,corner),(pair.Key,corner.Id),category:"Corners");
        }
        foreach(var group in edgeGuides.SelectMany(p=>p.Value.Where(g=>g.CornerGroup!=null).Select(g=>(face:p.Key,guide:g))).GroupBy(g=>g.guide.CornerGroup))
        {
            var members=group.ToArray();var a=GuideWorldPoint(members[0].face,members[0].guide,0);
            foreach(var item in members.Skip(1))
            {
                var b=GuideWorldPoint(item.face,item.guide,0);
                var key="merge|"+group.Key+"|"+item.guide.Id;
                if((b-a).Length>.00001)
                    for(int dash=0;dash<12;dash++)PreviewConnectionSegment(scene,a+(b-a)*(dash/12.0),a+(b-a)*((dash+.55)/12.0),Colors.DodgerBlue,key,group.Key!);
                else
                {
                    int member=Array.IndexOf(members,item)-1;int count=members.Length-1;
                    for(int ring=0;ring<2;ring++)for(int dash=0;dash<8;dash++)
                    {
                        double angle=(member+(dash/8.0))/count*2*Math.PI;double end=(member+(dash+.65)/8.0)/count*2*Math.PI;
                        Vector3D Offset(double t)=>ring==0?new(.03*Math.Cos(t),.03*Math.Sin(t),0):new(.03*Math.Cos(t),0,.03*Math.Sin(t));
                        PreviewConnectionSegment(scene,a+Offset(angle),a+Offset(end),Colors.DodgerBlue,key,group.Key!);
                    }
                }
            }
        }
    }
    private bool MergePreviewCorners(string faceA,EdgeGuideData a,string faceB,EdgeGuideData b)
    {
        var group=a.CornerGroup ?? b.CornerGroup ?? Guid.NewGuid().ToString("N");var oldGroups=new[]{a.CornerGroup,b.CornerGroup}.Where(g=>g!=null).ToHashSet();
        var members=edgeGuides.SelectMany(pair=>pair.Value.Where(g=>g.Id==a.Id || g.Id==b.Id || (g.CornerGroup!=null && oldGroups.Contains(g.CornerGroup))).Select(g=>(face:pair.Key,guide:g))).ToArray();
        var common=new Point3D();foreach(var item in members){var p=GuideWorldPoint(item.face,item.guide,0);common.X+=p.X/members.Length;common.Y+=p.Y/members.Length;common.Z+=p.Z/members.Length;}
        for(int iteration=0;iteration<100;iteration++)
        {
            var correction=new Vector3D();foreach(var item in members){var p=GuideWorldPoint(item.face,item.guide,0);var n=PlaneNormal(item.face);var delta=p-common;correction+=delta-n*Vector3D.DotProduct(delta,n);}common+=correction/members.Length;
        }
        var backup=edgeGuides.ToDictionary(p=>p.Key,p=>p.Value.ToList());
        foreach(var item in members)
        {
            var point=ProjectLinePoint(item.face,common);if(point.X<-.0001 || point.X>canvasWidth+.0001 || point.Y<-.0001 || point.Y>canvasHeight+.0001){edgeGuides.Clear();foreach(var pair in backup)edgeGuides[pair.Key]=pair.Value;return false;}point=EdgePoint(point);
            var list=edgeGuides[item.face];list[list.FindIndex(g=>g.Id==item.guide.Id)]=GuideEndpointAtWorld(item.face,GuideEndpointAtWorld(item.face,item.guide,0,common),1,common) with {CornerGroup=group,CornerStyle=a.CornerStyle};
            for(int i=0;i<list.Count;i++)if(list[i].Kind=="Straight")
            {
                var line=list[i];if(Math.Abs(line.X1-item.guide.X1)<.01 && Math.Abs(line.Y1-item.guide.Y1)<.01)line=GuideEndpointAtWorld(item.face,line,0,common);if(Math.Abs(line.X2-item.guide.X1)<.01 && Math.Abs(line.Y2-item.guide.Y1)<.01)line=GuideEndpointAtWorld(item.face,line,1,common);list[i]=line;
            }
        }
        try{DrawingDocument.Validate(Document());}catch(System.IO.InvalidDataException){edgeGuides.Clear();foreach(var old in backup)edgeGuides[old.Key]=old.Value;return false;}
        foreach(var pair in edgeGuides)foreach(var line in pair.Value.Where(g=>g.PositionLocked).ToArray())if(!SyncLockedLine(pair.Key,line)){edgeGuides.Clear();foreach(var old in backup)edgeGuides[old.Key]=old.Value;return false;}
        return true;
    }
}
