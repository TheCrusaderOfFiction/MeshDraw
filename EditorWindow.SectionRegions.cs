using System.Windows.Media.Media3D;
namespace MeshDraw;
public partial class EditorWindow
{
    private static double[,] SignedSectionMask(bool[,] mask)
    {
        int w=mask.GetLength(0),h=mask.GetLength(1);var distance=new double[w,h];var queue=new Queue<(int x,int y)>();
        for(int x=0;x<w;x++)for(int y=0;y<h;y++)
        {
            bool boundary=(x>0 && mask[x-1,y]!=mask[x,y]) || (y>0 && mask[x,y-1]!=mask[x,y]) || (x+1<w && mask[x+1,y]!=mask[x,y]) || (y+1<h && mask[x,y+1]!=mask[x,y]) || (mask[x,y] && (x==0 || y==0 || x==w-1 || y==h-1));
            distance[x,y]=boundary?.5:w+h;if(boundary)queue.Enqueue((x,y));
        }
        while(queue.Count>0){var (x,y)=queue.Dequeue();foreach(var (a,b) in new[]{(x-1,y),(x+1,y),(x,y-1),(x,y+1)})if(a>=0 && b>=0 && a<w && b<h && distance[a,b]>distance[x,y]+1){distance[a,b]=distance[x,y]+1;queue.Enqueue((a,b));}}
        for(int x=0;x<w;x++)for(int y=0;y<h;y++)if(mask[x,y])distance[x,y]=-distance[x,y];return distance;
    }
    private static bool RegionalPreviewProfile(Vector3D world,int axis,bool original,FaceDefinition[] faces,Dictionary<string,double[,]> fields,double? originalDistance=null)
    {
        if(faces.Length==0)return original;
        double selectedDistance=double.PositiveInfinity,selected=originalDistance ?? (original?-1:1);
        var ahead=new List<(double t,double span,double field)>();
        foreach(var face in faces)
        {
            var basis=FaceBasis(face);var normal=basis.normal;int dominant=Math.Abs(normal.X)>=Math.Abs(normal.Y) && Math.Abs(normal.X)>=Math.Abs(normal.Z)?0:Math.Abs(normal.Y)>=Math.Abs(normal.Z)?1:2;if(dominant!=axis)continue;
            var point=world-new Vector3D(face.MoveX,face.MoveY,face.MoveZ);double radius=Math.Abs(normal.X)+Math.Abs(normal.Y)+Math.Abs(normal.Z),sign=face.ReverseSection?-1:1;
            double t=sign*(Vector3D.DotProduct(point,normal)-face.Offset*radius);
            double u=(Vector3D.DotProduct(point,basis.u)+1)/2,v=(1-Vector3D.DotProduct(point,basis.v))/2;var field=fields[face.Id];
            double sampled=u<0 || v<0 || u>1 || v>1?4:2*field[Math.Clamp((int)(u*field.GetLength(0)),0,field.GetLength(0)-1),Math.Clamp((int)(v*field.GetLength(1)),0,field.GetLength(1)-1)]/Math.Max(field.GetLength(0),field.GetLength(1));
            if(t<=0 && -t<selectedDistance){selectedDistance=-t;selected=sampled;}
            if(t>0 && face.SectionMode=="Blend")ahead.Add((t,Math.Max(.001,radius-sign*(Vector3D.DotProduct(new Vector3D(face.MoveX,face.MoveY,face.MoveZ),normal)+face.Offset*radius)),sampled));
        }
        if(ahead.Count>0){var next=ahead.MinBy(p=>p.t);double weight=Math.Clamp(1-next.t/next.span,0,1);selected=selected*(1-weight)+next.field*weight;}
        return selected<=0;
    }
}
