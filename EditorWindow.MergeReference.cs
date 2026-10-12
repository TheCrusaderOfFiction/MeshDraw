using System.Windows;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private void MergeEdgesToReference(object sender,RoutedEventArgs e)
    {
        if(equalityReference is not {} reference || edgeGuides.GetValueOrDefault(reference.face)?.FirstOrDefault(g=>g.Id==reference.id && g.Kind=="Straight") is not {} source)
        {PreviewLineSelection.Text="Select the reference edge and click Set reference edge. Then select partner edges and Merge.";return;}
        var members=highlightedGuides.Where(p=>p.id!=reference.id).Select(p=>(p.face,guide:edgeGuides.GetValueOrDefault(p.face)?.FirstOrDefault(g=>g.Id==p.id))).ToArray();
        if(members.Length==0 || members.Any(p=>p.guide?.Kind!="Straight") || highlightedConnections.Count>0){PreviewLineSelection.Text="Highlight one or more partner face edges, then Merge.";return;}
        var start=GuideWorldPoint(reference.face,source,0);var end=GuideWorldPoint(reference.face,source,1);var direction=end-start;var normal=PlaneNormal(reference.face);
        direction-=normal*Vector3D.DotProduct(direction,normal);
        if(direction.LengthSquared<.000001){PreviewLineSelection.Text="The reference needs a visible span on its face.";return;}
        double Along(Point3D p)=>Vector3D.DotProduct(p-start,direction)/direction.LengthSquared;
        var endpoints=members.Select(p=>(p.face,guide:p.guide!,a:GuideWorldPoint(p.face,p.guide!,0),b:GuideWorldPoint(p.face,p.guide!,1))).ToArray();
        double low=endpoints.Min(p=>Math.Min(Along(p.a),Along(p.b))),high=endpoints.Max(p=>Math.Max(Along(p.a),Along(p.b)));
        if(high-low<.000001){PreviewLineSelection.Text="The selected edges have no span along the reference axis.";return;}
        Point3D Match(Point3D point)
        {
            double t=(Along(point)-low)/(high-low);
            return start+(end-start)*t+normal*Vector3D.DotProduct(point-(start+(end-start)*t),normal);
        }
        var guidesBefore=edgeGuides.ToDictionary(p=>p.Key,p=>p.Value.ToList());var equalitiesBefore=lineEqualities.ToList();var connectionsBefore=connectionEdges.ToList();
        void Restore(){edgeGuides.Clear();foreach(var p in guidesBefore)edgeGuides[p.Key]=p.Value;lineEqualities.Clear();lineEqualities.AddRange(equalitiesBefore);connectionEdges.Clear();connectionEdges.AddRange(connectionsBefore);}
        // The reference is a construction edge; matching partners must not move it through an old live relation.
        lineEqualities.RemoveAll(r=>new[]{r.Whole.Id,r.First.Id,r.Second.Id}.Contains(source.Id));
        foreach(var item in endpoints)
        {
            var changed=GuideEndpointAtWorld(item.face,GuideEndpointAtWorld(item.face,item.guide,0,Match(item.a)),1,Match(item.b));
            if(!MoveRelatedGuide(item.face,changed)){Restore();PreviewLineSelection.Text="Merge blocked: "+(relatedMoveError ?? "a partner would leave the canvas or conflict with another relationship.");return;}
        }
        var available=endpoints.SelectMany(p=>new[]{(p.face,id:p.guide.Id,endpoint:0),(p.face,id:p.guide.Id,endpoint:1)}).ToArray();
        (string face,string id,int endpoint) Replacement(int endpoint)=>available.OrderBy(p=>(ConnectionPoint(p.face,p.id,p.endpoint)-(endpoint==0?start:end)).Length).First();
        for(int i=0;i<connectionEdges.Count;i++)
        {
            var c=connectionEdges[i];if(c.GuideA==source.Id){var p=Replacement(c.EndpointA);c=c with {FaceA=p.face,GuideA=p.id,EndpointA=p.endpoint};}if(c.GuideB==source.Id){var p=Replacement(c.EndpointB);c=c with {FaceB=p.face,GuideB=p.id,EndpointB=p.endpoint};}connectionEdges[i]=c;
        }
        edgeGuides[reference.face].RemoveAll(g=>g.Id==source.Id);CleanupPreviewRelationships();
        try{DrawingDocument.Validate(Document());}catch(System.IO.InvalidDataException error){Restore();PreviewLineSelection.Text="Merge blocked: "+error.Message;return;}
        equalityReference=null;highlightedGuides.Clear();highlightedConnections.Clear();selectedPreviewLine=null;
        RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text=$"Merged {members.Length} edges to the reference depth and angle. Reference edge removed.";
    }
}
