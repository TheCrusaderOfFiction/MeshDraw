using System.Windows;
namespace MeshDraw;
public partial class EditorWindow
{
    private bool previewPanelsActive;
    private GridLength savedToolWidth,savedToolDividerWidth;
    private double savedToolMinimum;
    private void Update3DPanelLayout()
    {
        if(DrawingToolMenu==null)return;
        bool preview=viewLayout=="3d";
        if(preview && !previewPanelsActive)
        {
            CancelGuidedStroke();ColorPopup.IsOpen=false;
            savedToolWidth=LeftPanelColumn.Width;savedToolMinimum=LeftPanelColumn.MinWidth;savedToolDividerWidth=LeftDividerColumn.Width;
            LeftPanelColumn.MinWidth=0;LeftPanelColumn.Width=new GridLength(0);LeftDividerColumn.Width=new GridLength(0);
        }
        else if(!preview && previewPanelsActive)
        {
            LeftPanelColumn.MinWidth=savedToolMinimum;LeftPanelColumn.Width=savedToolWidth;LeftDividerColumn.Width=savedToolDividerWidth;
        }
        if(previewPanelsActive!=preview)BottomSettingsScroll.ScrollToTop();
        previewPanelsActive=preview;
        DrawingToolMenu.Visibility=LeftPanelDivider.Visibility=DrawingBottomSettings.Visibility=preview?Visibility.Collapsed:Visibility.Visible;
        PreviewBottomSettings.Visibility=preview?Visibility.Visible:Visibility.Collapsed;
        FaceSelection.Visibility=ShowCrossFaceEdges.Visibility=ShowOppositeShadow.Visibility=RulerSettings.Visibility=StabilizerSettings.Visibility=preview?Visibility.Collapsed:Visibility.Visible;
        BottomPanelDivider.ToolTip=preview?"Drag up or down to resize 3D settings":"Drag up or down to resize tool settings";
    }
}
