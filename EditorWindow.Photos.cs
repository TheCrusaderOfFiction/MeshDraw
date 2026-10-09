using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MeshDraw;
public partial class EditorWindow
{
    private bool syncingCanvasSize;
    private void CanvasDimensionSliderChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(syncingCanvasSize || NewCanvasWidth==null || NewCanvasHeight==null) return;
        syncingCanvasSize=true;
        try
        {
            var input=ReferenceEquals(sender,CanvasWidthSlider) ? NewCanvasWidth : NewCanvasHeight;
            input.Text=((int)Math.Round(e.NewValue)).ToString();
        }
        finally {syncingCanvasSize=false;}
    }
    private void CanvasSizePreviewChanged(object sender,TextChangedEventArgs e)
    {
        if(CanvasSizePreview==null || CanvasPreviewLabel==null || NewCanvasWidth==null || NewCanvasHeight==null) return;
        if(!int.TryParse(NewCanvasWidth.Text,out var width) || !int.TryParse(NewCanvasHeight.Text,out var height) || width is <128 or >2048 || height is <128 or >2048)
        {CanvasSizePreview.Visibility=Visibility.Collapsed;CanvasPreviewLabel.Text="Enter a size from 128 to 2048 pixels";return;}
        if(!syncingCanvasSize && CanvasWidthSlider!=null && CanvasHeightSlider!=null)
        {
            syncingCanvasSize=true;try {CanvasWidthSlider.Value=width;CanvasHeightSlider.Value=height;} finally {syncingCanvasSize=false;}
        }
        var scale=Math.Min(300d/width,78d/height);CanvasSizePreview.Width=width*scale;CanvasSizePreview.Height=height*scale;
        CanvasSizePreview.Visibility=Visibility.Visible;CanvasPreviewLabel.Text=$"{width} × {height} px · {(width==height ? "Square" : width>height ? "Landscape" : "Portrait")}";
    }
    private static string ReadPhoto(string path)
    {
        if(new FileInfo(path).Length>25*1024*1024) throw new InvalidDataException("Choose a photo under 25 MB.");
        using var file=File.OpenRead(path);
        var decoder=BitmapDecoder.Create(file,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
        var original=decoder.Frames[0];
        if((long)original.PixelWidth*original.PixelHeight>32_000_000) throw new InvalidDataException("Choose a photo with fewer than 32 million pixels.");
        var scale=Math.Min(1,2048d/Math.Max(original.PixelWidth,original.PixelHeight));
        BitmapSource photo=scale<1 ? new TransformedBitmap(original,new ScaleTransform(scale,scale)) : original;
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(photo));using var output=new MemoryStream();encoder.Save(output);
        var data=Convert.ToBase64String(output.ToArray());DrawingDocument.DecodePhoto(data);return data;
    }
    private PaintLayer AddPhotoLayer(string image,string name,LayerKind kind)
    {
        var layers=drawingViews[ActiveView];if(layers.Count>=32) throw new InvalidDataException("This view already has 32 layers.");
        if(kind is not (LayerKind.Reference or LayerKind.BaseColor)) throw new InvalidDataException("Choose Reference or Color for the photo.");
        var layer=Attach(new PaintLayer {Name=(name.Length>65 ? name[..65] : name)+(kind==LayerKind.Reference ? " · reference" : " · photo"),Kind=kind,ImageData=image,Locked=kind==LayerKind.Reference});
        var outline=layers.FindIndex(l=>l.Kind==LayerKind.Artwork);
        layers.Insert(kind==LayerKind.Reference ? layers.Count : outline>=0 ? outline+1 : 0,layer);
        activeLayers[ActiveView]=layer.Id;RecordChange();RefreshEditor();return layer;
    }
    private void ImportPhoto(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Title="Import a photo into "+ActiveView,Filter="Photos|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*"};
        if(dialog.ShowDialog(this)!=true) return;
        try
        {
            var kind=PhotoImportKind.SelectedIndex==1 ? LayerKind.BaseColor : LayerKind.Reference;
            AddPhotoLayer(ReadPhoto(dialog.FileName),Path.GetFileNameWithoutExtension(dialog.FileName),kind);
            Status.Text=kind==LayerKind.Reference ? "Photo added as a locked reference. Select Outline to trace it; adjust the photo layer opacity to fade it." : "Photo added to Color and will be included in the material texture. It fits the canvas without stretching.";
        }
        catch(Exception error) {ShowError(error);}
    }
}
