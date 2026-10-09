using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MeshDraw;
public partial class EditorWindow
{
    private string? currentDrawingPath;
    private string DrawingLibraryFolder => Path.Combine(projectRoot,"drawings");
    private void UpdateDrawingTitle() => Title="MeshDraw · "+(currentDrawingPath==null ? "Untitled drawing" : Path.GetFileNameWithoutExtension(currentDrawingPath));
    private static void WriteDrawingFile(string path,string state)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(state)>64*1024*1024) throw new InvalidDataException("This drawing exceeds 64 MB. Remove some photo layers before saving.");
        DrawingDocument.Read(state);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,state);File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    private Action<string>? drawingNameAction;
    private string UniqueDrawingPath(string name,string? folder=null)
    {
        name=name.Trim();
        if(string.IsNullOrWhiteSpace(name) || name.Length>80 || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.EndsWith('.')
            || new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(name.Split('.')[0],StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a name without special file characters.");
        folder ??= DrawingLibraryFolder;Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,name+".meshdraw");int number=2;
        while(File.Exists(path)) path=Path.Combine(folder,$"{name} ({number++}).meshdraw");
        return path;
    }
    private bool SaveToGallery(string path)
    {
        try
        {
            RecordChange();var state=Snapshot();WriteDrawingFile(path,state);
            currentDrawingPath=path;savedState=state;UpdateDrawingTitle();
            try {WriteComposite(Path.ChangeExtension(path,".preview.png"),drawingViews["Front"].Where(l=>l.Kind is LayerKind.Artwork or LayerKind.BaseColor or LayerKind.Reference),false);} catch { }
            Status.Text="Saved to My gallery.";return true;
        }
        catch(Exception error) {ShowError(error);return false;}
    }
    private void SaveDrawing(bool copy)
    {
        if(!copy && currentDrawingPath!=null) {SaveToGallery(currentDrawingPath);return;}
        AskDrawingName(copy ? "Save a copy" : "Name your drawing",currentDrawingPath==null ? "Untitled" : Path.GetFileNameWithoutExtension(currentDrawingPath)+" copy",name=>
        {SaveToGallery(UniqueDrawingPath(name));});
    }
    private bool SaveBeforeLeaving()
    {
        if(Snapshot()==savedState) return true;
        return SaveToGallery(currentDrawingPath ?? UniqueDrawingPath("Untitled"));
    }
    private bool CanSwitchDrawing()
    {
        if(generator!=null) {Status.Text="Wait for generation to finish, or cancel it before switching drawings.";return false;}
        return SaveBeforeLeaving();
    }
    private void AskDrawingName(string title,string initial,Action<string> action)
    {
        CreationOptions.Visibility=Visibility.Collapsed;
        DrawingNameTitle.Text=title;DrawingNameInput.Text=initial;DrawingNameError.Text="";drawingNameAction=action;
        DrawingNameOverlay.Visibility=Visibility.Visible;DrawingNameInput.Focus();DrawingNameInput.SelectAll();
    }
    private void AcceptDrawingName(object sender,RoutedEventArgs e)
    {
        try
        {
            _=UniqueDrawingPath(DrawingNameInput.Text); // Validate before changing the current drawing.
            var action=drawingNameAction;action?.Invoke(DrawingNameInput.Text.Trim());drawingNameAction=null;DrawingNameOverlay.Visibility=Visibility.Collapsed;
        }
        catch(Exception error) {DrawingNameError.Text=error.Message;}
    }
    private void CancelDrawingName(object sender,RoutedEventArgs e) {drawingNameAction=null;DrawingNameOverlay.Visibility=Visibility.Collapsed;}
    private void DrawingNameKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter) {AcceptDrawingName(sender,e);e.Handled=true;}
        if(e.Key==Key.Escape) {CancelDrawingName(sender,e);e.Handled=true;}
    }
    private void CreateGalleryDrawing(object sender,RoutedEventArgs e)
    {
        if(!CanSwitchDrawing()) return;
        RefreshSkeletonTypes();NewCanvasWidth.Text="640";NewCanvasHeight.Text="640";NewOutputType.SelectedIndex=0;
        AskDrawingName("New drawing","Untitled",name=>
        {
            if(!int.TryParse(NewCanvasWidth.Text,out var width) || !int.TryParse(NewCanvasHeight.Text,out var height) || width is <128 or >2048 || height is <128 or >2048)
                throw new InvalidDataException("Enter a width and height from 128 to 2048 pixels.");
            var kind=NewOutputType.SelectedIndex;var type=kind==1 ? NewSkeletonType.SelectedItem?.ToString() ?? "Custom" : "None";
            var initialRig=kind==1 ? GetSkeletonType(type) : new RigData([],[]);
            StartDrawing(width,height,kind,type,initialRig);SaveToGallery(UniqueDrawingPath(name));
        });
        CreationOptions.Visibility=Visibility.Visible;CanvasSizePreviewChanged(this,new TextChangedEventArgs(System.Windows.Controls.TextBox.TextChangedEvent,System.Windows.Controls.UndoAction.None));
    }
    private void ImportGalleryDrawing(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Filter="MeshDraw drawing|*.meshdraw"};
        if(dialog.ShowDialog(this)!=true) return;
        OpenDrawingFile(dialog.FileName);
        if(currentDrawingPath==Path.GetFullPath(dialog.FileName)) SaveToGallery(UniqueDrawingPath(Path.GetFileNameWithoutExtension(dialog.FileName)));
    }
    private void ShowEditorScreen()
    {
        GalleryScreen.Visibility=Visibility.Collapsed;EditorScreen.Visibility=Visibility.Visible;UpdateDrawingTitle();
    }
    private void ResetDrawingHistory()
    {
        undo.Clear();redo.Clear();savedState=Snapshot();undo.Add(savedState);
        resultFolder=null;ResultButton.IsEnabled=false;UpdateDrawingTitle();
    }
    private void OpenDrawingFile(string path)
    {
        if(!CanSwitchDrawing()) return;
        try
        {
            if(new FileInfo(path).Length>64*1024*1024) throw new InvalidDataException("Drawing exceeds the 64 MB loading limit.");
            var document=DrawingDocument.Read(File.ReadAllText(path));ApplyDocument(document);RefreshSkeletonTypes();
            currentDrawingPath=Path.GetFullPath(path);ResetDrawingHistory();ShowEditorScreen();Status.Text="Opened "+Path.GetFileNameWithoutExtension(path)+" with all six views, layers, and bones.";
        }
        catch(Exception error) {ShowError(error);}
    }
    private void NewDrawing(object sender,RoutedEventArgs e)
    {
        if(!CanSwitchDrawing()) return;
        StartDrawing(640,640,0,"None",new([],[]));
    }
    private void StartDrawing(int width,int height,int output,string type,RigData initialRig)
    {
        var layers=DrawingDocument.ViewNames.ToDictionary(name=>name,name=>new List<LayerData> {
            DrawingDocument.Data(new PaintLayer {Name="Outline",Kind=LayerKind.Artwork}),
            DrawingDocument.Data(new PaintLayer {Name="Color",Kind=LayerKind.BaseColor}) });
        activeLayers.Clear();selectedBone=null;ApplyDocument(new EditorDocument(2,output,layers,initialRig,width,height,type));
        EditorSkeletonTypes.SelectedItem=skeletonTypes.ContainsKey(type) ? type : "Custom";
        currentDrawingPath=null;ResetDrawingHistory();ShowEditorScreen();Status.Text="New drawing. Returning to the gallery saves your work.";
    }
    private sealed record SavedDrawing(string Path,string Name,string Updated,BitmapSource? Preview);
    private static List<SavedDrawing> ReadDrawingLibrary(string folder)
    {
        if(!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder,"*.meshdraw").Select(path=>new FileInfo(path)).OrderByDescending(f=>f.LastWriteTimeUtc).Select(file=>
        {
            BitmapSource? preview=null;
            try
            {
                var imagePath=Path.ChangeExtension(file.FullName,".preview.png");
                if(File.Exists(imagePath)) {var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=128;image.UriSource=new Uri(imagePath);image.EndInit();image.Freeze();preview=image;}
            }
            catch { /* A missing preview does not prevent opening a drawing. */ }
            return new SavedDrawing(file.FullName,file.Name[..^file.Extension.Length],file.LastWriteTime.ToString("MMM d, yyyy · h:mm tt"),preview);
        }).ToList();
    }
    private void ShowDrawingLibrary(object sender,RoutedEventArgs e)
    {
        if(!CanSwitchDrawing()) return;
        LoadGallery();EditorScreen.Visibility=Visibility.Collapsed;GalleryScreen.Visibility=Visibility.Visible;Title="MeshDraw · My gallery";
    }
    private static void RemoveDrawingFiles(string path,string folder,bool recycle=true)
    {
        path=Path.GetFullPath(path);folder=Path.GetFullPath(folder);
        if(!string.Equals(Path.GetDirectoryName(path),folder,StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(path).Equals(".meshdraw",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only drawings in this gallery can be deleted.");
        void Remove(string file)
        {
            if(!File.Exists(file)) return;
            if(recycle) Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else File.Delete(file);
        }
        Remove(path);Remove(Path.ChangeExtension(path,".preview.png"));
    }
    private void ForgetDeletedDrawing(string path)
    {
        if(!string.Equals(currentDrawingPath,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)) return;
        StartDrawing(640,640,0,"None",new([],[]));
        EditorScreen.Visibility=Visibility.Collapsed;GalleryScreen.Visibility=Visibility.Visible;Title="MeshDraw · My gallery";
    }
    private void DeleteGalleryDrawing(string path)
    {
        if(MessageBox.Show(this,$"Delete '{Path.GetFileNameWithoutExtension(path)}'? The drawing and preview will move to the Recycle Bin.","Delete drawing",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
        try
        {
            RemoveDrawingFiles(path,DrawingLibraryFolder);ForgetDeletedDrawing(path);LoadGallery();GalleryStatus.Text="Drawing deleted. You can restore its files from the Windows Recycle Bin.";
        }
        catch(Exception error)
        {
            if(!File.Exists(path)) ForgetDeletedDrawing(path);
            LoadGallery();ShowError(error);
        }
    }
    private void LoadGallery(string? folder=null)
    {
        try
        {
            var drawings=ReadDrawingLibrary(folder ?? DrawingLibraryFolder);GalleryCards.Children.Clear();
            foreach(var drawing in drawings)
            {
                var content=new StackPanel();
                content.Children.Add(new Border {Background=Brushes.White,CornerRadius=new CornerRadius(8),Child=new Image {Source=drawing.Preview,Width=208,Height=180,Stretch=Stretch.Uniform}});
                content.Children.Add(new TextBlock {Text=drawing.Name,FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,5),TextTrimming=TextTrimming.CharacterEllipsis});
                content.Children.Add(new TextBlock {Text=drawing.Updated,FontSize=11,Foreground=Brushes.LightSlateGray});
                var delete=new Button {Content="Delete",Foreground=Brushes.LightCoral,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0),ToolTip="Delete this drawing"};
                delete.Click+=(_,e)=> {e.Handled=true;DeleteGalleryDrawing(drawing.Path);};content.Children.Add(delete);
                var card=new Button {Content=content,Tag=drawing.Path,Width=240,Padding=new Thickness(14),Margin=new Thickness(0,0,18,18),ToolTip="Open "+drawing.Name,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                card.Click+=(_,_)=>OpenDrawingFile(drawing.Path);GalleryCards.Children.Add(card);
            }
            GalleryEmpty.Visibility=drawings.Count==0 ? Visibility.Visible : Visibility.Collapsed;
            GalleryStatus.Text=$"{drawings.Count} saved drawing{(drawings.Count==1 ? "" : "s")} · Click a drawing to continue. Returning here saves your edits.";
        }
        catch(Exception error) {GalleryStatus.Text="Could not load drawings: "+error.Message;}
    }
}
