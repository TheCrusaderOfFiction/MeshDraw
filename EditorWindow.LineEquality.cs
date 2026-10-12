using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MeshDraw;
public partial class EditorWindow
{
    private readonly List<LineEqualityData> lineEqualities=new();
    private (string face,string id)? equalityReference;
    private bool EqualityExists(LineEqualityData relation)=>new[]{relation.Whole,relation.First,relation.Second}.All(p=>edgeGuides.GetValueOrDefault(p.Face)?.Any(g=>g.Id==p.Id && g.Kind=="Straight")==true);
    private EdgeGuideData EqualityGuide(GuideReference p)=>edgeGuides[p.Face].First(g=>g.Id==p.Id);
    private (Point3D a,Point3D b) EqualityPoints(GuideReference p,bool reverse=false)
    {
        var g=EqualityGuide(p);var a=GuideWorldPoint(p.Face,g,0);var b=GuideWorldPoint(p.Face,g,1);return reverse?(b,a):(a,b);
    }
    private EdgeGuideData EqualityProjected(GuideReference p,Point3D a,Point3D b,bool reverse=false)
    {
        var start=ProjectLinePoint(p.Face,reverse?b:a);var end=ProjectLinePoint(p.Face,reverse?a:b);
        return EqualityGuide(p) with {X1=start.X,Y1=start.Y,X2=end.X,Y2=end.Y};
    }
    private EdgeGuideData EqualityViewProjected(GuideReference whole,GuideReference part,Point3D a,Point3D b,bool reverse)
    {
        var viewNormal=PlaneNormal(whole.Face);var partNormal=PlaneNormal(part.Face);var origin=FacePlanePoint(part.Face,0,0);var previous=EqualityPoints(part,reverse);
        Point3D OnPlane(Point3D target,Point3D old)
        {
            double denominator=Vector3D.DotProduct(viewNormal,partNormal);double distance=Vector3D.DotProduct(origin-target,partNormal);
            if(Math.Abs(denominator)<.000001)
            {
                return target+viewNormal*Vector3D.DotProduct(old-target,viewNormal);
            }
            return target+viewNormal*(distance/denominator);
        }
        var start=OnPlane(a,previous.a);var end=OnPlane(b,previous.b);var projected=EqualityProjected(part,start,end,reverse);
        if(Math.Abs(Vector3D.DotProduct(viewNormal,partNormal))<.000001)
        {
            var p=reverse?end:start;var q=reverse?start:end;
            projected=projected with {Depth1=Vector3D.DotProduct(p-FacePlanePoint(part.Face,projected.X1/canvasWidth,projected.Y1/canvasHeight),partNormal),Depth2=Vector3D.DotProduct(q-FacePlanePoint(part.Face,projected.X2/canvasWidth,projected.Y2/canvasHeight),partNormal)};
        }
        return projected;
    }
    private IEnumerable<(string face,EdgeGuideData guide)> EqualityUpdates(LineEqualityData relation,string movedId)
    {
        var whole=EqualityPoints(relation.Whole);var first=EqualityPoints(relation.First,relation.ReverseFirst);var second=EqualityPoints(relation.Second,relation.ReverseSecond);
        if(movedId!=relation.Whole.Id)
        {
            var source=movedId==relation.First.Id?first:second;double start=movedId==relation.First.Id?0:relation.Split;double span=movedId==relation.First.Id?relation.Split:1-relation.Split;
            var direction=(source.b-source.a)/span;var a=source.a-direction*start;var b=a+direction;
            // Recover the complete span from either section, then project it onto the reference face.
            var projected=EqualityProjected(relation.Whole,a,b);yield return(relation.Whole.Face,projected);
            whole=(FacePlanePoint(relation.Whole.Face,projected.X1/canvasWidth,projected.Y1/canvasHeight),FacePlanePoint(relation.Whole.Face,projected.X2/canvasWidth,projected.Y2/canvasHeight));
        }
        var joint=whole.a+(whole.b-whole.a)*relation.Split;
        if(movedId!=relation.First.Id)yield return(relation.First.Face,EqualityViewProjected(relation.Whole,relation.First,whole.a,joint,relation.ReverseFirst));
        if(movedId!=relation.Second.Id)yield return(relation.Second.Face,EqualityViewProjected(relation.Whole,relation.Second,joint,whole.b,relation.ReverseSecond));
    }
    private void SetEqualityReference(object sender,RoutedEventArgs e)
    {
        var selected=highlightedGuides.Where(p=>edgeGuides.GetValueOrDefault(p.face)?.Any(g=>g.Id==p.id && g.Kind=="Straight")==true).ToArray();
        if(selected.Length!=1 || highlightedConnections.Count>0){PreviewLineSelection.Text="Highlight one face edge to use as the single line.";return;}
        equalityReference=selected[0];ClearGuideSelection(sender,e);PreviewLineSelection.Text="Reference edge set on "+FaceName(selected[0].face)+". Select any number of partner edges and Merge to transfer its depth/angle and remove it.";
    }
    private void AssignLineEquality(object sender,RoutedEventArgs e)
    {
        if(equalityReference is not {} reference || edgeGuides.GetValueOrDefault(reference.face)?.Any(g=>g.Id==reference.id && g.Kind=="Straight")!=true){PreviewLineSelection.Text="First highlight one line and click Set single line.";return;}
        var selected=highlightedGuides.Where(p=>p.id!=reference.id && edgeGuides.GetValueOrDefault(p.face)?.Any(g=>g.Id==p.id && g.Kind=="Straight")==true).ToArray();
        if(selected.Length!=2 || highlightedConnections.Count>0){PreviewLineSelection.Text="Highlight exactly two partner face edges.";return;}
        var whole=new GuideReference(reference.face,reference.id);var a=new GuideReference(selected[0].face,selected[0].id);var b=new GuideReference(selected[1].face,selected[1].id);
        var ids=new[]{whole.Id,a.Id,b.Id};
        if(lineEqualities.Where(EqualityExists).Any(r=>new[]{r.Whole.Id,r.First.Id,r.Second.Id}.Any(ids.Contains)) || edgeGuides.Values.SelectMany(g=>g).Any(g=>ids.Contains(g.Id) && g.PositionLocked)){PreviewLineSelection.Text="Release existing position locks or 1-to-2 relationships for these lines first.";return;}
        var wp=EqualityPoints(whole);var ap=EqualityPoints(a);var bp=EqualityPoints(b);var direction=wp.b-wp.a;
        double Along((Point3D a,Point3D b) p)=>Vector3D.DotProduct((p.a+(p.b-p.a)*.5)-wp.a,direction);
        if(Along(ap)>Along(bp)){(a,b)=(b,a);(ap,bp)=(bp,ap);}
        double ProjectedLength((Point3D a,Point3D b) points){var n=PlaneNormal(whole.Face);var d=points.b-points.a;return (d-n*Vector3D.DotProduct(d,n)).Length;}
        double lengthA=ProjectedLength(ap),lengthB=ProjectedLength(bp);
        if(lengthA<.000001 || lengthB<.000001){PreviewLineSelection.Text="A selected line is edge-on to the single line’s face. Choose two visible projected sections.";return;}
        double split=lengthA/(lengthA+lengthB);
        var relation=new LineEqualityData(Guid.NewGuid().ToString("N"),whole,a,b,"Sections",split,Vector3D.DotProduct(ap.b-ap.a,direction)<0,Vector3D.DotProduct(bp.b-bp.a,direction)<0);
        lineEqualities.Add(relation);
        if(!MoveRelatedGuide(whole.Face,EqualityGuide(whole))){lineEqualities.Remove(relation);PreviewLineSelection.Text="Cannot assign: "+(relatedMoveError ?? "a partner would leave the canvas or become too short.");return;}
        RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text="Assigned two projected sections. From the single line’s face, they now match its span.";
    }
    private void ClearLineEqualities(object sender,RoutedEventArgs e)
    {
        var selected=highlightedGuides.Select(p=>p.id).ToHashSet();
        int removed=lineEqualities.RemoveAll(r=>selected.Contains(r.Whole.Id) || selected.Contains(r.First.Id) || selected.Contains(r.Second.Id));
        if(removed==0){PreviewLineSelection.Text="Highlight a member of a 1-to-2 relationship to clear it.";return;}
        RecordChange();RenderOutlinePreview();PreviewLineSelection.Text="Cleared 1-to-2 relationships.";
    }
    private void RenderLineEqualities(Model3DGroup scene)
    {
        foreach(var relation in lineEqualities.Where(EqualityExists))
        {
            var whole=EqualityPoints(relation.Whole);var center=whole.a+(whole.b-whole.a)*.5;
            foreach(var p in new[]{relation.First,relation.Second})
            {
                var pair=EqualityPoints(p);var target=pair.a+(pair.b-pair.a)*.5;var delta=target-center;
                for(int dot=0;dot<24;dot++)PreviewConnectionSegment(scene,center+delta*(dot/24.0),center+delta*((dot+.18)/24.0),Colors.Red,"equality|"+relation.Id+"|"+p.Id,relation.Id);
            }
        }
    }
}
