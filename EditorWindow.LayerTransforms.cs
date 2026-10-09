using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MeshDraw;
public partial class EditorWindow
{
    private PaintLayer? movingLayer;
    private Point layerMoveStart;
    private Vector layerMoveOffset;
    private void FlipLayer(object sender,RoutedEventArgs e)
    {
        CancelGuidedStroke();
        if(SelectionEditing)
        {
            if(SelectionFor(ActiveView) is { } selection) {var copy=selection.Clone();copy.Transform=new MatrixTransform(((Button)sender).Tag.ToString()=="horizontal" ? new Matrix(-1,0,0,1,canvasWidth,0) : new Matrix(1,0,0,-1,0,canvasHeight));SetSelection(copy);}return;
        }
        if(ActiveLayer is not {Locked:false} layer) {Status.Text="Unlock the layer to flip it.";return;}
        var horizontal=((Button)sender).Tag.ToString()=="horizontal";
        try {TransformLayer(layer,horizontal ? new Matrix(-1,0,0,1,canvasWidth,0) : new Matrix(1,0,0,-1,0,canvasHeight));
        RecordChange();RefreshEditor();Status.Text="Flipped "+layer.Name+" "+(horizontal ? "horizontally." : "vertically.");}
        catch(Exception error) {ShowError(error);}
    }
    private void TransformLayer(PaintLayer layer,Matrix matrix)
    {
        // Keep pen strokes editable; transform a photo separately so paint is not flattened.
        string? photo=null;
        if(layer.Photo!=null)
        {
            var visual=new DrawingVisual();using(var context=visual.RenderOpen())
            {
                context.PushTransform(new MatrixTransform(matrix));var image=layer.Photo;
                double scale=Math.Min(canvasWidth/(double)image.PixelWidth,canvasHeight/(double)image.PixelHeight);
                context.DrawImage(image,new Rect((canvasWidth-image.PixelWidth*scale)/2,(canvasHeight-image.PixelHeight*scale)/2,image.PixelWidth*scale,image.PixelHeight*scale));
            }
            var bitmap=new RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var pixels=new byte[canvasWidth*canvasHeight*4];bitmap.CopyPixels(pixels,canvasWidth*4,0);photo=EncodeRaster(pixels,canvasWidth,canvasHeight);
            _=DrawingDocument.DecodePhoto(photo);
        }
        syncing=true;try {layer.Ink.Strokes.Transform(matrix,false);if(photo!=null) layer.ImageData=photo;} finally {syncing=false;}
    }
    private bool BeginLayerMove(Canvas canvas,Point point)
    {
        if(ActiveLayer is not {Locked:false,Visible:true,Opacity:>0} layer) {Status.Text="Select a visible, unlocked layer to move.";return false;}
        movingLayer=layer;layerMoveStart=point;layerMoveOffset=new Vector();guidedCanvas=canvas;
        canvas.CaptureMouse();canvas.CaptureStylus();return true;
    }
    private void ContinueLayerMove(Point point)
    {
        if(movingLayer==null) return;
        layerMoveOffset=point-layerMoveStart;movingLayer.Ink.RenderTransform=new TranslateTransform(layerMoveOffset.X,layerMoveOffset.Y);
    }
    private void EndLayerMove(Point point)
    {
        ContinueLayerMove(point);var layer=movingLayer;var offset=layerMoveOffset;CancelGuidedStroke();
        if(layer!=null && offset.Length>.01)
        {
            try {TransformLayer(layer,new Matrix(1,0,0,1,offset.X,offset.Y));RecordChange();RefreshEditor();Status.Text="Moved "+layer.Name+".";}
            catch(Exception error) {RefreshEditor();ShowError(error);}
        }
    }
    private void CancelLayerMove()
    {
        if(movingLayer!=null) movingLayer.Ink.RenderTransform=Transform.Identity;
        movingLayer=null;layerMoveOffset=new Vector();
    }
}
