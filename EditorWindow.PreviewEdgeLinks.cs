using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private readonly Dictionary<GeometryModel3D,(string face,string id)> previewEdgeModels=new();
    private (string face,string id)? selectedPreviewLine;
    private readonly Dictionary<GeometryModel3D,(string key,string group,Color color)> previewConnectionModels=new();
    private readonly HashSet<string> highlightedConnections=new();
    private readonly List<ConnectionEdgeData> connectionEdges=new();
    private readonly List<(Model3DGroup scene,GeometryModel3D model,string category)> previewGuideVisibility=new();
    private bool PreviewCategoryVisible(string category)=>category switch {"Locks"=>PreviewShowLocks?.IsChecked!=false,"Merges"=>PreviewShowMerges?.IsChecked!=false,"Corners"=>PreviewShowCorners?.IsChecked!=false,_=>PreviewShowEdges?.IsChecked!=false};
    private void PreviewGuideVisibilityChanged(object sender,RoutedEventArgs e)
    {
        foreach(var item in previewGuideVisibility)
        {
            bool visible=PreviewCategoryVisible(item.category);bool present=item.scene.Children.Contains(item.model);
            if(visible && !present)item.scene.Children.Add(item.model);
            else if(!visible && present)item.scene.Children.Remove(item.model);
        }
    }

    private bool ConnectionExists(ConnectionEdgeData c)=>edgeGuides.GetValueOrDefault(c.FaceA)?.Any(g=>g.Id==c.GuideA)==true && edgeGuides.GetValueOrDefault(c.FaceB)?.Any(g=>g.Id==c.GuideB)==true;
    private Point3D ConnectionPoint(string face,string id,int endpoint)
    {
        var g=edgeGuides[face].First(g=>g.Id==id);return GuideWorldPoint(face,g,endpoint);
    }

    private Point3D GuideWorldPoint(string face,EdgeGuideData guide,int endpoint)=>FacePlanePoint(face,(endpoint==0?guide.X1:guide.X2)/canvasWidth,(endpoint==0?guide.Y1:guide.Y2)/canvasHeight)+PlaneNormal(face)*(endpoint==0?guide.Depth1:guide.Depth2);
    private EdgeGuideData GuideEndpointAtWorld(string face,EdgeGuideData guide,int endpoint,Point3D world)
    {
        var p=ProjectLinePoint(face,world);
        if(Math.Abs(p.X)<.000001)p.X=0;if(Math.Abs(p.Y)<.000001)p.Y=0;
        if(Math.Abs(p.X-canvasWidth)<.000001)p.X=canvasWidth;if(Math.Abs(p.Y-canvasHeight)<.000001)p.Y=canvasHeight;
        double depth=Vector3D.DotProduct(world-FacePlanePoint(face,p.X/canvasWidth,p.Y/canvasHeight),PlaneNormal(face));
        return endpoint==0?guide with {X1=p.X,Y1=p.Y,Depth1=depth}:guide with {X2=p.X,Y2=p.Y,Depth2=depth};
    }
    private Vector3D PlaneNormal(string face)
    {
        var u=FacePlanePoint(face,1,0)-FacePlanePoint(face,0,0);var v=FacePlanePoint(face,0,1)-FacePlanePoint(face,0,0);
        var normal=Vector3D.CrossProduct(u,v);normal.Normalize();return normal;
    }
    private bool ParallelLines(string a,string b)=>Math.Abs(Vector3D.DotProduct(PlaneNormal(a),PlaneNormal(b)))>.9999;
    private Point ProjectLinePoint(string face,Point3D point)
    {
        var origin=FacePlanePoint(face,0,0);var u=FacePlanePoint(face,1,0)-origin;var v=FacePlanePoint(face,0,1)-origin;var relative=point-origin;
        return new Point(Vector3D.DotProduct(relative,u)/u.LengthSquared*canvasWidth,Vector3D.DotProduct(relative,v)/v.LengthSquared*canvasHeight);
    }
    private bool SyncLockedLine(string face,EdgeGuideData source)=>MoveRelatedGuide(face,source);
    private readonly HashSet<(string face,string id)> highlightedGuides=new();
    private string? relatedMoveError;
    private bool MoveRelatedGuide(string face,EdgeGuideData source)
    {
        relatedMoveError=null;
        var backup=edgeGuides.ToDictionary(p=>p.Key,p=>p.Value.ToList());
        var queue=new Queue<(string face,EdgeGuideData guide)>();queue.Enqueue((face,source));
        var touched=new HashSet<string>();
        void Restore(){edgeGuides.Clear();foreach(var p in backup)edgeGuides[p.Key]=p.Value;}
        while(queue.Count>0)
        {
            var item=queue.Dequeue();if(!touched.Add(item.guide.Id))continue;
            var list=edgeGuides[item.face];var index=list.FindIndex(g=>g.Id==item.guide.Id);if(index<0)continue;
            var old=backup[item.face].First(g=>g.Id==item.guide.Id);var changed=item.guide;
            list[index]=changed;
            var lockId=changed.LockGroup ?? (changed.PositionLocked?changed.LinkGroup:null);
            foreach(var pair in edgeGuides)
            foreach(var current in pair.Value.ToArray())
            {
                if(touched.Contains(current.Id))continue;
                var next=current;
                if(lockId!=null && (current.LockGroup ?? (current.PositionLocked?current.LinkGroup:null))==lockId)
                {
                    if(!ParallelLines(item.face,pair.Key)){Restore();return false;}
                    var a=ProjectLinePoint(pair.Key,FacePlanePoint(item.face,changed.X1/canvasWidth,changed.Y1/canvasHeight));
                    var b=ProjectLinePoint(pair.Key,FacePlanePoint(item.face,changed.X2/canvasWidth,changed.Y2/canvasHeight));
                    next=next with {X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y};
                }
                if(changed.Kind=="Corner" && changed.CornerGroup!=null && current.Kind=="Corner" && current.CornerGroup==changed.CornerGroup)
                {
                    var common=GuideWorldPoint(item.face,changed,0);
                    next=GuideEndpointAtWorld(pair.Key,next,0,common);
                    next=GuideEndpointAtWorld(pair.Key,next,1,common);
                }
                if(pair.Key==item.face)
                foreach(var endpoint in new[]{(old.X1,old.Y1,changed.X1,changed.Y1,changed.Depth1),(old.X2,old.Y2,changed.X2,changed.Y2,changed.Depth2)})
                {
                    bool joint=old.Kind=="Corner" || backup[item.face].Any(g=>g.Kind=="Corner" && Math.Abs(g.X1-endpoint.Item1)<.01 && Math.Abs(g.Y1-endpoint.Item2)<.01);
                    if(!joint)continue;
                    if(Math.Abs(current.X1-endpoint.Item1)<.01 && Math.Abs(current.Y1-endpoint.Item2)<.01)next=next with {X1=endpoint.Item3,Y1=endpoint.Item4,Depth1=endpoint.Item5};
                    if(Math.Abs(current.X2-endpoint.Item1)<.01 && Math.Abs(current.Y2-endpoint.Item2)<.01)next=next with {X2=endpoint.Item3,Y2=endpoint.Item4,Depth2=endpoint.Item5};
                }
                if(next!=current)queue.Enqueue((pair.Key,next));
            }
            try
            {
                foreach(var relation in lineEqualities.Where(EqualityExists).Where(r=>new[]{r.Whole.Id,r.First.Id,r.Second.Id}.Contains(changed.Id)))
                    foreach(var update in EqualityUpdates(relation,changed.Id))if(!touched.Contains(update.guide.Id))queue.Enqueue(update);
            }
            catch(System.IO.InvalidDataException error){relatedMoveError=error.Message;Restore();return false;}
        }
        try{DrawingDocument.Validate(Document());return true;}catch(System.IO.InvalidDataException error){relatedMoveError=error.Message;Restore();return false;}
    }
    private void PreviewEdgeSegment(Model3DGroup scene,Point3D a,Point3D b,Color color,(string face,string id)? pick=null,string category="Edges")
    {
        var direction=b-a;if(direction.Length<.00001)return;direction.Normalize();
        var axis=Math.Abs(direction.Y)<.9?new Vector3D(0,1,0):new Vector3D(1,0,0);
        var side=Vector3D.CrossProduct(direction,axis);side.Normalize();side*=.008;
        var mesh=new MeshGeometry3D();AddQuad(mesh,a-side,b-side,b+side,a+side);
        var side2=Vector3D.CrossProduct(direction,side);side2.Normalize();side2*=.008;AddQuad(mesh,a-side2,b-side2,b+side2,a+side2);
        var material=new EmissiveMaterial(new SolidColorBrush(color));var model=new GeometryModel3D(mesh,material){BackMaterial=material};previewGuideVisibility.Add((scene,model,category));if(PreviewCategoryVisible(category))scene.Children.Add(model);
        if(pick is {} item)previewEdgeModels[model]=item;
    }
    private void RenderPreviewEdgeLinks(Model3DGroup scene)
    {
        previewGuideVisibility.Clear();previewEdgeModels.Clear();previewConnectionModels.Clear();var groups=new Dictionary<string,List<(Point3D a,Point3D b)>>();
        foreach(var pair in edgeGuides)
        foreach(var line in pair.Value.Where(g=>g.Kind=="Straight"))
        {
            var a=GuideWorldPoint(pair.Key,line,0);var b=GuideWorldPoint(pair.Key,line,1);
            PreviewEdgeSegment(scene,a,b,GuideDisplayColor(pair.Key,line),(pair.Key,line.Id));
            if(line.LinkGroup!=null && (!line.PositionLocked || line.LockGroup!=null)){if(!groups.ContainsKey(line.LinkGroup))groups[line.LinkGroup]=[];groups[line.LinkGroup].Add((a,b));}
        }
        foreach(var pair in edgeGuides)foreach(var curve in pair.Value.Where(g=>g.Kind=="Curve")) {var points=GuidePathPoints(curve);int step=Math.Max(1,points.Count/64);for(int i=step;i<points.Count+step;i+=step) {var a=points[Math.Min(i-step,points.Count-1)];var b=points[Math.Min(i,points.Count-1)];PreviewEdgeSegment(scene,FacePlanePoint(pair.Key,a.X/canvasWidth,a.Y/canvasHeight),FacePlanePoint(pair.Key,b.X/canvasWidth,b.Y/canvasHeight),GuideDisplayColor(pair.Key,curve),(pair.Key,curve.Id));}}
        RenderPreviewLockMarkers(scene);
        RenderLineEqualities(scene);
        RenderPreviewCorners(scene);
        foreach(var c in connectionEdges.Where(ConnectionExists))PreviewConnectionSegment(scene,ConnectionPoint(c.FaceA,c.GuideA,c.EndpointA),ConnectionPoint(c.FaceB,c.GuideB,c.EndpointB),Colors.Orange,c.Id,c.Id);
        foreach(var group in groups){var lines=group.Value;for(int i=1;i<lines.Count;i++){PreviewConnectionSegment(scene,lines[0].a,lines[i].a,Colors.Cyan,group.Key+"|"+i+"|a",group.Key);PreviewConnectionSegment(scene,lines[0].b,lines[i].b,Colors.Cyan,group.Key+"|"+i+"|b",group.Key);}}
    }
    private void RenderPreviewLockMarkers(Model3DGroup scene)
    {
        var groups=edgeGuides.SelectMany(p=>p.Value.Where(g=>g.Kind=="Straight" && g.PositionLocked && (g.LockGroup ?? g.LinkGroup)!=null).Select(g=>(face:p.Key,guide:g))).GroupBy(p=>p.guide.LockGroup ?? p.guide.LinkGroup);
        foreach(var group in groups)
        {
            var members=group.ToArray();
            Point3D Center((string face,EdgeGuideData guide) item){var a=GuideWorldPoint(item.face,item.guide,0);return a+(GuideWorldPoint(item.face,item.guide,1)-a)*.5;}
            var a=Center(members[0]);
            foreach(var item in members.Skip(1))
            {
                var b=Center(item);var delta=b-a;
                if(delta.Length<.00001)continue;
                int dots=Math.Clamp((int)Math.Ceiling(delta.Length/.055),8,96);
                for(int dot=0;dot<dots;dot++)PreviewConnectionSegment(scene,a+delta*(dot/(double)dots),a+delta*((dot+.18)/dots),Colors.Red,"lock|"+group.Key+"|"+item.guide.Id,group.Key!);
            }
        }
    }
    private void PreviewConnectionSegment(Model3DGroup scene,Point3D a,Point3D b,Color color,string key,string group)
    {
        int before=previewGuideVisibility.Count;
        PreviewEdgeSegment(scene,a,b,highlightedConnections.Contains(key)?Colors.DeepPink:color,category:key.StartsWith("merge|")?"Merges":IsLockMarker(key)?"Locks":"Edges");
        if(previewGuideVisibility.Count>before)previewConnectionModels[previewGuideVisibility[^1].model]=(key,group,color);
    }
    private bool BeginPreviewLineLink(MouseButtonEventArgs e)
    {
        if(PreviewSelectionMode.SelectedIndex is 0 or 2 or 3 or 4)
        {
            string? key=null;
            VisualTreeHelper.HitTest(OutlineViewport,null,result=>{if(result is RayMeshGeometry3DHitTestResult hit && hit.ModelHit is GeometryModel3D model && previewConnectionModels.TryGetValue(model,out var connection) && SelectionAllowsConnection(connection.key)){key=connection.key;return HitTestResultBehavior.Stop;}return HitTestResultBehavior.Continue;},new PointHitTestParameters(e.GetPosition(OutlineViewport)));
            if(key==null){if(PreviewSelectionMode.SelectedIndex!=0)return false;}
            else
            {
            if(!highlightedConnections.Add(key))highlightedConnections.Remove(key);
            RefreshGuideHighlights();PreviewLineSelection.Text=$"{highlightedConnections.Count} lines highlighted.";return true;
            }
        }
        string kind=PreviewSelectionMode.SelectedIndex==1?"Corner":"Straight";
        (string face,string id)? picked=null;
        VisualTreeHelper.HitTest(OutlineViewport,null,result=>{if(result is RayMeshGeometry3DHitTestResult hit && hit.ModelHit is GeometryModel3D model && previewEdgeModels.TryGetValue(model,out var entry) && edgeGuides.GetValueOrDefault(entry.face)?.Any(g=>g.Id==entry.id && (kind=="Straight" ? g.Kind!="Corner" : g.Kind==kind))==true){picked=entry;return HitTestResultBehavior.Stop;}return HitTestResultBehavior.Continue;},new PointHitTestParameters(e.GetPosition(OutlineViewport)));
        if(picked is not {} target)return false;
        var guide=edgeGuides[target.face].First(g=>g.Id==target.id);
        if(guide.Kind=="Corner" && guide.CornerGroup!=null)
        {
            var members=edgeGuides.SelectMany(p=>p.Value.Where(g=>g.Kind=="Corner" && g.CornerGroup==guide.CornerGroup).Select(g=>(face:p.Key,id:g.Id))).ToArray();
            bool remove=members.Any(highlightedGuides.Contains);foreach(var member in members){if(remove)highlightedGuides.Remove(member);else highlightedGuides.Add(member);}
        }
        else if(!highlightedGuides.Add(target))highlightedGuides.Remove(target);
        selectedPreviewLine=highlightedGuides.FirstOrDefault();
        RefreshGuideHighlights();
        int count=guide.Kind=="Corner"?highlightedGuides.Select(p=>edgeGuides.GetValueOrDefault(p.face)?.FirstOrDefault(g=>g.Id==p.id)).Where(g=>g!=null).Select(g=>g!.CornerGroup ?? g.Id).Distinct().Count():highlightedGuides.Count;
        PreviewLineSelection.Text=$"{count} highlighted. Choose Merge, Connect, or Lock positions.";return true;
    }
    private Color GuideDisplayColor(string face,EdgeGuideData guide)
    {
        if(highlightedGuides.Contains((face,guide.Id)))return Colors.DeepPink;
        if(guide.Kind=="Corner")return guide.CornerGroup==null?Colors.Gold:Colors.DodgerBlue;
        return guide.PositionLocked?Colors.LimeGreen:guide.LinkGroup!=null?Colors.Cyan:Colors.Orange;
    }
    private void RefreshGuideHighlights()
    {
        foreach(var pair in previewConnectionModels)
        {
            var material=new EmissiveMaterial(new SolidColorBrush(highlightedConnections.Contains(pair.Value.key)?Colors.DeepPink:pair.Value.color));pair.Key.Material=pair.Key.BackMaterial=material;
        }
        foreach(var pair in previewEdgeModels)
        {
            var guide=edgeGuides.GetValueOrDefault(pair.Value.face)?.FirstOrDefault(g=>g.Id==pair.Value.id);if(guide==null)continue;
            var color=GuideDisplayColor(pair.Value.face,guide);
            var material=new EmissiveMaterial(new SolidColorBrush(color));pair.Key.Material=pair.Key.BackMaterial=material;
        }
    }
    private void PreviewSelectionModeChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        highlightedGuides.Clear();highlightedConnections.Clear();selectedPreviewLine=null;
        RefreshGuideHighlights();
        if(drawingViews.Count>=6)RenderRig();
        if(PreviewLineSelection!=null)PreviewLineSelection.Text=PreviewSelectionMode.SelectedIndex==4?"Click red dotted lock lines to highlight them. Unlock or Delete selected releases the relationship.":PreviewSelectionMode.SelectedIndex==3?"Dashed blue merge markers mean the endpoints represent the same 3D point.":PreviewSelectionMode.SelectedIndex==2?"Click connection lines to highlight them. Edges and corners cannot be selected in this mode.":PreviewSelectionMode.SelectedIndex==1?"Click corners to highlight them. Edges cannot be selected in this mode.":"Click edges to highlight them. Corners cannot be selected in this mode.";
    }
    private void ClearGuideSelection(object sender,RoutedEventArgs e){highlightedGuides.Clear();highlightedConnections.Clear();selectedPreviewLine=null;RefreshGuideHighlights();PreviewLineSelection.Text="Highlights cleared.";}
    private void MergeSelectedGuides(object sender,RoutedEventArgs e)
    {
        var selected=highlightedGuides.Select(p=>(p.face,guide:edgeGuides.GetValueOrDefault(p.face)?.FirstOrDefault(g=>g.Id==p.id))).Where(p=>p.guide!=null).ToArray();
        if(selected.Length>0 && selected.All(p=>p.guide!.Kind=="Straight")){MergeEdgesToReference(sender,e);return;}
        ApplySelectedGuides("Merge");
    }
    private void ConnectSelectedGuides(object sender,RoutedEventArgs e)=>ApplySelectedGuides("Connect");
    private void LockSelectedGuides(object sender,RoutedEventArgs e)=>ApplySelectedGuides("Lock");
    private void ApplySelectedGuides(string action)
    {
        var selected=highlightedGuides.Select(p=>(face:p.face,guide:edgeGuides.GetValueOrDefault(p.face)?.FirstOrDefault(g=>g.Id==p.id))).Where(p=>p.guide!=null).ToArray();
        if(selected.Length<2){PreviewLineSelection.Text="Highlight at least two edges or corners first.";return;}
        var backup=edgeGuides.ToDictionary(p=>p.Key,p=>p.Value.ToList());var first=selected[0];bool okay=true;
        if(action=="Connect")
        {
            foreach(var other in selected.Skip(1))
            {
                var a=first.guide!;var b=other.guide!;
                var closest=(from ea in (a.Kind=="Corner"?new[]{0}:new[]{0,1}) from eb in (b.Kind=="Corner"?new[]{0}:new[]{0,1}) select (ea,eb,d:(ConnectionPoint(first.face,a.Id,ea)-ConnectionPoint(other.face,b.Id,eb)).Length)).OrderBy(p=>p.d).First();
                if(closest.d<.00001)continue;
                if(!connectionEdges.Any(c=>(c.GuideA==a.Id && c.EndpointA==closest.ea && c.GuideB==b.Id && c.EndpointB==closest.eb) || (c.GuideB==a.Id && c.EndpointB==closest.ea && c.GuideA==b.Id && c.EndpointA==closest.eb)))connectionEdges.Add(new(Guid.NewGuid().ToString("N"),first.face,a.Id,closest.ea,other.face,b.Id,closest.eb));
            }
            RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text="Added 3D edges. Endpoints remain separate and follow their attached marks.";return;
        }
        if(action=="Lock")
        {
            if(selected.Any(p=>p.guide!.Kind!="Straight" || !ParallelLines(first.face,p.face))){PreviewLineSelection.Text="Lock positions requires edges on parallel faces.";return;}
            var oldGroups=selected.Select(p=>p.guide!.LockGroup ?? (p.guide.PositionLocked?p.guide.LinkGroup:null)).Where(g=>g!=null).ToHashSet();var group=Guid.NewGuid().ToString("N");
            foreach(var pair in edgeGuides)for(int i=0;i<pair.Value.Count;i++){var g=pair.Value[i];if(highlightedGuides.Contains((pair.Key,g.Id)) || oldGroups.Contains(g.LockGroup ?? (g.PositionLocked?g.LinkGroup:null)))pair.Value[i]=g with {LockGroup=group,PositionLocked=true};}
            okay=MoveRelatedGuide(first.face,edgeGuides[first.face].First(g=>g.Id==first.guide!.Id));
        }
        else if(selected.All(p=>p.guide!.Kind=="Corner"))
        {
            var mergeGroups=selected.Select(p=>p.guide!.CornerGroup).Where(g=>g!=null).ToHashSet();var group=Guid.NewGuid().ToString("N");
            foreach(var pair in edgeGuides)for(int i=0;i<pair.Value.Count;i++){var g=pair.Value[i];if(highlightedGuides.Contains((pair.Key,g.Id)) || (g.CornerGroup!=null && mergeGroups.Contains(g.CornerGroup)))pair.Value[i]=g with {CornerGroup=group};}
            var second=selected[1];okay=MergePreviewCorners(first.face,edgeGuides[first.face].First(g=>g.Id==first.guide!.Id),second.face,edgeGuides[second.face].First(g=>g.Id==second.guide!.Id));
        }
        else if(selected.All(p=>p.guide!.Kind=="Straight"))
        {
            foreach(var other in selected.Skip(1))
            {
                var a=edgeGuides[first.face].First(g=>g.Id==first.guide!.Id);var b=edgeGuides[other.face].First(g=>g.Id==other.guide!.Id);
                var a0=GuideWorldPoint(first.face,a,0);var a1=GuideWorldPoint(first.face,a,1);var b0=GuideWorldPoint(other.face,b,0);var b1=GuideWorldPoint(other.face,b,1);
                bool reverse=(a0-b1).Length+(a1-b0).Length<(a0-b0).Length+(a1-b1).Length;
                EdgeGuideData Corner(string face,EdgeGuideData line,int endpoint)
                {
                    var p=new Point(endpoint==0?line.X1:line.X2,endpoint==0?line.Y1:line.Y2);double depth=endpoint==0?line.Depth1:line.Depth2;
                    var list=edgeGuides[face];var c=list.FirstOrDefault(g=>g.Kind=="Corner" && (new Point(g.X1,g.Y1)-p).Length<.01 && Math.Abs(g.Depth1-depth)<.01);if(c!=null)return c;
                    c=new(Guid.NewGuid().ToString("N"),"Corner",p.X,p.Y,p.X,p.Y,Depth1:depth,Depth2:depth);list.Add(c);return c;
                }
                var cornersA=new[]{Corner(first.face,a,0),Corner(first.face,a,1)};var cornersB=new[]{Corner(other.face,b,reverse?1:0),Corner(other.face,b,reverse?0:1)};
                for(int endpoint=0;endpoint<2;endpoint++)
                {
                    var ca=edgeGuides[first.face].First(g=>g.Id==cornersA[endpoint].Id);var cb=edgeGuides[other.face].First(g=>g.Id==cornersB[endpoint].Id);
                    if(!MergePreviewCorners(first.face,ca,other.face,cb)){okay=false;break;}
                }
                if(!okay)break;
            }
        }
        else{PreviewLineSelection.Text="Select edges together or corners together.";return;}
        if(!okay){edgeGuides.Clear();foreach(var p in backup)edgeGuides[p.Key]=p.Value;PreviewLineSelection.Text="Action blocked: a joined edge would leave the canvas or collapse.";return;}
        RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text=action+" applied. Move edges or corners with the drawing tools.";
    }
    private void DeleteSelectedMergeMarkers(object sender,RoutedEventArgs e)
    {
        var detach=new HashSet<string>();
        foreach(var key in highlightedConnections.Where(k=>k.StartsWith("merge|")))
        {
            var parts=key.Split('|');
            if(parts.Length==3 && edgeGuides.Values.SelectMany(g=>g).Any(g=>g.Kind=="Corner" && g.Id==parts[2] && g.CornerGroup==parts[1]))detach.Add(parts[2]);
        }
        foreach(var selected in highlightedGuides)
            if(edgeGuides.GetValueOrDefault(selected.face)?.Any(g=>g.Id==selected.id && g.Kind=="Corner" && g.CornerGroup!=null)==true)detach.Add(selected.id);
        if(detach.Count==0){PreviewLineSelection.Text="Select blue merge markers, or merged corners, to delete their merge relationships.";return;}
        foreach(var list in edgeGuides.Values)for(int i=0;i<list.Count;i++)if(detach.Contains(list[i].Id))list[i]=list[i] with {CornerGroup=null};
        var singletons=edgeGuides.Values.SelectMany(g=>g).Where(g=>g.CornerGroup!=null).GroupBy(g=>g.CornerGroup).Where(g=>g.Count()<2).Select(g=>g.Key).ToHashSet();
        foreach(var list in edgeGuides.Values)for(int i=0;i<list.Count;i++)if(list[i].CornerGroup!=null && singletons.Contains(list[i].CornerGroup))list[i]=list[i] with {CornerGroup=null};
        highlightedConnections.RemoveWhere(k=>k.StartsWith("merge|"));
        RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text="Deleted merge relationships. Corners, edges, and positions preserved.";
    }
    private void ClearPreviewLineConnection(object sender,RoutedEventArgs e)
    {
        var before=Snapshot();
        connectionEdges.RemoveAll(c=>highlightedConnections.Contains(c.Id));
        var groups=highlightedGuides.SelectMany(p=>edgeGuides.GetValueOrDefault(p.face)?.Where(g=>g.Id==p.id).Select(g=>g.CornerGroup) ?? []).Where(g=>g!=null).ToHashSet();
        foreach(var key in highlightedConnections.Where(k=>k.StartsWith("merge|"))){var parts=key.Split('|');if(parts.Length==3)groups.Add(parts[1]);}
        var cornerIds=new HashSet<string>();
        foreach(var p in highlightedGuides)
        {
            var guide=edgeGuides.GetValueOrDefault(p.face)?.FirstOrDefault(g=>g.Id==p.id);if(guide==null)continue;
            if(guide.Kind=="Corner")cornerIds.Add(guide.Id);
            else foreach(var c in edgeGuides[p.face].Where(g=>g.Kind=="Corner" && GuideDistance(new Point(g.X1,g.Y1),guide)<.01))cornerIds.Add(c.Id);
        }
        foreach(var list in edgeGuides.Values)list.RemoveAll(g=>g.Kind=="Corner" && (cornerIds.Contains(g.Id) || (g.CornerGroup!=null && groups.Contains(g.CornerGroup))));
        CleanupPreviewRelationships();
        if(Snapshot()==before){PreviewLineSelection.Text="Select corners, merge markers, or connection edges to dissolve. Use Unlock for locks.";return;}
        RecordChange();RenderRig();RenderOutlinePreview();PreviewLineSelection.Text="Dissolved selected joints and connections.";
    }
}
