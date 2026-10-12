using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
namespace MeshDraw;
public partial class EditorWindow
{
    private readonly record struct EdgeSurface(Point3D A,Point3D B,Point3D C);
    private string? crossFaceEdgeKey;
    private readonly Dictionary<string,List<(Point a,Point b)>> crossFaceProjected=new();
    private readonly List<(string? face,Point3D a,Point3D b)> crossFaceEdges=new();
    private readonly List<EdgeSurface> crossFaceSurfaces=new();
    private void PrepareCrossFaceEdges()
    {
        string key=canvasWidth+"x"+canvasHeight+string.Join(";",edgeGuides.SelectMany(p=>p.Value.Select(g=>p.Key+g)))+string.Join(";",extraFaces)+string.Join(";",connectionEdges);
        if(key==crossFaceEdgeKey)return;crossFaceEdgeKey=key;crossFaceEdges.Clear();crossFaceSurfaces.Clear();crossFaceProjected.Clear();
        foreach(var pair in edgeGuides)foreach(var g in pair.Value.Where(g=>g.Kind=="Straight"))crossFaceEdges.Add((pair.Key,GuideWorldPoint(pair.Key,g,0),GuideWorldPoint(pair.Key,g,1)));
        foreach(var c in connectionEdges.Where(ConnectionExists))crossFaceEdges.Add((null,ConnectionPoint(c.FaceA,c.GuideA,c.EndpointA),ConnectionPoint(c.FaceB,c.GuideB,c.EndpointB)));
        crossFaceSurfaces.AddRange(BuildEdgeSurfaces(crossFaceEdges.Select(p=>(p.a,p.b)).ToArray()));
    }
    private static List<EdgeSurface> BuildEdgeSurfaces((Point3D a,Point3D b)[] edges)
    {
        var vertices=new List<Point3D>();var ids=new Dictionary<(long,long,long),int>();var neighbors=new List<HashSet<int>>();
        int Node(Point3D p)
        {
            var key=((long)Math.Round(p.X*100000),(long)Math.Round(p.Y*100000),(long)Math.Round(p.Z*100000));
            if(ids.TryGetValue(key,out int id))return id;id=vertices.Count;ids[key]=id;vertices.Add(p);neighbors.Add([]);return id;
        }
        foreach(var edge in edges){int a=Node(edge.a),b=Node(edge.b);if(a!=b){neighbors[a].Add(b);neighbors[b].Add(a);}}
        var result=new List<EdgeSurface>();var seen=new HashSet<string>();int budget=40000;
        void AddCycle(List<int> path,bool allowNonPlanar=false)
        {
            var reversed=new[]{path[0]}.Concat(path.Skip(1).Reverse()).ToArray();var forward=string.Join(",",path);var backward=string.Join(",",reversed);var key=string.CompareOrdinal(forward,backward)<0?forward:backward;
            if(!seen.Add(key))return;
            var polygon=path.Select(i=>vertices[i]).ToArray();var normal=new Vector3D();
            for(int i=1;i<polygon.Length-1;i++)normal+=Vector3D.CrossProduct(polygon[i]-polygon[0],polygon[i+1]-polygon[0]);
            if(normal.Length<.000001)return;normal.Normalize();
            if(!allowNonPlanar && polygon.Any(p=>Math.Abs(Vector3D.DotProduct(p-polygon[0],normal))>.0001))return;
            result.AddRange(TriangulateEdgeSurface(polygon,normal));
        }
        // Long simple outline loops need no search. Branching networks use bounded chordless cycles.
        var simpleVisited=new HashSet<int>();
        for(int start=0;start<vertices.Count;start++)
        {
            if(neighbors[start].Count!=2 || simpleVisited.Contains(start))continue;
            var path=new List<int>();int previous=-1,current=start;
            while(neighbors[current].Count==2 && !path.Contains(current))
            {
                path.Add(current);int next=neighbors[current].First(n=>n!=previous);previous=current;current=next;
            }
            if(current==start && path.Count>=3){foreach(int node in path)simpleVisited.Add(node);AddCycle(path,true);}
        }
        void Search(int start,List<int> path)
        {
            if(--budget<0 || result.Count>2048)return;
            int last=path[^1];
            foreach(int next in neighbors[last])
            {
                if(next==start){if(path.Count>=3)AddCycle(path);continue;}
                if(next<start || path.Count>=16 || path.Contains(next))continue;
                if(path.Take(path.Count-1).Any(n=>n!=start && neighbors[next].Contains(n)))continue;
                path.Add(next);
                if(neighbors[next].Contains(start) && path.Count>=3)AddCycle(path);else Search(start,path);
                path.RemoveAt(path.Count-1);
                if(budget<0)return;
            }
        }
        for(int start=0;start<vertices.Count && budget>0;start++)if(!simpleVisited.Contains(start))Search(start,[start]);
        return result;
    }
    private static List<EdgeSurface> TriangulateEdgeSurface(Point3D[] polygon,Vector3D normal)
    {
        var u=polygon[1]-polygon[0];u.Normalize();var v=Vector3D.CrossProduct(normal,u);
        var points=polygon.Select(p=>new Point(Vector3D.DotProduct(p-polygon[0],u),Vector3D.DotProduct(p-polygon[0],v))).ToArray();
        static double Cross(Point a,Point b,Point c)=>Vector.CrossProduct(b-a,c-a);
        double area=0;for(int i=0;i<points.Length;i++)area+=points[i].X*points[(i+1)%points.Length].Y-points[(i+1)%points.Length].X*points[i].Y;
        double sign=area>=0?1:-1;var remaining=Enumerable.Range(0,polygon.Length).ToList();var result=new List<EdgeSurface>();
        while(remaining.Count>3)
        {
            bool found=false;
            for(int i=0;i<remaining.Count;i++)
            {
                int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                if(sign*Cross(points[a],points[b],points[c])<=.00000001)continue;
                if(remaining.Any(p=>p!=a && p!=b && p!=c && sign*Cross(points[a],points[b],points[p])>=-.00000001 && sign*Cross(points[b],points[c],points[p])>=-.00000001 && sign*Cross(points[c],points[a],points[p])>=-.00000001))continue;
                result.Add(new(polygon[a],polygon[b],polygon[c]));remaining.RemoveAt(i);found=true;break;
            }
            if(!found)return [];
        }
        if(remaining.Count==3)result.Add(new(polygon[remaining[0]],polygon[remaining[1]],polygon[remaining[2]]));return result;
    }
    private static bool ClipEdgeInterval(ref double low,ref double high,double value0,double value1,double threshold)
    {
        double delta=value1-value0;
        if(Math.Abs(delta)<1e-12)return value0>=threshold;
        double crossing=(threshold-value0)/delta;if(delta>0)low=Math.Max(low,crossing);else high=Math.Min(high,crossing);return high-low>1e-9;
    }
    private static List<(double low,double high)> VisibleEdgeIntervals(Point3D a,Point3D b,Vector3D towardCamera,IEnumerable<EdgeSurface> surfaces)
    {
        var hidden=new List<(double low,double high)>();
        foreach(var triangle in surfaces)
        {
            var u=triangle.B-triangle.A;var v=triangle.C-triangle.A;var n=Vector3D.CrossProduct(u,v);double denominator=Vector3D.DotProduct(n,towardCamera);
            if(Math.Abs(denominator)<1e-10)continue;
            double d0=Vector3D.DotProduct(n,triangle.A-a)/denominator,d1=Vector3D.DotProduct(n,triangle.A-b)/denominator;
            double low=0,high=1;if(!ClipEdgeInterval(ref low,ref high,d0,d1,.00001))continue;
            var p0=a+towardCamera*d0-triangle.A;var p1=b+towardCamera*d1-triangle.A;
            double uu=u.LengthSquared,vv=v.LengthSquared,uv=Vector3D.DotProduct(u,v),det=uu*vv-uv*uv;if(det<1e-12)continue;
            (double s,double t) Bary(Vector3D p){double pu=Vector3D.DotProduct(p,u),pv=Vector3D.DotProduct(p,v);return((vv*pu-uv*pv)/det,(uu*pv-uv*pu)/det);}
            var q0=Bary(p0);var q1=Bary(p1);
            if(ClipEdgeInterval(ref low,ref high,q0.s,q1.s,-1e-8) && ClipEdgeInterval(ref low,ref high,q0.t,q1.t,-1e-8) && ClipEdgeInterval(ref low,ref high,1-q0.s-q0.t,1-q1.s-q1.t,-1e-8))hidden.Add((Math.Max(0,low),Math.Min(1,high)));
        }
        var visible=new List<(double low,double high)>();double cursor=0;
        foreach(var interval in hidden.OrderBy(p=>p.low)){if(interval.low>cursor+1e-8)visible.Add((cursor,interval.low));cursor=Math.Max(cursor,interval.high);}
        if(cursor<1-1e-8)visible.Add((cursor,1));return visible;
    }
    private void RenderCrossFaceEdges(Canvas canvas,string face)
    {
        if(ShowCrossFaceEdges?.IsChecked!=true)return;
        if(!crossFaceProjected.TryGetValue(face,out var segments))
        {
        segments=[];crossFaceProjected[face]=segments;
        var towardCamera=-PlaneNormal(face);
        foreach(var edge in crossFaceEdges.Where(p=>p.face!=face))
        {
            var a=ProjectLinePoint(face,edge.a);var b=ProjectLinePoint(face,edge.b);if((b-a).Length<.5)continue;
            foreach(var interval in VisibleEdgeIntervals(edge.a,edge.b,towardCamera,crossFaceSurfaces))
            {
                double low=interval.low,high=interval.high;
                if(!ClipEdgeInterval(ref low,ref high,a.X,b.X,0) || !ClipEdgeInterval(ref low,ref high,canvasWidth-a.X,canvasWidth-b.X,0) || !ClipEdgeInterval(ref low,ref high,a.Y,b.Y,0) || !ClipEdgeInterval(ref low,ref high,canvasHeight-a.Y,canvasHeight-b.Y,0))continue;
                var start=a+(b-a)*low;var end=a+(b-a)*high;
                segments.Add((start,end));
            }
        }
        }
        foreach(var segment in segments)canvas.Children.Add(new Line {X1=segment.a.X,Y1=segment.a.Y,X2=segment.b.X,Y2=segment.b.Y,Stroke=Brushes.SteelBlue,StrokeThickness=2,Opacity=.75,StrokeDashArray=new DoubleCollection {3,2},IsHitTestVisible=false,Tag="CrossFaceEdge"});
    }
    private void CrossFaceEdgeVisibilityChanged(object sender,RoutedEventArgs e){if(drawingViews.Count>=6)RenderRig();}
}
