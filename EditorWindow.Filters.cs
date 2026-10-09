using System.Windows;

namespace MeshDraw;
public partial class EditorWindow
{
    private void OpenFilters(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();
        if(SelectionEditing) {Status.Text="Choose a paint or texture layer first. Your selection will limit the filter.";return;}
        if(ActiveLayer is not {Locked:false,Visible:true} layer) {Status.Text="Choose a visible, unlocked layer to apply filters.";return;}
        try
        {
            var dialog=new FilterWindow(projectRoot,LayerPixels(new[]{layer}),canvasWidth,canvasHeight,SelectionMask(ActiveView),FaceName(ActiveView)+" · "+layer.Name) {Owner=this};
            if(dialog.ShowDialog()==true && dialog.Result is {} pixels)
            {
                CommitRaster(layer,pixels);Status.Text="Applied "+dialog.AppliedName+" to "+layer.Name+". Undo restores the original layer.";
            }
        }
        catch(Exception error) {ShowError(error);}
    }
}
