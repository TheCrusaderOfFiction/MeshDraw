using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace MeshDraw;
public partial class EditorWindow
{
    private int vertexLimit=50000;
    private bool changingVertexLimit;
    private void VertexLimitChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(changingVertexLimit || VertexLimitInput==null) return;
        vertexLimit=(int)Math.Round(VertexLimitSlider.Value);VertexLimitInput.Text=vertexLimit.ToString(CultureInfo.InvariantCulture);
    }
    private void CommitVertexLimit(object sender,RoutedEventArgs e)
    {
        if(syncing || changingVertexLimit) return;
        if(int.TryParse(VertexLimitInput.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var count))
            LoadVertexLimit(Math.Clamp(count,256,100000));
        else LoadVertexLimit(vertexLimit);
        RecordChange();
    }
    private void VertexLimitKey(object sender,KeyEventArgs e) {if(e.Key==Key.Enter) CommitVertexLimit(sender,e);}
    private void LoadVertexLimit(int count)
    {
        changingVertexLimit=true;
        try {vertexLimit=count;VertexLimitSlider.Value=count;VertexLimitInput.Text=count.ToString(CultureInfo.InvariantCulture);}
        finally {changingVertexLimit=false;}
    }
}
