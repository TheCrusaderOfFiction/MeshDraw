using System.Diagnostics;
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
    private async Task CheckVfx(string folder)
    {
        static void Check(bool value,string text){if(!value)throw new Exception(text);}
        var previous=Document();var previousUndo=undo.ToList();var previousRedo=redo.ToArray();
        try
        {
            StartDrawing(640,640,2,"None",new([],[]));VfxPreviewPlaying.IsChecked=false;
            Check(Output.SelectedIndex==2 && VfxSettingsPanel.Visibility==Visibility.Visible && VertexBudgetSettings.Visibility==Visibility.Collapsed && SkeletonSettingsPanel.Visibility==Visibility.Collapsed,"VFX mode controls are incorrect");
            foreach(var preset in new[]{"Fire","Smoke","Sparks","Magic"})
            {
                VfxPreset.SelectedIndex=Array.IndexOf(new[]{"Fire","Smoke","Sparks","Magic"},preset);Check(vfxSettings!.Preset==preset,"VFX preset did not apply");
                vfxTime=.4;RenderVfxPreview();Check(VfxPreviewImage.Source is BitmapSource,"VFX preview is missing");
            }
            VfxRate.Value=91;VfxLifetime.Value=2.3;VfxSize.Value=42;CommitVfxSettings(this,new RoutedEventArgs());
            var saved=Snapshot();DrawingDocument.Read(saved);ApplyDocument(DrawingDocument.Read(saved));Check(vfxSettings!.SpawnRate==91 && vfxSettings.Lifetime==2.3 && vfxSettings.Size==42,"VFX settings did not save/load");
            var beforeOption=Snapshot();VfxLoop.IsChecked=false;var afterOption=Snapshot();Undo(this,new RoutedEventArgs());Check(Snapshot()==beforeOption,"VFX undo failed");Redo(this,new RoutedEventArgs());Check(Snapshot()==afterOption,"VFX redo failed");
            var output=Path.Combine(folder,"vfx-export");Directory.CreateDirectory(output);WriteVfxInput(output);File.WriteAllText(Path.Combine(output,"job.json"),JsonSerializer.Serialize(new {version=2,outputType="vfx"}));
            var start=new ProcessStartInfo(Path.Combine(projectRoot,"local-ai","venv","Scripts","python.exe")){WorkingDirectory=projectRoot,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var argument in new[]{Path.Combine(projectRoot,"backend","generate_vfx.py"),"--job",Path.Combine(output,"job.json")})start.ArgumentList.Add(argument);
            using(var process=Process.Start(start)!) {var errors=process.StandardError.ReadToEndAsync();var logs=process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();Check(process.ExitCode==0,await errors);await logs;}
            Check(GenerationOutputReady(output) && !File.Exists(Path.Combine(output,"mesh.fbx")),"VFX incorrectly required a mesh");
            using(var asset=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"asset.meshdrawasset"))))Check(asset.RootElement.GetProperty("outputType").GetString()=="vfx" && asset.RootElement.GetProperty("effect").GetString()=="effect.json","VFX asset bundle is wrong");
            VfxPreset.SelectedIndex=0;VfxUseDrawing.IsChecked=true;
            drawingViews["Front"][0].Ink.Strokes.Add(new Stroke(new StylusPointCollection {new StylusPoint(250,320),new StylusPoint(390,320)}){DrawingAttributes=new DrawingAttributes {Width=50,Height=50,Color=Colors.Black}});
            var drawn=DrawingVfxSprite();var pixels=new byte[drawn.PixelWidth*drawn.PixelHeight*4];drawn.CopyPixels(pixels,drawn.PixelWidth*4,0);int center=(320*640+320)*4;
            Check(pixels[3]==0 && pixels[center+3]>240 && pixels[center]>240,"Drawn particle sprite lost alpha or outline visibility");
            WriteVfxInput(output);Check(File.Exists(Path.Combine(output,"vfx_drawing.png")),"Drawn VFX sprite was not exported");
            var invalid=Document() with {Vfx=vfxSettings! with {Lifetime=double.NaN}};bool rejected=false;try{DrawingDocument.Validate(invalid);}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid VFX settings accepted");
            VfxUseDrawing.IsChecked=false;vfxTime=.7;RenderVfxPreview();
            var content=(FrameworkElement)Content;content.Measure(new Size(1408,960));content.Arrange(new Rect(0,0,1408,960));content.UpdateLayout();var image=new RenderTargetBitmap(1408,960,96,96,PixelFormats.Pbgra32);image.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(folder,"vfx-preview.png")))png.Save(file);
            File.WriteAllText(Path.Combine(folder,"vfx-checks.txt"),"PASS: VFX mode, four presets, particle preview, saved settings, undo/redo, local CPU generation, custom bundle, no mesh requirement, transparent drawn sprite, invalid-setting rejection.");
        }
        finally {ApplyDocument(previous);undo.Clear();undo.AddRange(previousUndo);redo.Clear();foreach(var value in previousRedo.Reverse())redo.Push(value);}
    }
}
