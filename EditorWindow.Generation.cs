using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace MeshDraw;

public partial class EditorWindow
{
    private Process? generator;
    private string? resultFolder;
    private string? generationLogFolder;
    private bool cancelled;
    private readonly string projectRoot=FindProjectRoot();
    private static string FindProjectRoot()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory); directory!=null; directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"backend","generate.py"))) return directory.FullName;
        return AppContext.BaseDirectory;
    }
    private static string? FindBlender()
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Blender Foundation");
        return Directory.Exists(folder) ? Directory.GetFiles(folder,"blender.exe",SearchOption.AllDirectories).OrderDescending().FirstOrDefault() : null;
    }
    private bool BackendReady => File.Exists(Path.Combine(projectRoot,"local-ai","venv","Scripts","python.exe"))
        && File.Exists(Path.Combine(projectRoot,"local-ai","models","Hunyuan3D-2mv","hunyuan3d-dit-v2-mv","model.fp16.safetensors")) && FindBlender()!=null;
    private void RefreshBackend() => BackendStatus.Text=Output.SelectedIndex==2 ? "Local Niagara VFX export. Use a preset sprite or paint on Front. The Unreal plugin creates the Niagara system." : BackendReady
        ? "Local GPU engine ready. Painted properties bake to UV maps. For skeletal output, choose a skeleton type or draw bones."
        : "Run setup-local.ps1 once to install the local engine.";
    private async void GenerateMesh(object sender,RoutedEventArgs e)
    {
        if(generator!=null) return;
        bool vfx=Output.SelectedIndex==2;
        if(!vfx)CommitVertexLimit(this,new RoutedEventArgs());
        RefreshBackend(); if(vfx ? !File.Exists(Path.Combine(projectRoot,"local-ai","venv","Scripts","python.exe")) : !BackendReady) {Status.Text="Install the local AI engine with setup-local.ps1 first."; return;}
        if(!vfx && drawingViews.Values.Any(layers=>!layers.Any(l=>l.Kind==LayerKind.Artwork && l.Visible && l.Opacity>0 && (l.Ink.Strokes.Count>0 || l.Photo!=null))))
        {Status.Text="Draw a closed outline on a visible Artwork layer in every face, including extra faces, first."; return;}
        if(Output.SelectedIndex==1 && skeletonType=="Custom" && rig.Bones.Count==0) {Status.Text="Draw bones or apply a skeleton type before generating a skeletal mesh.";return;}
        try {DrawingDocument.Validate(Document());} catch(Exception error) {ShowError(error); return;}
        cancelled=false;
        var folder=Path.Combine(projectRoot,"outputs",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        var logs=new List<string>();
        try
        {
            Directory.CreateDirectory(folder); generationLogFolder=folder; if(vfx)WriteVfxInput(folder);else WriteDrawings(folder);
            File.WriteAllText(Path.Combine(folder,"job.json"),JsonSerializer.Serialize(new {version=2,outputType=vfx ? "vfx" : Output.SelectedIndex==0 ? "static" : "skeletal", textureSize=1024,vertexLimit}));
            var start=new ProcessStartInfo(Path.Combine(projectRoot,"local-ai","venv","Scripts","python.exe"))
                {WorkingDirectory=projectRoot,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[] {"-u",Path.Combine(projectRoot,"backend",vfx?"generate_vfx.py":"generate.py"),"--job",Path.Combine(folder,"job.json")}) start.ArgumentList.Add(argument);
            if(!vfx){start.ArgumentList.Add("--blender");start.ArgumentList.Add(FindBlender()!);}
            start.Environment["PYTHONIOENCODING"]="utf-8";
            generator=new Process {StartInfo=start};
            void Receive(object source,DataReceivedEventArgs line)
            {
                if(string.IsNullOrWhiteSpace(line.Data)) return;
                lock(logs) logs.Add(line.Data);
                if(!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(()=>Status.Text=line.Data);
            }
            generator.OutputDataReceived+=Receive; generator.ErrorDataReceived+=Receive;
            GenerateButton.IsEnabled=false; CancelButton.IsEnabled=true; ResultButton.IsEnabled=false; UnrealExportButton.IsEnabled=false;
            generator.Start(); generator.BeginOutputReadLine(); generator.BeginErrorReadLine();
            await generator.WaitForExitAsync(); generator.WaitForExit();
            generationLogFolder=folder; GenerationLogButton.IsEnabled=true;
            lock(logs) File.WriteAllLines(Path.Combine(folder,"generation.log"),logs);
            lock(logs) SetGenerationOutcome(folder,generator.ExitCode,cancelled,logs);

        }
        catch(Exception error)
        {
            lock(logs) logs.Add("Generation failed: "+error);
            if(Directory.Exists(folder))
            {
                try {lock(logs) File.WriteAllLines(Path.Combine(folder,"generation.log"),logs);} catch(IOException) {}
                GenerationLogButton.IsEnabled=File.Exists(Path.Combine(folder,"generation.log"));
            }
            ShowError(error);
        }
        finally {generator?.Dispose(); generator=null; GenerateButton.IsEnabled=true; CancelButton.IsEnabled=false; UnrealExportButton.IsEnabled=true; ResultButton.IsEnabled=generationLogFolder!=null && Directory.Exists(generationLogFolder);}
    }
    private void SetGenerationOutcome(string folder,int exitCode,bool wasCancelled,IReadOnlyList<string> logs)
    {
        generationLogFolder=folder;
        GenerationLogButton.IsEnabled=File.Exists(Path.Combine(folder,"generation.log"));
        ResultButton.IsEnabled=Directory.Exists(folder);
        if(wasCancelled) Status.Text="Generation cancelled. Your layers and skeleton are preserved.";
        else if(exitCode==0 && GenerationOutputReady(folder))
        {
            resultFolder=folder;
            Status.Text=File.Exists(Path.Combine(folder,"effect.json")) ? "Saved Niagara VFX as asset.meshdrawasset. Import it with the updated Unreal plugin. Open output folder to find your files." : "Saved asset.meshdrawasset for Unreal and Blender. "+OutputFitSummary(folder)+"Open output folder to find your files.";
        }
        else
        {
            var failure=logs.LastOrDefault(l=>l.StartsWith("Generation failed:",StringComparison.OrdinalIgnoreCase));
            Status.Text=(failure ?? "Generation failed before the complete asset was saved.")+" Click View generation log for details.";
        }
    }
    private static string OutputFitSummary(string folder)
    {
        try
        {
            using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"result.json")));
            if(result.RootElement.TryGetProperty("drawingFit",out var fit) && fit.ValueKind==JsonValueKind.Object && fit.TryGetProperty("meanIoU",out var value) && value.TryGetDouble(out var score) && double.IsFinite(score) && score>=0 && score<=1)
                return "Drawing silhouette match: "+(score*100).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"% before export. ";
        }
        catch(IOException) {} catch(JsonException) {}
        return "";
    }
    private static bool GenerationOutputReady(string folder) => new[]{"asset.meshdrawasset","result.json"}.All(name=>File.Exists(Path.Combine(folder,name)) && new FileInfo(Path.Combine(folder,name)).Length>0) && (File.Exists(Path.Combine(folder,"mesh.fbx")) && new FileInfo(Path.Combine(folder,"mesh.fbx")).Length>0 || File.Exists(Path.Combine(folder,"effect.json")) && File.Exists(Path.Combine(folder,"vfx_sprite.png")) && new FileInfo(Path.Combine(folder,"effect.json")).Length>0 && new FileInfo(Path.Combine(folder,"vfx_sprite.png")).Length>0);
    private void OpenGenerationLog(object sender,RoutedEventArgs e)
    {
        if(generationLogFolder!=null) Process.Start(new ProcessStartInfo("notepad.exe") {ArgumentList={Path.Combine(generationLogFolder,"generation.log")},UseShellExecute=true});
    }
    private void StopGenerator() {if(generator is {HasExited:false}) generator.Kill(true);}
    private void CancelGeneration(object sender,RoutedEventArgs e) {cancelled=true; StopGenerator();}
    private void OpenResult(object sender,RoutedEventArgs e)
    {
        var folder=generationLogFolder ?? resultFolder;
        if(folder!=null) Process.Start(new ProcessStartInfo("explorer.exe") {ArgumentList={folder},UseShellExecute=true});
    }
    private async void ExportUnreal(object sender,RoutedEventArgs e)
    {
        string? folder=resultFolder;
        if(folder==null)
        {
            var choose=new OpenFolderDialog {Title="Choose a generated MeshDraw output folder",InitialDirectory=Path.Combine(projectRoot,"outputs")};
            if(choose.ShowDialog(this)!=true) return;
            folder=choose.FolderName;
        }
        var destination=new SaveFileDialog {Filter="MeshDraw Unreal asset|*.meshdrawasset",FileName="Object.meshdrawasset"};
        if(destination.ShowDialog(this)!=true) return;
        UnrealExportButton.IsEnabled=false;
        try
        {
            var start=new ProcessStartInfo(Path.Combine(projectRoot,"local-ai","venv","Scripts","python.exe")) {
                WorkingDirectory=projectRoot,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[] {Path.Combine(projectRoot,"backend","package_asset.py"),"--folder",folder,"--output",destination.FileName}) start.ArgumentList.Add(argument);
            using var process=Process.Start(start) ?? throw new IOException("Could not start the local exporter.");
            var errors=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();var errorText=await errors;await output;
            if(process.ExitCode!=0) throw new IOException(errorText);
            Status.Text="Exported a complete Unreal file: "+destination.FileName;
        }
        catch(Exception error) {ShowError(error);}
        finally {UnrealExportButton.IsEnabled=true;}
    }
}
