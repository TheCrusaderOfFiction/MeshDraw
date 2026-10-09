using System.Windows;
namespace MeshDraw;
// Sample at fixed travel intervals, so event frequency does not determine correction strength.
internal sealed class StrokeSmoother
{
    private Point input,filtered;
    private float inputPressure,filteredPressure;
    private readonly double length,step;
    private double remainder;
    internal StrokeSmoother(Point start,float pressure,double strength)
    {input=filtered=start;inputPressure=filteredPressure=pressure;length=Math.Max(.01,strength*1.4);step=Math.Max(.25,length/24);}
    internal List<(Point point,float pressure)> Move(Point target,float pressure)
    {
        var output=new List<(Point,float)>();var start=input;var delta=target-start;double distance=delta.Length;
        if(distance<1e-8) {inputPressure=pressure;return output;}
        double traveled=step-remainder;
        for(;traveled<=distance;traveled+=step)
        {
            double t=traveled/distance;var sample=start+delta*t;float p=(float)(inputPressure+(pressure-inputPressure)*t);
            double alpha=1-Math.Exp(-step/length);filtered+=(sample-filtered)*alpha;filteredPressure+=(float)((p-filteredPressure)*alpha);
            output.Add((filtered,filteredPressure));
        }
        remainder=(remainder+distance)%step;input=target;inputPressure=pressure;return output;
    }
    internal List<(Point point,float pressure)> Finish(Point target,float pressure)
    {
        var result=Move(target,pressure);
        // Settle the delayed tip in short segments rather than adding a long release-time jump.
        for(int i=0;i<80 && (filtered-target).Length>.1;i++)
        {filtered+=(target-filtered)*.18;filteredPressure+=(pressure-filteredPressure)*.18f;result.Add((filtered,filteredPressure));}
        result.Add((target,pressure));return result;
    }
    internal static List<(Point point,float pressure)> SmoothAfter(IReadOnlyList<Point> points,IReadOnlyList<float> pressures,double strength)
    {
        if(points.Count<3 || strength<=0) return points.Select((p,i)=>(p,pressures[i])).ToList();
        // Uniform arc-length samples avoid overweighting slow sections with more input events.
        double spacing=Math.Max(.5,strength/8),radius=strength*2;
        var samples=new List<(Point point,float pressure,double distance)> {(points[0],pressures[0],0)};
        double total=0,next=spacing;
        for(int i=1;i<points.Count;i++)
        {
            double segment=(points[i]-points[i-1]).Length;
            while(next<=total+segment && segment>0) {double t=(next-total)/segment;samples.Add((points[i-1]+(points[i]-points[i-1])*t,(float)(pressures[i-1]+(pressures[i]-pressures[i-1])*t),next));next+=spacing;}
            total+=segment;
        }
        samples.Add((points[^1],pressures[^1],total));var result=new List<(Point,float)>();int window=(int)Math.Ceiling(radius/spacing);
        for(int i=0;i<samples.Count;i++)
        {
            double x=0,y=0,p=0,weight=0;
            for(int j=Math.Max(0,i-window);j<=Math.Min(samples.Count-1,i+window);j++) {double d=(samples[j].distance-samples[i].distance)/Math.Max(.01,radius/2);double w=Math.Exp(-d*d/2);x+=samples[j].point.X*w;y+=samples[j].point.Y*w;p+=samples[j].pressure*w;weight+=w;}
            double blend=Math.Clamp(Math.Min(samples[i].distance,total-samples[i].distance)/radius,0,1);
            result.Add((samples[i].point+(new Point(x/weight,y/weight)-samples[i].point)*blend,(float)(samples[i].pressure+(p/weight-samples[i].pressure)*blend)));
        }
        result[0]=(points[0],pressures[0]);result[^1]=(points[^1],pressures[^1]);return result;
    }
}
