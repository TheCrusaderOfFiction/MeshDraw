using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshDraw;

public partial class EditorWindow
{
    internal async Task RunChecks(string folder)
    {
        Directory.CreateDirectory(folder);
        static void Assert(bool value,string message) {if(!value) throw new Exception(message);}
        static Stroke Circle()
        {
            var points=new StylusPointCollection(Enumerable.Range(0,121).Select(i=>new StylusPoint(320+220*Math.Cos(i*Math.PI/60),320+220*Math.Sin(i*Math.PI/60))));
            return new Stroke(points) {DrawingAttributes=new DrawingAttributes {Color=Colors.Black,Width=5,Height=5}};
        }
        static LayerData Paint(string name,LayerKind kind,Color color,bool visible=true,bool locked=false,double opacity=1)
        {
            var strokes=new StrokeCollection();
            strokes.Add(new Stroke(new StylusPointCollection {new StylusPoint(230,320),new StylusPoint(410,320)}) {
                DrawingAttributes=new DrawingAttributes {Color=color,Width=260,Height=260}});
            return new(Guid.NewGuid().ToString("N"),name,kind,visible,locked,opacity,DrawingDocument.Encode(strokes));
        }
        Assert(drawingViews.Values.All(l=>l.Count==2 && l[0].Name=="Outline" && l[0].Kind==LayerKind.Artwork && l[1].Name=="Color" && l[1].Kind==LayerKind.BaseColor),"Default outline/color layers missing");
        SelectCoreLayer(new System.Windows.Controls.Button {Tag="1"},new RoutedEventArgs());
        Assert(ActiveLayer!.Kind==LayerKind.BaseColor && drawingViews["Front"][0].Ink.Visibility==Visibility.Visible,"Color workflow hides outline");
        SelectCoreLayer(new System.Windows.Controls.Button {Tag="0"},new RoutedEventArgs());
        Assert(drawingViews["Front"][1].Ink.Visibility==Visibility.Visible,"Outline workflow hides color");
        Assert(Hsv(0,1,1)==Colors.Red && Hsv(120,1,1)==Colors.Lime && Hsv(240,1,1)==Colors.Blue,"Hue picker conversion failed");
        paintHue=240;paintSaturation=1;paintValue=1;CommitSquareColor();
        Assert(BrushHex.Text=="#0000FF","Color square failed");
        SetPaintColor(Colors.Crimson);
        Assert(BrushHex.Text=="#DC143C","Color selection failed");
        BrushPreset(new System.Windows.Controls.Button {Tag="paint"},new RoutedEventArgs());
        Assert(BrushSize.Value==40 && BrushOpacity.Value==.3,"Paint preset failed");
        BrushPreset(new System.Windows.Controls.Button {Tag="pen"},new RoutedEventArgs());
        SelectPaintTool(new System.Windows.Controls.Button {Tag="1"},new RoutedEventArgs());
        Assert(ActiveLayer!.Ink.EditingMode==System.Windows.Controls.InkCanvasEditingMode.EraseByPoint,"Point eraser missing");
        SelectPaintTool(new System.Windows.Controls.Button {Tag="0"},new RoutedEventArgs());
        var legacy=JsonSerializer.Serialize(new {Version=1,OutputType=0,Views=DrawingDocument.ViewNames.ToDictionary(name=>name,name=>DrawingDocument.Encode(new StrokeCollection {Circle()}))});
        var migrated=DrawingDocument.Read(legacy);
        Assert(migrated.Version==2 && migrated.Layers.Values.All(l=>l.Count==1 && l[0].Kind==LayerKind.Artwork),"Legacy migration failed");
        var layerViews=DrawingDocument.ViewNames.ToDictionary(name=>name,name=>new List<LayerData> {
            Paint("Hidden green",LayerKind.BaseColor,Colors.Lime,visible:false),
            Paint("Bump",LayerKind.Height,Color.FromRgb(210,210,210)),
            Paint("Glow",LayerKind.Emission,Colors.OrangeRed),
            Paint("Metal",LayerKind.Metallic,Color.FromRgb(230,230,230)),
            Paint("Rough",LayerKind.Roughness,Color.FromRgb(64,64,64)),
            Paint("Paint",LayerKind.BaseColor,Colors.Crimson,locked:true,opacity:.75),
            migrated.Layers[name][0] });
        var custom=new RigData([new("a",.5,.1,.5),new("b",.5,.5,.5),new("c",.65,.85,.5)],
            [new("bone-a","drawn_spine","a","b",null),new("bone-b","drawn_branch","b","c","bone-a")]);
        var fixture=new EditorDocument(2,1,layerViews,custom);
        var roundtrip=DrawingDocument.Read(JsonSerializer.Serialize(fixture));
        Assert(roundtrip.Layers["Front"][5].Locked && roundtrip.Layers["Front"][5].Opacity==.75 && roundtrip.Skeleton.Bones[1].Parent=="bone-a","Project roundtrip lost state");
        var invalid=custom with {Bones=[custom.Bones[0] with {Parent="bone-b"},custom.Bones[1]]};
        bool rejected=false; try {DrawingDocument.ValidateRig(invalid);} catch(InvalidDataException) {rejected=true;} Assert(rejected,"Cyclic skeleton accepted");
        foreach(var view in DrawingDocument.ViewNames)
        {
            var joint=new JointData("check",.2,.4,.7); var point=ProjectJoint(joint,view); var result=MoveJoint(joint,point,view);
            Assert(Math.Abs(joint.X-result.X)+Math.Abs(joint.Y-result.Y)+Math.Abs(joint.Z-result.Z)<1e-8,"Joint projection mismatch: "+view);
        }
        Assert(HumanoidGuide().Bones.Count==19,"Humanoid guide is incomplete");
        ApplyDocument(fixture); undo.Clear(); redo.Clear(); undo.Add(Snapshot());
        var before=drawingViews["Front"].Count;
        NewLayerKind.SelectedIndex=0; AddLayer(this,new RoutedEventArgs());
        Assert(drawingViews["Front"].Count==before+1,"Layer add failed");
        Undo(this,new RoutedEventArgs()); Assert(drawingViews["Front"].Count==before,"Undo layer add failed");
        Redo(this,new RoutedEventArgs()); Assert(drawingViews["Front"].Count==before+1,"Redo layer add failed");
        activeLayers["Front"]=drawingViews["Front"][0].Id; RefreshEditor();
        ActiveLayer!.Ink.Strokes.Add(Circle());
        Assert(ActiveLayer!.Ink.Strokes.Count==1,"Stroke add failed");
        Undo(this,new RoutedEventArgs()); Assert(ActiveLayer!.Ink.Strokes.Count==0,"Undo stroke failed");
        Redo(this,new RoutedEventArgs()); Assert(ActiveLayer!.Ink.Strokes.Count==1,"Redo stroke failed");
        activeLayers["Front"]=drawingViews["Front"].First(l=>l.Name=="Paint").Id; RefreshEditor();
        Assert(!ActiveLayer!.Ink.IsHitTestVisible,"Locked layer accepts ink");
        ApplyDocument(fixture); WriteDrawings(folder);
        static byte[] Pixel(string path,int x,int y)
        {
            var decoder=BitmapDecoder.Create(new Uri(path),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            var image=new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Bgra32,null,0);
            var pixel=new byte[4]; image.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0); return pixel;
        }
        var art=Pixel(Path.Combine(folder,"front.png"),320,320);
        Assert(art[0]>250 && art[1]>250 && art[2]>250,"Material paint leaked into geometry");
        var color=Pixel(Path.Combine(folder,"front_basecolor.png"),320,320);
        Assert(color[2]>180 && color[1]<80 && color[3] is >=185 and <=195,"Opacity or hidden-layer compositing failed");
        var roughness=Pixel(Path.Combine(folder,"front_roughness.png"),320,320);
        Assert(Math.Abs(roughness[0]-64)<3,"Roughness was not preserved");
        Assert(Pixel(Path.Combine(folder,"front_basecolor.png"),0,0)[3]==0,"Material background is not transparent");
        var alpha=DrawingDocument.Layer(Paint("Alpha brush",LayerKind.BaseColor,Color.FromArgb(128,255,0,0)));
        WriteComposite(Path.Combine(folder,"alpha-brush.png"),[alpha],true);
        var alphaPixel=Pixel(Path.Combine(folder,"alpha-brush.png"),320,320);
        Assert(alphaPixel[3] is >=125 and <=130,"Brush opacity is not applied to strokes");
        var libraryCheck=Path.Combine(folder,"saved-drawings");
        Directory.CreateDirectory(libraryCheck);
        var firstDrawing=Path.Combine(libraryCheck,"Creature.meshdraw");var secondDrawing=Path.Combine(libraryCheck,"Prop.meshdraw");
        var firstState=Snapshot();WriteDrawingFile(firstDrawing,firstState);
        WriteDrawingFile(secondDrawing,System.Text.Json.JsonSerializer.Serialize(fixture with {OutputType=0,Skeleton=new([],[])}));
        Assert(ReadDrawingLibrary(libraryCheck).Count==2,"Multiple saved drawings missing");
        Assert(File.ReadAllText(firstDrawing)==firstState && DrawingDocument.Read(File.ReadAllText(secondDrawing)).Skeleton.Bones.Count==0,"Saved drawings overwrite each other");
        savedState=Snapshot();OpenDrawingFile(secondDrawing);
        Assert(rig.Bones.Count==0 && Output.SelectedIndex==0,"Opened wrong saved drawing");
        NewDrawing(this,new RoutedEventArgs());
        Assert(currentDrawingPath==null && drawingViews.Values.All(l=>l.Count==2 && l.All(p=>p.Ink.Strokes.Count==0)),"New drawing retains prior strokes");
        OpenDrawingFile(firstDrawing);Assert(rig.Bones.Count==2,"Saved skeleton failed to reopen");
        ApplyDocument(fixture);savedState=Snapshot();
        HueSlider.Value=120;HueSlider.ApplyTemplate();HueSlider.Measure(new Size(260,30));HueSlider.Arrange(new Rect(0,0,260,30));HueSlider.UpdateLayout();
        Assert(HueSlider.Template.FindName("PART_Track",HueSlider) is System.Windows.Controls.Primitives.Track,"Hue slider track missing");
        var hueImage=new RenderTargetBitmap(260,30,96,96,PixelFormats.Pbgra32);hueImage.Render(HueSlider);
        var huePng=new PngBitmapEncoder();huePng.Frames.Add(BitmapFrame.Create(hueImage));
        using(var stream=File.Create(Path.Combine(folder,"hue-slider.png"))) huePng.Save(stream);
        var hueRed=Pixel(Path.Combine(folder,"hue-slider.png"),12,15);var hueBlue=Pixel(Path.Combine(folder,"hue-slider.png"),170,15);
        Assert(hueRed[2]>180 && hueRed[0]<80 && hueBlue[0]>180,"Hue rainbow not rendered");
        Assert(Path.GetFileName(UniqueDrawingPath("Creature",libraryCheck))!="Creature.meshdraw","Gallery copy would overwrite a drawing");
        bool badName=false;try {UniqueDrawingPath("../escape",libraryCheck);} catch(InvalidDataException) {badName=true;}Assert(badName,"Invalid gallery name accepted");
        AskDrawingName("New drawing","Cancel me",_=>throw new Exception("Cancelled name action ran"));
        CancelDrawingName(this,new RoutedEventArgs());Assert(DrawingNameOverlay.Visibility==Visibility.Collapsed,"Name panel did not close");
        currentDrawingPath=firstDrawing;savedState=Snapshot();
        activeLayers["Front"]=drawingViews["Front"].First(l=>l.Kind==LayerKind.Artwork).Id;RefreshEditor();
        ActiveLayer!.Ink.Strokes.Add(Circle());var autosavedState=Snapshot();
        ShowDrawingLibrary(this,new RoutedEventArgs());
        Assert(File.ReadAllText(firstDrawing)==autosavedState && GalleryScreen.Visibility==Visibility.Visible && EditorScreen.Visibility==Visibility.Collapsed,"Return to gallery failed to save");
        LoadGallery(libraryCheck);Assert(GalleryCards.Children.Count==2,"Gallery cards missing");
        var propCard=GalleryCards.Children.OfType<System.Windows.Controls.Button>().Single(b=>(string)b.Tag==secondDrawing);
        propCard.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert(currentDrawingPath==secondDrawing && rig.Bones.Count==0 && EditorScreen.Visibility==Visibility.Visible && GalleryScreen.Visibility==Visibility.Collapsed,"Clicking gallery card did not open drawing");
        ShowDrawingLibrary(this,new RoutedEventArgs());LoadGallery(libraryCheck);
        var galleryContent=(FrameworkElement)Content;
        galleryContent.Measure(new Size(1408,880));galleryContent.Arrange(new Rect(0,0,1408,880));galleryContent.UpdateLayout();
        var galleryImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);galleryImage.Render(galleryContent);
        var galleryPng=new PngBitmapEncoder();galleryPng.Frames.Add(BitmapFrame.Create(galleryImage));
        using(var stream=File.Create(Path.Combine(folder,"gallery-preview.png"))) galleryPng.Save(stream);
        ShowEditorScreen();ApplyDocument(fixture);savedState=Snapshot();
        foreach(var view in DrawingDocument.ViewNames)
        {
            var joint=new JointData("rect",.23,.41,.72);var point=ProjectJoint(joint,view,1024,512);var moved=MoveJoint(joint,point,view,1024,512);
            Assert(Math.Abs(joint.X-moved.X)+Math.Abs(joint.Y-moved.Y)+Math.Abs(joint.Z-moved.Z)<1e-8,"Rectangular joint projection failed");
        }
        RefreshSkeletonTypes();foreach(var type in new[]{"Humanoid","Quadruped","Winged","Serpent"})
        {var template=GetSkeletonType(type);DrawingDocument.ValidateRig(template);Assert(template.Bones.Count>0,"Empty skeleton preset");template.Bones.Clear();Assert(GetSkeletonType(type).Bones.Count>0,"Preset mutated through drawing");}
        var customTypePath=Path.Combine(folder,"types","Creature.skeleton.json");WriteSkeletonPreset(customTypePath,"Creature type",custom);
        Assert(ReadSkeletonPreset(customTypePath).Skeleton.Bones[1].Parent=="bone-a","Reusable skeleton type lost hierarchy");
        var oldV2=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(fixture))!.AsObject();oldV2.Remove("CanvasWidth");oldV2.Remove("CanvasHeight");oldV2.Remove("SkeletonType");
        Assert(DrawingDocument.Read(oldV2.ToJsonString()).CanvasWidth==640,"Old canvas dimensions not migrated");
        bool badSize=false;try {DrawingDocument.Validate(fixture with {CanvasWidth=4096});} catch(InvalidDataException) {badSize=true;}Assert(badSize,"Oversized canvas accepted");
        StartDrawing(1024,512,1,"Quadruped",GetSkeletonType("Quadruped"));
        Assert(Output.SelectedIndex==1 && canvasWidth==1024 && canvasHeight==512 && surfaces.Values.All(s=>s.Width==1024 && s.Height==512),"Creation settings not applied");
        Assert(drawingViews.Values.All(l=>l.All(p=>p.Ink.Width==1024 && p.Ink.Height==512)),"Layer canvas size incorrect");
        var rectRoundtrip=DrawingDocument.Read(Snapshot());Assert(rectRoundtrip.CanvasWidth==1024 && rectRoundtrip.CanvasHeight==512 && rectRoundtrip.SkeletonType=="Quadruped" && rectRoundtrip.Skeleton.Bones.Count==18,"Creation settings did not persist");
        foreach(var layers in drawingViews.Values)
        {
            var points=new StylusPointCollection(Enumerable.Range(0,121).Select(i=>new StylusPoint(512+320*Math.Cos(i*Math.PI/60),256+160*Math.Sin(i*Math.PI/60))));
            layers[0].Ink.Strokes.Add(new Stroke(points) {DrawingAttributes=new DrawingAttributes {Color=Colors.Black,Width=6,Height=6}});
        }
        var rectExport=Path.Combine(folder,"rectangular");Directory.CreateDirectory(rectExport);WriteDrawings(rectExport);
        var rectImage=BitmapDecoder.Create(new Uri(Path.Combine(rectExport,"front.png")),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        Assert(rectImage.PixelWidth==1024 && rectImage.PixelHeight==512,"Rectangular export dimensions incorrect");
        savedState=Snapshot();StartDrawing(512,1024,0,"None",new([],[]));Assert(Output.SelectedIndex==0 && rig.Bones.Count==0,"Static creation retained bones");
        ApplyDocument(fixture);savedState=Snapshot();
        CreateGalleryDrawing(this,new RoutedEventArgs());NewCanvasWidth.Text="1024";NewCanvasHeight.Text="512";NewOutputType.SelectedIndex=1;NewSkeletonType.SelectedItem="Quadruped";
        Assert(CreationOptions.Visibility==Visibility.Visible && CreationRigOptions.Visibility==Visibility.Visible,"Creation choices not shown");
        var creationContent=(FrameworkElement)Content;creationContent.Measure(new Size(1408,880));creationContent.Arrange(new Rect(0,0,1408,880));creationContent.UpdateLayout();
        var creationImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);creationImage.Render(creationContent);var creationPng=new PngBitmapEncoder();creationPng.Frames.Add(BitmapFrame.Create(creationImage));
        using(var stream=File.Create(Path.Combine(folder,"creation-preview.png"))) creationPng.Save(stream);
        CancelDrawingName(this,new RoutedEventArgs());
        NewCanvasWidth.Text="1200";NewCanvasHeight.Text="600";
        Assert(Math.Abs(CanvasSizePreview.Width/CanvasSizePreview.Height-2)<.001 && CanvasPreviewLabel.Text.Contains("Landscape"),"Live canvas preview ratio incorrect");
        NewCanvasWidth.Text="invalid";Assert(CanvasSizePreview.Visibility==Visibility.Collapsed,"Invalid canvas preview shown");NewCanvasWidth.Text="600";NewCanvasHeight.Text="1200";
        Assert(CanvasPreviewLabel.Text.Contains("Portrait") && Math.Abs(CanvasSizePreview.Width/CanvasSizePreview.Height-.5)<.001,"Portrait preview incorrect");
        CanvasWidthSlider.Value=1536;CanvasHeightSlider.Value=768;
        Assert(NewCanvasWidth.Text=="1536" && NewCanvasHeight.Text=="768" && CanvasPreviewLabel.Text.Contains("1536 × 768"),"Canvas sliders did not update size and preview");
        NewCanvasWidth.Text="800";NewCanvasHeight.Text="1200";
        Assert(CanvasWidthSlider.Value==800 && CanvasHeightSlider.Value==1200,"Typed sizes did not sync canvas sliders");
        var sourcePixels=Enumerable.Repeat(new byte[]{180,30,240,255},64*32).SelectMany(x=>x).ToArray();
        var sourcePhoto=BitmapSource.Create(64,32,96,96,PixelFormats.Bgra32,null,sourcePixels,64*4);var photoEncoder=new PngBitmapEncoder();photoEncoder.Frames.Add(BitmapFrame.Create(sourcePhoto));
        var photoPath=Path.Combine(folder,"import-photo.png");using(var stream=File.Create(photoPath)) photoEncoder.Save(stream);
        var photoData=ReadPhoto(photoPath);
        ApplyDocument(fixture);undo.Clear();redo.Clear();undo.Add(Snapshot());
        var photoExport=Path.Combine(folder,"photo-export");Directory.CreateDirectory(photoExport);
        WriteComposite(Path.Combine(photoExport,"shape-before.png"),drawingViews["Front"].Where(l=>l.Kind==LayerKind.Artwork),false);
        AddPhotoLayer(photoData,"Reference photo",LayerKind.Reference);
        Assert(ActiveLayer!.Locked && ActiveLayer.Photo?.PixelWidth==64,"Reference import failed");
        WriteDrawings(photoExport);
        Assert(File.ReadAllBytes(Path.Combine(photoExport,"shape-before.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(photoExport,"front.png"))),"Reference photo leaked into geometry");
        Undo(this,new RoutedEventArgs());Assert(!drawingViews["Front"].Any(l=>l.Kind==LayerKind.Reference),"Undo photo import failed");
        Redo(this,new RoutedEventArgs());Assert(drawingViews["Front"].Any(l=>l.ImageData==photoData),"Redo lost photo data");
        AddPhotoLayer(photoData,"Texture photo",LayerKind.BaseColor);
        var photoRoundtrip=DrawingDocument.Read(Snapshot());Assert(photoRoundtrip.Layers["Front"].Count(l=>l.Image==photoData)==2,"Saved photos missing");
        WriteDrawings(photoExport);var paintedPhoto=Pixel(Path.Combine(photoExport,"front_basecolor.png"),80,320);
        Assert(paintedPhoto[2]>230 && paintedPhoto[1]<40 && paintedPhoto[0]>170,"Photo color export failed");
        Assert(Pixel(Path.Combine(photoExport,"front_basecolor.png"),320,10)[3]==0,"Photo was stretched to fill the canvas");
        DuplicateLayer(this,new RoutedEventArgs());Assert(ActiveLayer!.ImageData==photoData,"Duplicate lost photo");
        ApplyDocument(fixture);savedState=Snapshot();
        var deleteTestPath=Path.Combine(libraryCheck,"Delete test.meshdraw");WriteDrawingFile(deleteTestPath,Snapshot());
        var deletePreviewPath=Path.ChangeExtension(deleteTestPath,".preview.png");File.Copy(photoPath,deletePreviewPath,true);
        currentDrawingPath=deleteTestPath;RemoveDrawingFiles(deleteTestPath,libraryCheck,false);ForgetDeletedDrawing(deleteTestPath);
        Assert(!File.Exists(deleteTestPath) && !File.Exists(deletePreviewPath) && currentDrawingPath==null && Snapshot()==savedState,"Drawing deletion retains files or stale autosave state");
        Assert(File.Exists(firstDrawing) && File.Exists(secondDrawing),"Deleting one drawing removed others");
        bool outsideDeleteRejected=false;try {RemoveDrawingFiles(firstDrawing,Path.Combine(libraryCheck,"other"),false);} catch(InvalidDataException) {outsideDeleteRejected=true;}Assert(outsideDeleteRejected,"Deletion allowed outside gallery");
        ApplyDocument(fixture);savedState=Snapshot();ShowEditorScreen();
        SetViewLayout("all");Assert(AllViews.Children.Count==6 && Views.Visibility==Visibility.Collapsed && multiViewHosts.Values.All(h=>h.Child!=null),"All views not mounted");
        SelectMultiView("Right");Assert(ActiveView=="Right" && Layers.Items.OfType<PaintLayer>().SequenceEqual(drawingViews["Right"]) && Layers.Items.OfType<SelectionLayerRow>().Count()==1,"Multi-view selection did not update layer editor");
        var allContent=(FrameworkElement)Content;allContent.Measure(new Size(1408,880));allContent.Arrange(new Rect(0,0,1408,880));allContent.UpdateLayout();
        var allImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);allImage.Render(allContent);var allPng=new PngBitmapEncoder();allPng.Frames.Add(BitmapFrame.Create(allImage));
        using(var stream=File.Create(Path.Combine(folder,"all-views-preview.png"))) allPng.Save(stream);
        var plane=OutlinePlanePoint("Back",.2,.3);Assert(Math.Abs(plane.X-.6)<1e-8 && Math.Abs(plane.Y-.4)<1e-8 && plane.Z==-1,"Back plane alignment incorrect");
        var noFill=DrawingDocument.ViewNames.ToDictionary(n=>n,n=>new bool[8,8]);BuildOutlineOverlap(noFill,8);Assert(previewOccupiedCells==0,"Empty outlines create preview volume");
        var filledMask=new bool[8,8];for(int x=0;x<8;x++) for(int y=0;y<8;y++) filledMask[x,y]=true;
        var fullMasks=DrawingDocument.ViewNames.ToDictionary(n=>n,n=>filledMask);var cube=BuildOutlineOverlap(fullMasks,8);Assert(previewOccupiedCells==512 && cube.TriangleIndices.Count>0,"Complete silhouette overlap failed");
        fullMasks["Top"]=new bool[8,8];BuildOutlineOverlap(fullMasks,8);Assert(previewOccupiedCells==0,"Top drawing did not constrain preview");
        SetViewLayout("3d");await previewLoadTask;Assert(OutlineViewport.Children.Count==1 && previewOccupiedCells>0,"Closed outline 3D preview missing");
        var threeContent=(FrameworkElement)Content;threeContent.Measure(new Size(1408,880));threeContent.Arrange(new Rect(0,0,1408,880));threeContent.UpdateLayout();
        var threeImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);threeImage.Render(threeContent);var threePng=new PngBitmapEncoder();threePng.Frames.Add(BitmapFrame.Create(threeImage));
        using(var stream=File.Create(Path.Combine(folder,"3d-outline-preview.png"))) threePng.Save(stream);
        SetViewLayout("single");Assert(Views.Visibility==Visibility.Visible && Views.Items.OfType<System.Windows.Controls.TabItem>().All(t=>((System.Windows.Controls.Viewbox)t.Content).Child!=null),"Switching back lost drawing canvases");
        Views.SelectedIndex=0;ApplyDocument(fixture);savedState=Snapshot();
        var linePoint=ConstrainRulerPoint(new Point(120,250),new Point(100,100),1,0,50);Assert(linePoint==new Point(120,100),"Straight ruler constraint failed");
        var circlePoint=ConstrainRulerPoint(new Point(120,250),new Point(100,100),2,0,50);Assert(Math.Abs((circlePoint-new Point(100,100)).Length-50)<1e-8,"Circular ruler constraint failed");
        var fadeStroke=CreateGuidedStroke(Enumerable.Range(0,11).Select(i=>new Point(i*10,100)).ToList(),new DrawingAttributes {Width=10,Height=10},true,.2);
        Assert(fadeStroke.StylusPoints[0].PressureFactor<.1 && fadeStroke.StylusPoints[5].PressureFactor>.45 && fadeStroke.StylusPoints[^1].PressureFactor<.1,"Fade ends missing");
        var pressureStroke=CreateGuidedStroke(new[]{new Point(0,0),new Point(100,100)},new DrawingAttributes(),false,.2,new[]{.2f,.8f});
        Assert(Math.Abs(pressureStroke.StylusPoints[0].PressureFactor-.2)<.001 && Math.Abs(pressureStroke.StylusPoints[1].PressureFactor-.8)<.001,"Pen pressure lost");
        var fadeSaved=DrawingDocument.Decode(DrawingDocument.Encode(new StrokeCollection {fadeStroke}));Assert(fadeSaved[0].StylusPoints[0].PressureFactor<.1,"Fade not preserved in saved strokes");
        activeLayers["Front"]=drawingViews["Front"].First(l=>l.Kind==LayerKind.Artwork).Id;RefreshEditor();ShowRig.IsChecked=false;
        RulerMode.SelectedIndex=1;RulerAngle.Value=0;StrokeStabilizer.Value=10;StrokeFade.IsChecked=true;
        undo.Clear();redo.Clear();undo.Add(Snapshot());var beforeGuided=ActiveLayer!.Ink.Strokes.Count;
        Assert(BeginGuidedStroke(overlays["Front"],new Point(120,250)),"Guided brush did not start");ContinueGuidedStroke(new Point(250,380));
        Assert(guidedPoints[^1].X<250,"Stabilizer did not smooth movement");EndGuidedStroke(new Point(300,420));
        Assert(ActiveLayer!.Ink.Strokes.Count==beforeGuided+1 && ActiveLayer.Ink.Strokes[^1].StylusPoints.All(p=>Math.Abs(p.Y-250)<.001),"Ruler brush commit failed");
        Undo(this,new RoutedEventArgs());Assert(ActiveLayer!.Ink.Strokes.Count==beforeGuided,"Guided stroke undo failed");
        RulerMode.SelectedIndex=0;StrokeStabilizer.Value=0;StrokeFade.IsChecked=false;ShowRig.IsChecked=true;
        foreach(var (width,height) in new[]{(2048,2048),(2048,512),(512,2048)})
        {
            StartDrawing(width,height,0,"None",new([],[]));
            foreach(var layers in drawingViews.Values)
            {
                var points=new StylusPointCollection(Enumerable.Range(0,241).Select(i=>new StylusPoint(width*(.5+.35*Math.Cos(i*Math.PI/120)),height*(.5+.35*Math.Sin(i*Math.PI/120)))));
                layers[0].Ink.Strokes.Add(new Stroke(points) {DrawingAttributes=new DrawingAttributes {Color=Colors.Black,Width=1,Height=1}});
            }
            SetViewLayout("3d");Assert(PreviewLoadingOverlay.Visibility==Visibility.Visible && !PreviewRefreshButton.IsEnabled,"Preview loading indicator not shown");
            await previewLoadTask;
            Assert(previewOccupiedCells>0 && PreviewLoadingOverlay.Visibility==Visibility.Collapsed && PreviewRefreshButton.IsEnabled,"Large thin-outline preview failed");
            RefreshOutlinePreview(this,new RoutedEventArgs());await previewLoadTask;Assert(PreviewLoadingBar.Value==100,"Refresh progress did not finish");
        }
        SetViewLayout("single");ApplyDocument(fixture);savedState=Snapshot();
        rulerCenters["Front"]=new Point(.2,.3);ResetRulerPosition(this,new RoutedEventArgs());Assert(RulerCenter("Front")==new Point(320,320),"Ruler reset failed");
        RulerAngleInput.Text="45";RulerNumberCommitted(RulerAngleInput,new RoutedEventArgs());Assert(RulerAngle.Value==45,"Angle box not applied");
        RulerRadiusInput.Text="160";RulerNumberCommitted(RulerRadiusInput,new RoutedEventArgs());Assert(Math.Abs(RulerRadius.Value-.25)<.001,"Radius box not applied");
        RulerAngle.Value=-30;Assert(RulerAngleInput.Text=="-30","Ruler slider not synced with box");
        RulerRadiusInput.Text="invalid";RulerNumberCommitted(RulerRadiusInput,new RoutedEventArgs());Assert(RulerRadiusInput.Text=="160","Invalid ruler input not recovered");
        var fillPixels=new byte[12*12*4];var boundaries=new byte[fillPixels.Length];
        for(int i=2;i<=9;i++) foreach(var (x,y) in new[]{(i,2),(i,9),(2,i),(9,i)}) boundaries[(y*12+x)*4+3]=255;
        Assert(FloodFillPixels(fillPixels,boundaries,12,12,5,5,Colors.Red,10)==36 && fillPixels[(5*12+5)*4+2]==255 && fillPixels[3]==0,"Bucket leaked across outline");
        var blurPixels=new byte[20*20*4];for(int y=0;y<20;y++) for(int x=0;x<10;x++) {int i=(y*20+x)*4;blurPixels[i+2]=255;blurPixels[i+3]=255;}
        Assert(ApplyRasterDab(blurPixels,20,20,new Point(10,10),new Point(10,10),6,1,4) && blurPixels[(10*20+10)*4+3]>0 && blurPixels[(10*20+10)*4+3]<255,"Blur did not soften paint edge");
        var smudgePixels=new byte[20*20*4];for(int y=0;y<20;y++) for(int x=0;x<10;x++) {int i=(y*20+x)*4;smudgePixels[i+1]=255;smudgePixels[i+3]=255;}
        Assert(ApplyRasterDab(smudgePixels,20,20,new Point(12,10),new Point(8,10),5,1,5) && smudgePixels[(10*20+12)*4+1]>0,"Smudge did not carry color");
        Tool.SelectedIndex=6;activeLayers["Front"]=drawingViews["Front"].First(l=>l.Name=="Paint").Id;RefreshEditor();Assert(!BeginRasterGesture(overlays["Front"],new Point(320,320)),"Locked layer accepts bucket");
        SelectCoreLayer(new System.Windows.Controls.Button {Tag="1"},new RoutedEventArgs());ActiveLayer!.Locked=false;ActiveLayer.Visible=true;ActiveLayer.Opacity=1;RefreshEditor();ActiveLayer.Ink.Strokes.Clear();
        undo.Clear();redo.Clear();undo.Add(Snapshot());var rasterBefore=Snapshot();Tool.SelectedIndex=6;SetPaintColor(Colors.Blue);BeginRasterGesture(overlays["Front"],new Point(320,320));
        Assert(ActiveLayer!.Photo!=null && ActiveLayer.Ink.Strokes.Count==0,"Bucket result not saved on selected layer");
        var rasterState=Snapshot();Undo(this,new RoutedEventArgs());Assert(Snapshot()==rasterBefore,"Bucket undo failed");Redo(this,new RoutedEventArgs());Assert(Snapshot()==rasterState,"Bucket redo failed");
        Assert(DrawingDocument.Read(rasterState).Layers["Front"].Any(l=>l.Image!=null),"Raster tool result missing from project");
        var rasterExport=Path.Combine(folder,"raster-export");Directory.CreateDirectory(rasterExport);WriteDrawings(rasterExport);Assert(Pixel(Path.Combine(rasterExport,"front_basecolor.png"),320,320)[0]>240,"Bucket color not exported");
        Tool.SelectedIndex=4;var selectedRaster=ActiveLayer!;var selectedRasterState=Snapshot();BeginRasterGesture(overlays["Front"],new Point(100,320));ContinueRasterGesture(new Point(110,320));EndRasterGesture(new Point(120,320));
        Assert(rasterPixels==null && rasterDisplay==null && selectedRaster.Ink.Visibility==Visibility.Visible,"Blur left stale preview state");
        if(Snapshot()!=selectedRasterState) {Undo(this,new RoutedEventArgs());Assert(Snapshot()==selectedRasterState,"Blur gesture undo failed");}
        RulerMode.SelectedIndex=0;RulerAngle.Value=0;Tool.SelectedIndex=0;ApplyDocument(fixture);savedState=Snapshot();
        activeLayers["Front"]=drawingViews["Front"].First(l=>l.Kind==LayerKind.Artwork).Id;RefreshEditor();Tool.SelectedIndex=0;StrokeStabilizer.Value=0;StrokeFade.IsChecked=false;PositionRuler.IsChecked=false;
        RulerMode.SelectedIndex=1;RulerAngle.Value=0;ResetRulerPosition(this,new RoutedEventArgs());
        var rulerStrokeCount=ActiveLayer!.Ink.Strokes.Count;
        Assert(BeginGuidedStroke(overlays["Front"],RulerCenter("Front")) && movingRuler,"Center handle not detected");EndGuidedStroke(new Point(280,260));
        Assert(RulerCenter("Front")==new Point(280,260) && ActiveLayer!.Ink.Strokes.Count==rulerStrokeCount,"Center drag painted or failed to move");
        var ends=StraightRulerEnds("Front");Assert(BeginGuidedStroke(overlays["Front"],ends.b) && rulerDragHandle==3,"Straight end handle missing");EndGuidedStroke(new Point(480,380));
        var rotatedEnds=StraightRulerEnds("Front");Assert((rotatedEnds.a-ends.a).Length<.001 && (rotatedEnds.b-new Point(480,380)).Length<.001,"Straight end drag failed to preserve other endpoint");
        Assert(BeginGuidedStroke(overlays["Front"],rotatedEnds.a) && rulerDragHandle==2,"Other straight end handle missing");EndGuidedStroke(new Point(140,200));
        Assert((StraightRulerEnds("Front").b-rotatedEnds.b).Length<.001,"Dragging first endpoint moved second endpoint");
        ResetRulerPosition(this,new RoutedEventArgs());RulerAngle.Value=0;
        BeginGuidedStroke(overlays["Front"],new Point(130,150));ContinueGuidedStroke(new Point(350,200));EndGuidedStroke(new Point(450,240));
        Assert(ActiveLayer!.Ink.Strokes[^1].StylusPoints.All(p=>Math.Abs(p.Y-150)<.001),"Straight ruler did not apply across canvas");
        RulerMode.SelectedIndex=2;RulerRadius.Value=.3;ResetRulerPosition(this,new RoutedEventArgs());
        Assert(BeginGuidedStroke(overlays["Front"],CircleRulerHandle("Front")) && rulerDragHandle==4,"Circular edge handle missing");EndGuidedStroke(new Point(320,560));
        Assert(Math.Abs(RulerRadius.Value*640-240)<.001 && (CircleRulerHandle("Front")-new Point(320,560)).Length<.001,"Circle radius drag failed");
        BeginGuidedStroke(overlays["Front"],CircleRulerHandle("Front"));EndGuidedStroke(new Point(600,600));
        Assert((CircleRulerHandle("Front")-new Point(600,600)).Length<.001,"Circle handle clamped before requested point");
        var circleStart=new Point(220,220);var drawnRadius=(circleStart-RulerCenter("Front")).Length;
        BeginGuidedStroke(overlays["Front"],circleStart);ContinueGuidedStroke(new Point(400,250));EndGuidedStroke(new Point(420,400));
        Assert(ActiveLayer!.Ink.Strokes[^1].StylusPoints.All(p=>Math.Abs((new Point(p.X,p.Y)-RulerCenter("Front")).Length-drawnRadius)<.01),"Circular ruler did not use stroke start radius");
        Assert(Math.Abs(drawnRadius-RulerRadius.Value*640)>20,"Concentric stroke test started on guide edge");
        RulerMode.SelectedIndex=0;RulerAngle.Value=0;ApplyDocument(fixture);savedState=Snapshot();
        Tool.SelectedIndex=0;BrushSize.Value=17;BrushOpacity.Value=.35;
        Tool.SelectedIndex=4;BrushSize.Value=43;BrushOpacity.Value=.8;
        Assert(RasterSettings.Visibility==Visibility.Visible && PenSettings.Visibility==Visibility.Collapsed,"Blur settings panel");
        Tool.SelectedIndex=0;Assert(BrushSize.Value==17 && Math.Abs(BrushOpacity.Value-.35)<.001,"Pen settings were global");
        Tool.SelectedIndex=4;Assert(BrushSize.Value==43 && Math.Abs(BrushOpacity.Value-.8)<.001,"Blur settings not restored");
        Tool.SelectedIndex=0;BrushOpacity.Value=1;
        selectedFaces.Add("Back");selectedFaces.Add("Left");RefreshEditor();
        undo.Clear();redo.Clear();undo.Add(Snapshot());var multiBefore=Snapshot();
        var backCount=drawingViews["Back"].First(l=>l.Kind==LayerKind.Artwork).Ink.Strokes.Count;
        var leftCount=drawingViews["Left"].First(l=>l.Kind==LayerKind.Artwork).Ink.Strokes.Count;
        CommitFaceStroke(ActiveLayer!,CreateGuidedStroke(new[]{new Point(90,120),new Point(150,180)},ActiveLayer!.Ink.DefaultDrawingAttributes,false,.15));
        var back=drawingViews["Back"].First(l=>l.Kind==LayerKind.Artwork);
        Assert(back.Ink.Strokes.Count==backCount+1 && Math.Abs(back.Ink.Strokes[^1].StylusPoints[0].X-550)<.01,"Opposite face stroke mirror");
        Assert(drawingViews["Left"].First(l=>l.Kind==LayerKind.Artwork).Ink.Strokes.Count==leftCount+1,"Multiple face paint");
        Assert(shadowImages["Back"].Source!=null && shadowImages["Back"].Parent==surfaces["Back"],"Opposite shadow missing");
        Undo(this,new RoutedEventArgs());Assert(Snapshot()==multiBefore,"Multi face edit undo not grouped");
        Tool.SelectedIndex=6;BucketReference.SelectedIndex=1;SetPaintColor(Colors.Blue);
        BeginRasterGesture(overlays["Front"],new Point(20,20));
        Assert(drawingViews["Front"].First(l=>l.Kind==LayerKind.Artwork).Photo!=null && drawingViews["Back"].First(l=>l.Kind==LayerKind.Artwork).Photo!=null && drawingViews["Left"].First(l=>l.Kind==LayerKind.Artwork).Photo!=null,"Multi face bucket failed");
        Tool.SelectedIndex=1;var eraseBefore=Snapshot();BeginRasterGesture(overlays["Front"],new Point(20,20));EndRasterGesture(new Point(40,20));
        Assert(Snapshot()!=eraseBefore,"Multi face eraser failed");Undo(this,new RoutedEventArgs());Assert(Snapshot()==eraseBefore,"Multi face eraser undo failed");
        Tool.SelectedIndex=0;
        selectedFaces.Clear();ApplyDocument(fixture);savedState=Snapshot();
        var coarse=new StrokeSmoother(new Point(0,0),.5f,15);var fine=new StrokeSmoother(new Point(0,0),.5f,15);
        var coarseLine=coarse.Move(new Point(200,0),.5f);var fineLine=new List<(Point point,float pressure)>();
        for(int x=2;x<=200;x+=2) fineLine.AddRange(fine.Move(new Point(x,0),.5f));
        Assert((coarseLine[^1].point-fineLine[^1].point).Length<.01,"Stabilizer depends on event frequency");
        var noisy=Enumerable.Range(0,101).Select(i=>new Point(i*3,100+(i%2==0 ? 5 : -5))).ToArray();
        var smoothed=StrokeSmoother.SmoothAfter(noisy,Enumerable.Repeat(.5f,101).ToArray(),15);
        Assert(smoothed[0].point==noisy[0] && smoothed[^1].point==noisy[^1],"After mode changed endpoints");
        Assert(smoothed.Where(p=>p.point.X>60 && p.point.X<240).Average(p=>Math.Abs(p.point.Y-100))<2,"After mode did not reduce wobble");
        var finish=coarse.Finish(new Point(200,0),.8f);Assert(finish[^1].point==new Point(200,0) && Math.Abs(finish[^1].pressure-.8)<.001,"Stabilizer release or pressure failed");
        StabilizerMethod.SelectedIndex=1;StrokeStabilizer.Value=15;RulerMode.SelectedIndex=0;StrokeFade.IsChecked=false;
        BeginGuidedStroke(overlays["Front"],new Point(60,100));foreach(var point in noisy) ContinueGuidedStroke(new Point(point.X+60,point.Y));
        Assert(Math.Abs(guidedPoints[10].Y-100)>=4,"After mode corrected live preview");EndGuidedStroke(new Point(360,105));
        Assert(ActiveLayer!.Ink.Strokes[^1].StylusPoints[^1].X==360,"After mode committed endpoint incorrectly");
        StabilizerMethod.SelectedIndex=0;StrokeStabilizer.Value=0;ApplyDocument(fixture);savedState=Snapshot();
        activeLayers["Front"]=drawingViews["Front"].First(l=>l.Kind==LayerKind.Artwork).Id;RefreshEditor();
        undo.Clear();redo.Clear();undo.Add(Snapshot());var transformBefore=Snapshot();var transformPoint=ActiveLayer!.Ink.Strokes[0].StylusPoints[0];
        FlipLayer(new System.Windows.Controls.Button {Tag="horizontal"},new RoutedEventArgs());
        Assert(Math.Abs(ActiveLayer.Ink.Strokes[0].StylusPoints[0].X-(640-transformPoint.X))<.001,"Horizontal layer flip failed");
        Undo(this,new RoutedEventArgs());Assert(Snapshot()==transformBefore,"Layer flip undo failed");
        FlipLayer(new System.Windows.Controls.Button {Tag="vertical"},new RoutedEventArgs());
        Assert(Math.Abs(ActiveLayer!.Ink.Strokes[0].StylusPoints[0].Y-(640-transformPoint.Y))<.001,"Vertical layer flip failed");
        Undo(this,new RoutedEventArgs());Tool.SelectedIndex=7;
        Assert(BeginGuidedStroke(overlays["Front"],new Point(100,100)),"Move layer input failed");ContinueGuidedStroke(new Point(130,140));
        Assert(ActiveLayer!.Ink.RenderTransform is TranslateTransform,"Layer move preview missing");EndGuidedStroke(new Point(130,140));
        Assert(Math.Abs(ActiveLayer.Ink.Strokes[0].StylusPoints[0].X-transformPoint.X-30)<.001 && Math.Abs(ActiveLayer.Ink.Strokes[0].StylusPoints[0].Y-transformPoint.Y-40)<.001,"Layer move commit failed");
        Undo(this,new RoutedEventArgs());Assert(Snapshot()==transformBefore,"Move gesture undo not grouped");
        BeginGuidedStroke(overlays["Front"],new Point(100,100));ContinueGuidedStroke(new Point(150,150));CancelGuidedStroke();Assert(Snapshot()==transformBefore && movingLayer==null,"Cancelled move changed layer");
        ActiveLayer!.Locked=true;Assert(!BeginLayerMove(overlays["Front"],new Point(100,100)),"Locked layer moved");ActiveLayer.Locked=false;
        var photoLayer=Attach(new PaintLayer {Kind=LayerKind.Reference,Name="Transform photo"});var photoPixels=new byte[640*640*4];photoPixels[(40*640+30)*4+2]=255;photoPixels[(40*640+30)*4+3]=255;photoLayer.ImageData=EncodeRaster(photoPixels,640,640);
        TransformLayer(photoLayer,new Matrix(-1,0,0,1,640,0));var photoResult=LayerPixels(new[]{photoLayer});Assert(photoResult[(40*640+609)*4+2]>240,"Photo flip failed");
        TransformLayer(photoLayer,new Matrix(1,0,0,1,-20,10));photoResult=LayerPixels(new[]{photoLayer});Assert(photoResult[(50*640+589)*4+2]>240,"Photo move failed");
        Assert(RulerSettings.Parent!=null && StabilizerSettings.Parent==RulerSettings.Parent,"Separate drawing aid menus missing");
        Tool.SelectedIndex=0;ApplyDocument(fixture);savedState=Snapshot();
        SetViewLayout("3d");await previewLoadTask;var qualityYaw=previewYaw;var qualityPitch=previewPitch;int previousCells=0;
        for(int level=0;level<4;level++)
        {
            PreviewQuality.Value=new[]{16,28,48,72}[level];await previewLoadTask;
            Assert(previewOccupiedCells>previousCells && PreviewLoadingBar.Value==100 && PreviewLoadingOverlay.Visibility==Visibility.Collapsed,"Preview quality did not increase volume resolution");
            Assert(OutlinePreviewStatus.Text.StartsWith(PreviewQualitySettings(new[]{16,28,48,72}[level]).label+" quality"),"Preview quality status incorrect");
            Assert(previewYaw==qualityYaw && previewPitch==qualityPitch,"Quality reset preview camera");previousCells=previewOccupiedCells;
        }
        PreviewQuality.Value=16;var oldQualityTask=previewLoadTask;PreviewQuality.Value=48;await Task.WhenAll(oldQualityTask,previewLoadTask);
        Assert(OutlinePreviewStatus.Text.StartsWith("High quality") && PreviewRefreshButton.IsEnabled,"Older quality request replaced latest preview");
        PreviewQuality.Value=28;await previewLoadTask;SetViewLayout("single");
        PreviewQuality.Value=37;Assert(PreviewQualityInput.Text=="37","Quality slider did not update input");
        PreviewQualityInput.Text="45";PreviewQualityCommitted(this,new RoutedEventArgs());Assert(PreviewQuality.Value==45,"Quality input did not update slider");
        PreviewQualityInput.Text="100";PreviewQualityCommitted(this,new RoutedEventArgs());Assert(PreviewQuality.Value==72 && PreviewQualityInput.Text=="72","Quality input not bounded");
        PreviewQualityInput.Text="bad";PreviewQualityCommitted(this,new RoutedEventArgs());Assert(PreviewQualityInput.Text=="72","Invalid quality input not restored");PreviewQuality.Value=28;
        var outputCheck=Path.Combine(folder,"generation-output-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outputCheck);
        File.WriteAllText(Path.Combine(outputCheck,"mesh.fbx"),"fixture");
        Assert(!GenerationOutputReady(outputCheck),"Incomplete FBX reported as complete generation");
        File.WriteAllText(Path.Combine(outputCheck,"asset.meshdrawasset"),"fixture");File.WriteAllText(Path.Combine(outputCheck,"result.json"),"{}");
        Assert(GenerationOutputReady(outputCheck),"Complete generation output not available");
        File.WriteAllText(Path.Combine(outputCheck,"generation.log"),"test log");
        SetGenerationOutcome(outputCheck,0,false,Array.Empty<string>());
        Assert(ResultButton.IsEnabled && GenerationLogButton.IsEnabled && resultFolder==outputCheck && Status.Text.StartsWith("Saved asset.meshdrawasset"),"Successful generation output not exposed");
        SetGenerationOutcome(outputCheck,1,false,new[]{"Generation failed: example exporter error"});
        Assert(ResultButton.IsEnabled && GenerationLogButton.IsEnabled && Status.Text.Contains("example exporter error"),"Failed generation folder and error not exposed");
        SetGenerationOutcome(outputCheck,1,true,Array.Empty<string>());Assert(Status.Text.StartsWith("Generation cancelled"),"Cancellation reported as failure");
        File.WriteAllText(Path.Combine(outputCheck,"result.json"),"{\"drawingFit\":{\"meanIoU\":0.9566}}");
        Assert(OutputFitSummary(outputCheck).Contains("95.7% before export"),"Drawing fidelity summary missing");
        resultFolder=null;generationLogFolder=null;ResultButton.IsEnabled=false;GenerationLogButton.IsEnabled=false;

        var beforeExtras=Document();
        ExtraFaceName.Text="Diagonal view";ExtraFaceYaw.Text="45";ExtraFacePitch.Text="20";ExtraFaceType.SelectedIndex=0;
        AddExtraFace(this,new RoutedEventArgs());var angled=extraFaces.Single();
        Assert(drawingViews.Count==7 && ActiveView==angled.Id && ActiveLayer!.Name=="Outline","Extra angled face not editable");
        ActiveLayer!.Ink.Strokes.Add(Circle());
        ExtraFaceName.Text="Middle slice";ExtraFaceYaw.Text="0";ExtraFacePitch.Text="0";ExtraFaceType.SelectedIndex=1;ExtraFaceOffset.Value=0;
        AddExtraFace(this,new RoutedEventArgs());var slice=extraFaces.Last();ActiveLayer!.Ink.Strokes.Add(Circle());
        Assert(extraFaces.Count==2 && drawingViews.Count==8 && slice.IsSection,"Interior slice creation failed");
        var middle=FacePlanePoint(slice.Id,.5,.5);Assert(middle==new System.Windows.Media.Media3D.Point3D(0,0,0),"Middle slice not between exterior faces");
        ExtraFaceOffset.Value=.5;ApplyExtraFace(this,new RoutedEventArgs());Assert(Math.Abs(FacePlanePoint(slice.Id,.5,.5).Z-.5)<1e-8,"Slice position not applied");
        var extraMasks=DrawingDocument.ViewNames.ToDictionary(name=>name,name=>filledMask);var halfMask=new bool[8,8];for(int x=0;x<4;x++) for(int y=0;y<8;y++) halfMask[x,y]=true;
        var straightExtra=slice with {Offset=0,IsSection=false};extraMasks[slice.Id]=halfMask;
        BuildOutlineOverlapCore(extraMasks,8,out var extraOccupied,[straightExtra]);Assert(extraOccupied==256,"Angled face missing from overlap constraints");
        BuildOutlineOverlapCore(extraMasks,8,out var sliceOccupied,[straightExtra with {IsSection=true}]);Assert(sliceOccupied>extraOccupied && sliceOccupied<512,"Interior slice did not blend into surrounding preview volume");
        var extraSnapshot=Snapshot();ApplyDocument(DrawingDocument.Read(extraSnapshot));
        Assert(extraFaces.Count==2 && drawingViews[slice.Id][0].Ink.Strokes.Count==1,"Extra faces lost after save/load");
        SetViewLayout("all");Assert(AllViews.Children.Count==8 && AllViews.Rows==3,"All-view layout dropped extra faces");
        SelectMultiView(angled.Id);Assert(ActiveView==angled.Id,"Extra face canvas selection failed");
        var extraExport=Path.Combine(folder,"extra-face-export");Directory.CreateDirectory(extraExport);WriteDrawings(extraExport);
        Assert(File.Exists(Path.Combine(extraExport,angled.Id+".png")) && DrawingDocument.Read(File.ReadAllText(Path.Combine(extraExport,"project.meshdraw"))).ExtraFaces!.Count==2,"Extra face export incomplete");
        ExtraFacesSettings.IsExpanded=true;
        var extraContent=(FrameworkElement)Content;extraContent.Measure(new Size(1408,880));extraContent.Arrange(new Rect(0,0,1408,880));extraContent.UpdateLayout();
        var extraImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);extraImage.Render(extraContent);var extraPng=new PngBitmapEncoder();extraPng.Frames.Add(BitmapFrame.Create(extraImage));
        using(var stream=File.Create(Path.Combine(folder,"extra-faces-preview.png"))) extraPng.Save(stream);
        ExtraFacesSettings.IsExpanded=false;
        SelectMultiView(slice.Id);DeleteExtraFace(this,new RoutedEventArgs());Assert(extraFaces.Count==1 && !drawingViews.ContainsKey(slice.Id),"Slice removal failed");
        Undo(this,new RoutedEventArgs());Assert(extraFaces.Count==2 && drawingViews.ContainsKey(slice.Id),"Undo did not restore slice");
        var invalidExtra=Document() with {ExtraFaces=extraFaces.Select(f=>f.Id==slice.Id ? f with {Yaw=double.NaN} : f).ToList()};
        try {DrawingDocument.Validate(invalidExtra);throw new Exception("Invalid angle accepted");} catch(System.IO.InvalidDataException) {}
        ApplyDocument(beforeExtras);SetViewLayout("single");savedState=Snapshot();

        var beforeSelection=Document();var emptyStrokes=DrawingDocument.Encode(new StrokeCollection());
        var selectionFixture=new EditorDocument(2,0,DrawingDocument.ViewNames.ToDictionary(name=>name,name=>new List<LayerData> {
            new(Guid.NewGuid().ToString("N"),"Outline",LayerKind.Artwork,true,false,1,emptyStrokes),
            new(Guid.NewGuid().ToString("N"),"Color",LayerKind.BaseColor,true,false,1,emptyStrokes),
            new(Guid.NewGuid().ToString("N"),"Height",LayerKind.Height,true,false,1,emptyStrokes)}),new([],[]),256,256);
        ApplyDocument(selectionFixture);Views.SelectedIndex=0;selectedFaces.Clear();Tool.SelectedIndex=8;SelectionShape.SelectedIndex=0;SelectionOperation.SelectedIndex=0;undo.Clear();redo.Clear();undo.Add(Snapshot());
        Assert(SelectionSettings.Visibility==Visibility.Visible && SizeOpacitySettings.Visibility==Visibility.Collapsed,"Selection controls not contextual");
        BeginGuidedStroke(overlays["Front"],new Point(64,64));ContinueGuidedStroke(new Point(192,192));EndGuidedStroke(new Point(192,192));
        Assert(SelectionFor("Front")!.FillContains(new Point(128,128)) && !SelectionFor("Front")!.FillContains(new Point(32,128)),"Rectangle selection area wrong");
        Assert(overlays["Front"].Children.OfType<System.Windows.Shapes.Path>().Any(p=>p.Stroke==Brushes.DodgerBlue),"Selection highlight missing");
        var selectedState=Snapshot();Undo(this,new RoutedEventArgs());Assert(SelectionFor("Front")==null,"Selection undo failed");Redo(this,new RoutedEventArgs());Assert(SelectionFor("Front")!=null,"Selection redo failed");
        ApplyDocument(DrawingDocument.Read(selectedState));Assert(SelectionMask("Front")![128*256+128] && !SelectionMask("Front")![128*256+32],"Saved selection mask lost");
        Tool.SelectedIndex=0;BrushSize.Value=50;BrushOpacity.Value=1;SetPaintColor(Colors.Red);
        CommitFaceStroke(ActiveLayer!,CreateGuidedStroke(new[]{new Point(0,128),new Point(256,128)},ActiveLayer!.Ink.DefaultDrawingAttributes,false,.15));
        var selectedPixels=LayerPixels(new[]{ActiveLayer!});Assert(selectedPixels[(128*256+80)*4+3]>0 && selectedPixels[(128*256+32)*4+3]==0,"Pen escaped selection");
        Assert(selectedPixels[(128*256+63)*4+3]==0 && selectedPixels[(128*256+192)*4+3]==0,"Brush footprint leaked across selection boundary");
        var noPaint=Snapshot();CommitFaceStroke(ActiveLayer!,CreateGuidedStroke(new[]{new Point(5,5),new Point(50,5)},ActiveLayer!.Ink.DefaultDrawingAttributes,false,.15));Assert(Snapshot()==noPaint,"Outside pen stroke changed layer");
        Tool.SelectedIndex=1;ApplyTools();Assert(!ActiveLayer!.Ink.IsHitTestVisible && overlays["Front"].IsHitTestVisible,"Selection eraser bypasses mask");
        var selectedOnly=SelectionMask("Front")!;var outsideProtected=new byte[256*256*4];for(int y=0;y<256;y++) for(int x=0;x<256;x++) {int i=(y*256+x)*4;outsideProtected[i]=(byte)(20+(x*7+y*3)%220);outsideProtected[i+1]=100;outsideProtected[i+2]=60;outsideProtected[i+3]=255;}
        foreach(var mode in new[]{4,5,7})
        {
            var masked=(byte[])outsideProtected.Clone();Assert(ApplyRasterDab(masked,256,256,new Point(64,128),new Point(54,128),30,1,mode,selectedOnly),"Selected raster tool made no change");
            for(int y=0;y<256;y++) for(int x=0;x<256;x++) if(!selectedOnly[y*256+x]) for(int c=0;c<4;c++) Assert(masked[(y*256+x)*4+c]==outsideProtected[(y*256+x)*4+c],"Raster tool escaped selection");
        }
        var flood=new byte[256*256*4];Assert(FloodFillPixels(flood,(byte[])flood.Clone(),256,256,128,128,Colors.Blue,0,selectedOnly)==128*128,"Bucket did not stop at selection");
        Assert(FloodFillPixels(flood,(byte[])flood.Clone(),256,256,32,32,Colors.Red,0,selectedOnly)==0,"Outside bucket accepted");
        activeLayers["Front"]=drawingViews["Front"].First(l=>l.Kind==LayerKind.Height).Id;RefreshEditor();Tool.SelectedIndex=6;PropertyValue.Value=.8;BucketReference.SelectedIndex=1;
        BeginRasterGesture(overlays["Front"],new Point(128,128));var heightPixels=LayerPixels(new[]{ActiveLayer!});Assert(heightPixels[(128*256+128)*4+3]==255 && heightPixels[(32*256+32)*4+3]==0,"Property fill ignored selection");
        Views.SelectedIndex=1;Assert(SelectionFor("Back")==null,"Selection leaked into another face");Views.SelectedIndex=0;Tool.SelectedIndex=8;
        SelectionShape.SelectedIndex=1;BeginSelectionGesture(overlays["Front"],new Point(64,64));EndSelectionGesture(new Point(192,192));
        Assert(SelectionFor("Front")!.FillContains(new Point(128,128)) && !SelectionFor("Front")!.FillContains(new Point(65,65)),"Ellipse selection wrong");
        SelectionShape.SelectedIndex=2;BeginSelectionGesture(overlays["Front"],new Point(64,64));ContinueSelectionGesture(new Point(192,64));ContinueSelectionGesture(new Point(128,192));EndSelectionGesture(new Point(64,64));
        Assert(SelectionFor("Front")!.FillContains(new Point(128,100)) && !SelectionFor("Front")!.FillContains(new Point(180,180)),"Lasso selection wrong");
        SelectionShape.SelectedIndex=0;SelectionOperation.SelectedIndex=1;BeginSelectionGesture(overlays["Front"],new Point(200,200));EndSelectionGesture(new Point(240,240));
        Assert(SelectionFor("Front")!.FillContains(new Point(220,220)) && SelectionFor("Front")!.FillContains(new Point(128,100)),"Add selection discarded old region");
        SelectionOperation.SelectedIndex=2;BeginSelectionGesture(overlays["Front"],new Point(210,210));EndSelectionGesture(new Point(230,230));Assert(!SelectionFor("Front")!.FillContains(new Point(220,220)),"Subtract selection failed");
        InvertSelection(this,new RoutedEventArgs());Assert(SelectionFor("Front")!.FillContains(new Point(220,220)) && !SelectionFor("Front")!.FillContains(new Point(128,100)),"Invert selection failed");
        var complexSelection=Snapshot();ApplyDocument(DrawingDocument.Read(complexSelection));Assert(SelectionFor("Front")!.FillContains(new Point(220,220)) && !SelectionFor("Front")!.FillContains(new Point(128,100)),"Selection holes lost after save/load");
        SelectEntireCanvas(this,new RoutedEventArgs());Assert(SelectionMask("Front")!.All(v=>v),"Select all not full canvas");InvertSelection(this,new RoutedEventArgs());Assert(!SelectionMask("Front")!.Any(v=>v),"Empty selection not blocked");ApplyDocument(DrawingDocument.Read(Snapshot()));Assert(!SelectionMask("Front")!.Any(v=>v),"Empty selection lost on load");
        Tool.SelectedIndex=0;var blocked=Snapshot();CommitFaceStroke(ActiveLayer!,CreateGuidedStroke(new[]{new Point(50,50),new Point(150,150)},ActiveLayer!.Ink.DefaultDrawingAttributes,false,.15));Assert(Snapshot()==blocked,"Empty selection allowed paint");
        ClearSelection(this,new RoutedEventArgs());Assert(SelectionFor("Front")==null && SelectionMask("Front")==null,"Clear selection did not remove restriction");
        var invalidSelection=Document() with {Selections=new Dictionary<string,string>{{"Front","M -10,-10 L 100,0 100,100 Z"}}};
        try {DrawingDocument.Validate(invalidSelection);throw new Exception("Out-of-canvas selection accepted");} catch(System.IO.InvalidDataException) {}
        SetSelection(new RectangleGeometry(new Rect(64,64,128,128)));Tool.SelectedIndex=8;SelectionOperation.SelectedIndex=0;
        var propertyLayer=ActiveLayer!;Tool.SelectedIndex=0;PropertyValue.Value=.2;var propertyBefore=Snapshot();
        CommitFaceStroke(propertyLayer,CreateGuidedStroke(new[]{new Point(128,0),new Point(128,256)},propertyLayer.Ink.DefaultDrawingAttributes,false,.15));
        Undo(this,new RoutedEventArgs());Assert(Snapshot()==propertyBefore,"Masked pen undo lost selection or layer data");Redo(this,new RoutedEventArgs());
        Views.SelectedIndex=1;SetSelection(new RectangleGeometry(new Rect(64,100,128,40)));Views.SelectedIndex=0;selectedFaces.Add("Back");
        activeLayers["Front"]=drawingViews["Front"][0].Id;RefreshEditor();
        CommitFaceStroke(ActiveLayer!,CreateGuidedStroke(new[]{new Point(0,110),new Point(256,110)},ActiveLayer!.Ink.DefaultDrawingAttributes,false,.15));
        var backSelectionPixels=LayerPixels(new[]{drawingViews["Back"][0]});
        Assert(backSelectionPixels[(110*256+128)*4+3]>0 && backSelectionPixels[(99*256+128)*4+3]==0 && backSelectionPixels[(110*256+40)*4+3]==0,"Multiple-face pen ignored target selection");selectedFaces.Clear();Tool.SelectedIndex=8;
        var selectionExport=Path.Combine(folder,"selection-export");Directory.CreateDirectory(selectionExport);WriteDrawings(selectionExport);
        Assert(DrawingDocument.Read(File.ReadAllText(Path.Combine(selectionExport,"project.meshdraw"))).Selections!.ContainsKey("Front"),"Selection missing from saved project");
        var selectionPhoto=DrawingDocument.DecodePhoto(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(selectionExport,"front.png"))))!;var selectionExportPixels=new byte[256*256*4];selectionPhoto.CopyPixels(selectionExportPixels,256*4,0);
        Assert(selectionExportPixels[(70*256+70)*4]==255 && selectionExportPixels[(70*256+70)*4+2]==255,"Blue selection overlay exported into geometry drawing");
        var selectionContent=(FrameworkElement)Content;selectionContent.Measure(new Size(1408,880));selectionContent.Arrange(new Rect(0,0,1408,880));selectionContent.UpdateLayout();
        var selectionImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);selectionImage.Render(selectionContent);var selectionPng=new PngBitmapEncoder();selectionPng.Frames.Add(BitmapFrame.Create(selectionImage));
        using(var stream=File.Create(Path.Combine(folder,"selection-preview.png"))) selectionPng.Save(stream);
        ApplyDocument(beforeSelection);Tool.SelectedIndex=0;savedState=Snapshot();

        var beforeBrushSelection=Document();ApplyDocument(selectionFixture);Views.SelectedIndex=0;selectedFaces.Clear();RulerMode.SelectedIndex=0;StrokeStabilizer.Value=0;StrokeFade.IsChecked=false;
        Assert(Layers.Items[0] is SelectionLayerRow && Layers.Items.OfType<PaintLayer>().Count()==3,"Selectable selection row missing");
        var paintingBefore=JsonSerializer.Serialize(Document().Layers);Layers.SelectedItem=SelectionRow("Front");
        Assert(SelectionEditing && Tool.SelectedIndex==0 && ColorSettings.Visibility==Visibility.Collapsed && Layers.SelectedItem is SelectionLayerRow,"Selecting selection row did not enable mask brush");
        BrushSize.Value=40;BeginGuidedStroke(overlays["Front"],new Point(40,80));ContinueGuidedStroke(new Point(120,80));EndGuidedStroke(new Point(160,80));
        Assert(SelectionFor("Front")!.FillContains(new Point(80,80)) && !SelectionFor("Front")!.FillContains(new Point(80,120)),"Selection brush footprint missing");
        Assert(JsonSerializer.Serialize(Document().Layers)==paintingBefore,"Mask brush changed paint layers");
        var firstBrushMask=Snapshot();BeginGuidedStroke(overlays["Front"],new Point(40,140));EndGuidedStroke(new Point(160,140));
        Assert(SelectionFor("Front")!.FillContains(new Point(80,80)) && SelectionFor("Front")!.FillContains(new Point(80,140)),"Mask brush replaced prior stroke");
        Undo(this,new RoutedEventArgs());Assert(SelectionEditing,"Mask brush undo lost selected row");Assert(Snapshot()==firstBrushMask,"Mask brush undo changed saved data");Redo(this,new RoutedEventArgs());
        Tool.SelectedIndex=1;BrushSize.Value=40;BeginGuidedStroke(overlays["Front"],new Point(100,50));EndGuidedStroke(new Point(100,105));
        Assert(!SelectionFor("Front")!.FillContains(new Point(100,80)) && SelectionFor("Front")!.FillContains(new Point(60,80)),"Mask eraser did not subtract");
        Assert(JsonSerializer.Serialize(Document().Layers)==paintingBefore,"Mask eraser changed paint layers");
        Layers.SelectedItem=drawingViews["Front"][0];Tool.SelectedIndex=0;SetPaintColor(Colors.Red);BrushSize.Value=10;
        BeginGuidedStroke(overlays["Front"],new Point(0,80));EndGuidedStroke(new Point(256,80));
        var brushClipped=LayerPixels(new[]{drawingViews["Front"][0]});Assert(brushClipped[(80*256+60)*4+3]>0 && brushClipped[(80*256+100)*4+3]==0 && brushClipped[(80*256+10)*4+3]==0,"Painting did not use brushed mask");
        SelectSelectionLayer(this,new RoutedEventArgs());FlipLayer(new System.Windows.Controls.Button {Tag="horizontal"},new RoutedEventArgs());
        Assert(!SelectionFor("Front")!.FillContains(new Point(156,80)) && SelectionFor("Front")!.FillContains(new Point(196,80)),"Selection row flip did not flip mask");
        var maskPaintProtection=JsonSerializer.Serialize(Document().Layers);ClearLayer(this,new RoutedEventArgs());Assert(SelectionFor("Front")==null && JsonSerializer.Serialize(Document().Layers)==maskPaintProtection,"Clear selection row changed paint data");
        var borderStart=new Point(-10,10);BeginGuidedStroke(overlays["Front"],borderStart);EndGuidedStroke(new Point(20,10));DrawingDocument.Validate(Document());
        Assert(SelectionFor("Front")!.Bounds.Left>=0 && SelectionFor("Front")!.Bounds.Top>=0,"Selection brush escaped canvas bounds");
        Tool.SelectedIndex=8;SelectionShape.SelectedIndex=1;SelectionOperation.SelectedIndex=0;BeginGuidedStroke(overlays["Front"],new Point(64,64));EndGuidedStroke(new Point(192,192));
        Assert(SelectionEditing && SelectionFor("Front")!.FillContains(new Point(128,128)),"Shapes did not set selected selection row");
        var pixelMask=new byte[32*32*4];for(int y=4;y<28;y++) for(int x=4;x<28;x++) if(x<12 || x>=20 || y<12 || y>=20) pixelMask[(y*32+x)*4+3]=255;
        var traced=TraceSelectionMask(pixelMask,32,32);Assert(traced.FillContains(new Point(8.5,8.5)) && !traced.FillContains(new Point(16.5,16.5)) && !traced.FillContains(new Point(1.5,1.5)),"Layer mask trace lost holes");
        for(int y=0;y<32;y++) for(int x=0;x<32;x++) Assert(traced.FillContains(new Point(x+.5,y+.5))==(pixelMask[(y*32+x)*4+3]>0),"Traced mask pixel mismatch");
        for(int y=0;y<32;y++) for(int x=0;x<32;x++) pixelMask[(y*32+x)*4+3]=(byte)((x+y)%2==0 ? 255 : 0);
        var checkerMask=TraceSelectionMask(pixelMask,32,32);for(int y=0;y<32;y++) for(int x=0;x<32;x++) Assert(checkerMask.FillContains(new Point(x+.5,y+.5))==((x+y)%2==0),"Diagonal mask contacts lost pixels");
        Layers.SelectedItem=drawingViews["Front"][0];Assert(!SelectionEditing && Tool.SelectedIndex==0,"Returning to paint layer kept shape selection tool");UseLayerAsMask(this,new RoutedEventArgs());
        Assert(SelectionEditing && SelectionFor("Front")!.FillContains(new Point(60,80)) && !SelectionFor("Front")!.FillContains(new Point(100,80)),"Use layer as mask did not use source alpha");
        var brushSaved=Snapshot();ApplyDocument(DrawingDocument.Read(brushSaved));Assert(SelectionEditing && SelectionFor("Front")!.FillContains(new Point(60,80)),"Brushed selection lost on load");
        var maskContent=(FrameworkElement)Content;maskContent.Measure(new Size(1408,880));maskContent.Arrange(new Rect(0,0,1408,880));maskContent.UpdateLayout();
        var maskImage=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32);maskImage.Render(maskContent);var maskPng=new PngBitmapEncoder();maskPng.Frames.Add(BitmapFrame.Create(maskImage));
        using(var stream=File.Create(Path.Combine(folder,"selection-layer-brush-preview.png"))) maskPng.Save(stream);
        ApplyDocument(beforeBrushSelection);Tool.SelectedIndex=0;savedState=Snapshot();

        CheckFaceCopyAndVertexLimit(folder);
        CheckEdgeGuides(folder);
        await CheckFilters(folder);
        var content=(FrameworkElement)Content;
        content.Measure(new Size(1408,880)); content.Arrange(new Rect(0,0,1408,880)); content.UpdateLayout();
        var visual=new RenderTargetBitmap(1408,880,96,96,PixelFormats.Pbgra32); visual.Render(content);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(visual));
        using(var stream=File.Create(Path.Combine(folder,"editor-preview.png"))) png.Save(stream);
        savedState=Snapshot();
        File.WriteAllText(Path.Combine(folder,"editor-checks.txt"),"PASS: straight/corner gestures; edge mark paint isolation; edge mark save/load; mirrored independent marks; edge eraser undo; guide coordinate validation; saved vertex limit; slider/input synchronization; legacy defaults; limit validation; multiple destination face copies; add/replace; independent vectors/photos/property layers; horizontal mirror; selection copy; angled/slice preservation; atomic capacity/lock protection; face-copy undo/redo/save; 78 filter catalog; category/search; filter preview/reset; color validation; full-size filter application; selection boundary preservation; transparent alpha; effect opacity; filter cancellation; one-step undo/redo; filtered save/load; preview isolation; selectable selection row; brush mask accumulation; mask eraser subtraction; paint-layer isolation; mask brush undo/redo; brush mask paint clipping; selection row clear/flip; clipped mask bounds; use painted layer alpha as mask; holes and diagonal contacts; selection rectangle/ellipse/lasso; add/subtract/invert/clear/all; pen footprint clipping; protected raster pixels; masked bucket/property fill; eraser mask routing; per-face persistence; selection undo/redo; empty mask; selection excluded from drawing export; custom angled faces; interior slices and position; extra face save/load/export; dynamic all-view selection; slice removal/undo; invalid extra face rejection; drawing-fit report summary; complete generation output gating; success output button; failed output folder and log; actual failure message; cancellation; quality slider/input synchronization; quality range and invalid input; four preview quality levels; increasing overlap resolution; quality refresh loading; camera preservation; latest quality request wins; horizontal/vertical layer flips; vector and photo transforms; move drag preview; move undo/cancel; locked layer protection; separate top-right menus; event-independent stabilizer; real time smoothing; after-release correction; endpoint and pressure preservation; per-tool size/opacity; contextual bottom settings; multiple face strokes; opposite mirror; grouped undo; opposite shadow; draggable ruler center; both straight endpoints; circular radius handle; parallel strokes anywhere; concentric arcs anywhere; ruler reset and numeric inputs; bucket outline boundaries; blur edges; smudge color transport; locked layer protection; raster save/export; raster undo/redo; large canvas thin outlines; square/landscape/portrait preview; loading progress; refresh completion; straight/circular rulers; stabilizer; fade persistence; guided stroke undo; six-view layout; active view layer selection; canvas preservation; 3D outline plane alignment; six-silhouette overlap; empty overlap; top constraint; canvas sliders and text synchronization; individual drawing and preview deletion; current drawing reset; deletion path validation; live canvas size preview; photo import; photo save/load; photo undo/redo/duplicate; reference export isolation; photo texture export; aspect preservation; canvas size persistence; rectangular canvas export; dimension validation; rectangular joint projection; static/skeletal creation; built-in rig templates; reusable skeleton types; creation screen; in-app gallery cards; click to open; automatic save on gallery return; unique copy names; name validation; naming cancel; multiple saved drawings; independent files; reopen; new drawing reset; rainbow hue slider; default Outline/Color layers; two-channel preview; HSV color picker; brush presets; point eraser; legacy migration; layered save/load; lock; opacity; hidden compositing; geometry/property separation; add/undo/redo; 3D projection; hierarchy validation; five channel exports; custom skeleton export.\n");
    }
}



